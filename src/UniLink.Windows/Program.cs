using System.Security.Principal;
using UniLink.Core;

namespace UniLink.Windows;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length >= 2 && args[0] == "--ui-check") { Environment.ExitCode = UiChecks.Run(args[1]); return; }
        var user = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        using var mutex = new Mutex(true, @"Local\UniLink.Instance." + user, out var first);
        using var show = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\UniLink.Show." + user);
        if (!first) { show.Set(); return; }
        try
        {
            var store = new EncryptedConfigStore(); var config = store.Load();
            var log = new ActivityLog();
            if (store.RecoveredBackup) log.Write("Settings recovered from the previous encrypted backup.");
            using var network = new WindowsNetworkMonitor();
            using var engine = new ConnectionEngine(config, store, new PortalFactory(WindowsNetworkMonitor.ValidateBaseLink), log);
            using var form = new MainForm(engine, network, log, args.Contains("--background"));
            var registration = ThreadPool.RegisterWaitForSingleObject(show, (_, _) =>
            {
                if (form.IsHandleCreated && !form.IsDisposed) form.BeginInvoke(form.ShowWindow);
            }, null, -1, false);
            try { Application.Run(form); }
            finally { registration.Unregister(null); }
        }
        catch (Exception ex)
        {
            var detail = ex is InvalidDataException ? ex.Message : "UniLink could not start (" + ex.GetType().Name + "). Your saved settings have not been reset.";
            MessageBox.Show(detail, "UniLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
