using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace UniLink.Core;

public enum ReplyKind { Online, SignedOut, Quota, Credentials, Challenge, Unknown, LiveOff }
public sealed record PortalReply(ReplyKind Kind, string Message = "");
public sealed record CertificateInfo(string PortalId, string Url, string Fingerprint, string Subject, string Issuer, DateTime Expires, bool Changed);
public sealed class CertificateApprovalException(CertificateInfo certificate) : Exception("Portal certificate requires approval.")
{
    public CertificateInfo Certificate { get; } = certificate;
}
public sealed class PortalTransportException(string category) : Exception(category);

public interface IPortalSession : IDisposable
{
    Task ProbeAsync(CancellationToken ct);
    Task<PortalReply> LoginAsync(Account account, NetworkGroup group, CancellationToken ct);
    Task<PortalReply> HeartbeatAsync(Account account, NetworkGroup group, CancellationToken ct);
    Task LogoutAsync(Account account, CancellationToken ct);
}
public interface IPortalFactory { IPortalSession Create(PortalEndpoint endpoint, NetworkLink link); }
public sealed class PortalFactory(Func<NetworkLink, bool>? verifyInterface = null) : IPortalFactory
{
    public IPortalSession Create(PortalEndpoint endpoint, NetworkLink link) => new PortalSession(endpoint, link, verifyInterface);
}

public sealed class PortalSession : IPortalSession
{
    private readonly HttpClient _http;
    private readonly PortalEndpoint _endpoint;
    private CertificateInfo? _certificate;

