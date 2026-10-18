using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using UniLink.Core;
using Windows.Networking.Connectivity;

namespace UniLink.Windows;

public sealed class EncryptedConfigStore : IConfigStore
{
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UniLink");
    private readonly string _path = Path.Combine(DataDirectory, "settings.dpapi");
    public bool RecoveredBackup { get; private set; }
    public AppConfig Load()
    {
        Directory.CreateDirectory(DataDirectory);
        if (!File.Exists(_path) && !File.Exists(_path + ".bak")) return AppConfig.CreateDefault();
        try { return Read(_path); }
        catch (Exception ex) when (ex is IOException or CryptographicException or JsonException or InvalidDataException)
        {
            if (File.Exists(_path + ".bak"))
            {
                var previous = Read(_path + ".bak"); RecoveredBackup = true;
                File.Copy(_path + ".bak", _path, true); return previous;
            }
            throw new InvalidDataException("Saved settings could not be decrypted. They were not replaced. Use the same Windows account or restore a backup.", ex);
        }
    }
    private static AppConfig Read(string path)
    {
        var bytes = Dpapi.Unprotect(File.ReadAllBytes(path));
        try
        {
            var config = JsonSerializer.Deserialize<AppConfig>(bytes, AppConfig.JsonOptions) ?? throw new InvalidDataException("Empty settings.");
            config.Validate(); return config;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public void Save(AppConfig config)
    {
        config.Validate(); Directory.CreateDirectory(DataDirectory);
        var plain = JsonSerializer.SerializeToUtf8Bytes(config, AppConfig.JsonOptions);
        byte[] encrypted;
        try { encrypted = Dpapi.Protect(plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
        var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(encrypted); file.Flush(true); }
            if (File.Exists(_path)) File.Replace(temp, _path, _path + ".bak");
            else File.Move(temp, _path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static class Dpapi
    {
        [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
        public static byte[] Protect(byte[] data) => Transform(data, true);
        public static byte[] Unprotect(byte[] data) => Transform(data, false);
        private static byte[] Transform(byte[] data, bool protect)
        {
            var input = new Blob { Length = data.Length, Data = Marshal.AllocHGlobal(data.Length) };
            Blob output = default;
            try
            {
                Marshal.Copy(data, 0, input.Data, data.Length);
                var ok = protect ? CryptProtectData(ref input, "UniLink settings", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
                if (!ok) throw new CryptographicException(new Win32Exception(Marshal.GetLastWin32Error()).Message);
                var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
            }
            finally
            {
                for (var i = 0; i < data.Length; i++) Marshal.WriteByte(input.Data, i, 0);
                Marshal.FreeHGlobal(input.Data);
                if (output.Data != IntPtr.Zero)
                {
                    for (var i = 0; i < output.Length; i++) Marshal.WriteByte(output.Data, i, 0);
                    _ = LocalFree(output.Data);
                }
            }
        }
    }
}

public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool Enabled
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue("UniLink") is string; }
    }
    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue("UniLink", "\"" + Environment.ProcessPath + "\" --background", RegistryValueKind.String);
        else key.DeleteValue("UniLink", false);
    }
}

public sealed class WindowsNetworkMonitor : IDisposable
{
    public event Action? Changed;
    private IntPtr _wlan;
    private readonly NotificationCallback _callback;
    private long _generation;
    [StructLayout(LayoutKind.Sequential)] private struct Notification { public uint Source; public uint Code; public Guid Interface; public uint Size; public IntPtr Data; }
    private delegate void NotificationCallback(ref Notification notification, IntPtr context);
    [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out IntPtr handle);
    [DllImport("wlanapi.dll")] private static extern uint WlanRegisterNotification(IntPtr handle, uint source, [MarshalAs(UnmanagedType.Bool)] bool ignoreDuplicates, NotificationCallback? callback, IntPtr context, IntPtr reserved, out uint previous);
    [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);

    public WindowsNetworkMonitor()
    {
        _callback = OnWlanNotification;
        if (WlanOpenHandle(2, IntPtr.Zero, out _, out _wlan) == 0)
            _ = WlanRegisterNotification(_wlan, 8, true, _callback, IntPtr.Zero, IntPtr.Zero, out _); // ACM only; no scan/BSSID query
        NetworkChange.NetworkAddressChanged += OnAddressChanged;
    }
    private void OnAddressChanged(object? sender, EventArgs e) => Signal();
    private void OnWlanNotification(ref Notification notification, IntPtr context)
    {
        // ACM connection_complete=10, disconnected=21. Avoid resetting on signal-strength notifications.
        if (notification.Code is 10 or 21) Signal();
    }
    private void Signal() { Interlocked.Increment(ref _generation); Changed?.Invoke(); }

    public static bool ValidateBaseLink(NetworkLink link) => PhysicalNetworkInterface.TryRead(link.InterfaceIndex, out var facts)
        && PhysicalNetworkInterface.IsEligible(facts) && Guid.TryParse(link.InterfaceId, out var id) && facts.Id == id;

    public NetworkSnapshot Read()
    {
        var ssids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); var denied = false;
        try
        {
            // Microsoft's SSID-only API avoids querying location-sensitive BSSID information.
            foreach (var profile in NetworkInformation.GetConnectionProfiles())
            {
                if (!profile.IsWlanConnectionProfile || profile.NetworkAdapter is null || profile.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.None) continue;
                var ssid = profile.WlanConnectionProfileDetails.GetConnectedSsid();
                if (!string.IsNullOrEmpty(ssid)) ssids[profile.NetworkAdapter.NetworkAdapterId.ToString("D")] = ssid;
            }
        }
        catch (UnauthorizedAccessException) { denied = true; }
        catch (COMException ex) { denied = ex.HResult == unchecked((int)0x80070005); }
        var links = new List<NetworkLink>();
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().OrderBy(a => a.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 1))
            {
                if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType is not (NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet)) continue;
                var properties = adapter.GetIPProperties();
                var index = properties.GetIPv4Properties()?.Index ?? 0;
                if (!PhysicalNetworkInterface.TryRead(index, out var facts) || !PhysicalNetworkInterface.IsEligible(facts)
                    || !Guid.TryParse(adapter.Id, out var adapterGuid) || facts.Id != adapterGuid) continue;
                var local = properties.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal));
                if (local is null) continue;
                var id = adapter.Id.Trim('{', '}');
                ssids.TryGetValue(id, out var ssid);
                var gateway = properties.GatewayAddresses.FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? "";
                var signature = $"{id}|{ssid}|{local.Address}|{gateway}|{Interlocked.Read(ref _generation)}";
                links.Add(new NetworkLink(id, ssid, local.Address.ToString(), index, signature));
            }
        }
        catch (NetworkInformationException) { return new([], denied, "Windows network information is temporarily unavailable."); }
        return new(links, denied, links.Count == 0 ? "No verified physical Wi-Fi or Ethernet adapter with IPv4 is available. VPN and virtual adapters are not used." : null);
    }
    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= OnAddressChanged;
        if (_wlan != IntPtr.Zero)
        {
            _ = WlanRegisterNotification(_wlan, 0, true, null, IntPtr.Zero, IntPtr.Zero, out _);
            _ = WlanCloseHandle(_wlan, IntPtr.Zero); _wlan = IntPtr.Zero;
        }
    }
}