    public PortalSession(PortalEndpoint endpoint, NetworkLink link, Func<NetworkLink, bool>? verifyInterface = null)
    {
        _endpoint = endpoint;
        var handler = new SocketsHttpHandler
        {
            UseProxy = false, AllowAutoRedirect = false, CookieContainer = new CookieContainer(), UseCookies = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ConnectTimeout = TimeSpan.FromSeconds(7), PooledConnectionLifetime = TimeSpan.FromMinutes(4),
            SslOptions = new SslClientAuthenticationOptions { EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }
        };
        handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
        {
            if (certificate is null) return false;
            using var cert = new X509Certificate2(certificate);
            var hash = Convert.ToHexString(SHA256.HashData(cert.RawData));
            if (_endpoint.CertificateSha256.Length > 0 && string.Equals(hash, _endpoint.CertificateSha256, StringComparison.OrdinalIgnoreCase)) return true;
            // Even a public CA certificate is confirmed once. This also makes portal-only detection safe.
            _certificate = new CertificateInfo(endpoint.Id, endpoint.Url, hash, cert.Subject, cert.Issuer, cert.NotAfter,
                endpoint.CertificateSha256.Length > 0);
            return false;
        };
        handler.ConnectCallback = async (context, ct) =>
        {
            var local = IPAddress.Parse(link.LocalAddress);
            if (verifyInterface?.Invoke(link) == false) throw new HttpRequestException("Selected physical interface is no longer available.");
            if (OperatingSystem.IsWindows() && (link.InterfaceIndex <= 0 || link.InterfaceIndex > 0x00ffffff))
                throw new HttpRequestException("A verified base interface index is required; the default route is never used.");
            var addresses = await InterfaceDns.ResolveAsync(context.DnsEndPoint.Host, link.InterfaceIndex, ct);
            Exception? last = null;
            foreach (var remote in addresses.Where(a => a.AddressFamily == local.AddressFamily))
            {
                if (verifyInterface?.Invoke(link) == false) throw new HttpRequestException("Selected physical interface changed before connecting.");
                var socket = new Socket(local.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    socket.Bind(new IPEndPoint(local, 0));
                    if (OperatingSystem.IsWindows() && local.AddressFamily == AddressFamily.InterNetwork && link.InterfaceIndex > 0)
                        socket.SetSocketOption(SocketOptionLevel.IP, (SocketOptionName)31, IPAddress.HostToNetworkOrder(link.InterfaceIndex)); // IP_UNICAST_IF
                    await socket.ConnectAsync(new IPEndPoint(remote, context.DnsEndPoint.Port), ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception ex) when (ex is SocketException or OperationCanceledException)
                {
                    socket.Dispose(); last = ex;
                    if (ct.IsCancellationRequested) throw;
                }
            }
            throw new HttpRequestException("No reachable portal address on this interface.", last);
        };
        _http = new HttpClient(handler) { BaseAddress = new Uri(endpoint.Url), Timeout = TimeSpan.FromSeconds(12), MaxResponseContentBufferSize = 262144 };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("UniLink/1.2.1 (Windows; University Wi-Fi Assistant)");
        _http.DefaultRequestHeaders.Referrer = new Uri(endpoint.Url);
        _http.DefaultRequestHeaders.Accept.ParseAdd("*/*");
        _http.DefaultRequestHeaders.CacheControl = new() { NoCache = true, NoStore = true };
    }

    private static string Timestamp() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
    public async Task ProbeAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "");
        var body = await SendAsync(request, ct);
        if (!(body.Contains("httpclient.js", StringComparison.OrdinalIgnoreCase) || body.Contains("keepaliverequest", StringComparison.OrdinalIgnoreCase) || body.Contains("login.xml", StringComparison.OrdinalIgnoreCase)))
            throw new PortalTransportException("Unsupported portal (Sophos/Cyberoam web-client page was not found).");
    }

    public async Task<PortalReply> LoginAsync(Account account, NetworkGroup group, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "login.xml")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["mode"] = "191", ["username"] = account.Username.Replace("'", "''"), ["password"] = account.Password,
                ["a"] = Timestamp(), ["producttype"] = "0"
            })
        };
        request.Headers.TryAddWithoutValidation("Origin", _http.BaseAddress!.GetLeftPart(UriPartial.Authority));
        return Parse(await SendAsync(request, ct), false, group);
    }

    public async Task<PortalReply> HeartbeatAsync(Account account, NetworkGroup group, CancellationToken ct)
    {
        var query = "live?mode=192&username=" + Uri.EscapeDataString(account.Username.ToLowerInvariant()) + "&a=" + Timestamp() + "&producttype=0";
        using var request = new HttpRequestMessage(HttpMethod.Get, query);
        return Parse(await SendAsync(request, ct), true, group);
    }

    public async Task LogoutAsync(Account account, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "logout.xml")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            { ["mode"] = "193", ["username"] = account.Username.Replace("'", "''"), ["a"] = Timestamp(), ["producttype"] = "0" })
        };
        request.Headers.TryAddWithoutValidation("Origin", _http.BaseAddress!.GetLeftPart(UriPartial.Authority));
        _ = await SendAsync(request, ct);
    }

    private async Task<string> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        _certificate = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        var requestToken = timeout.Token;
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken);
            if (!response.IsSuccessStatusCode) throw new PortalTransportException("Portal HTTP status " + (int)response.StatusCode + ". Redirects are never followed.");
            if (response.Content.Headers.ContentLength > 262144) throw new PortalTransportException("Portal response exceeds the size limit.");
            using var stream = await response.Content.ReadAsStreamAsync(requestToken);
            using var buffer = new MemoryStream();
            var bytes = new byte[4096]; int n;
            while ((n = await stream.ReadAsync(bytes, requestToken)) > 0)
            {
                if (buffer.Length + n > 262144) throw new PortalTransportException("Portal response exceeds the size limit.");
                buffer.Write(bytes, 0, n);
            }
            return Encoding.UTF8.GetString(buffer.ToArray());
        }
        catch (HttpRequestException) when (_certificate is not null) { throw new CertificateApprovalException(_certificate); }
        catch (HttpRequestException) { throw new PortalTransportException("Portal connection failed (network, TLS or DNS)."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new PortalTransportException("Portal request timed out."); }
        catch (IOException) { throw new PortalTransportException("Portal response was interrupted."); }
    }

    public static PortalReply Parse(string xml, bool live, NetworkGroup group)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 262144 });
            var doc = XDocument.Load(reader);
            var expected = live ? "liverequestresponse" : "requestresponse";
            if (doc.Root?.Name.LocalName != expected) return new(ReplyKind.Unknown, "Unexpected portal XML.");
            string Value(string name) => doc.Root.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() ?? "";
            var status = Value(live ? "ack" : "status");
            var message = Value(live ? "livemessage" : "message");
            // An explicit successful status wins over words present in a success message.
            if (status.Equals(live ? "ack" : "LIVE", StringComparison.OrdinalIgnoreCase)) return new(ReplyKind.Online, message);
            if (message.Contains("your data transfer has been exceeded", StringComparison.OrdinalIgnoreCase)
                || group.QuotaPhrases.Any(p => p.Length > 0 && message.Contains(p, StringComparison.OrdinalIgnoreCase))) return new(ReplyKind.Quota, message);
            if (group.CredentialPhrases.Any(p => p.Length > 0 && message.Contains(p, StringComparison.OrdinalIgnoreCase))) return new(ReplyKind.Credentials, message);
            if (!live && status.Equals("CHALLENGE", StringComparison.OrdinalIgnoreCase)) return new(ReplyKind.Challenge, "Interactive authentication is required. Open the portal manually.");
            if (live && status == "live_off") return new(ReplyKind.LiveOff, message);
            if (live && status is "nack" or "login_again") return new(ReplyKind.SignedOut, message);
            return new(ReplyKind.Unknown, message.Length == 0 ? "Unrecognized authentication response." : message);
        }
        catch (XmlException) { return new(ReplyKind.Unknown, "Invalid XML from portal."); }
    }
    public void Dispose() => _http.Dispose();
}
