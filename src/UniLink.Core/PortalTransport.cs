using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Net.Http.Headers;

namespace UniLink.Core;

internal sealed record PortalDocument(string Body, HttpResponseHeaders Headers);

// Each session has its own cookies and uses the verified base network interface.
internal sealed class PortalTransport : IDisposable
{
    private readonly HttpClient _http;
    private readonly int _maxResponseBytes;
    private CertificateInfo? _certificate;

    public Uri BaseAddress => _http.BaseAddress!;

    public PortalTransport(PortalEndpoint endpoint, NetworkLink link, Func<NetworkLink, bool>? verifyInterface, int maxResponseBytes = 262144)
    {
        _maxResponseBytes = maxResponseBytes;
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
            if (endpoint.CertificateSha256.Length > 0 && string.Equals(hash, endpoint.CertificateSha256, StringComparison.OrdinalIgnoreCase)) return true;
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
        _http = new HttpClient(handler) { BaseAddress = new Uri(endpoint.Url), Timeout = TimeSpan.FromSeconds(12), MaxResponseContentBufferSize = maxResponseBytes };
        var version = typeof(PortalTransport).Assembly.GetName().Version?.ToString(3) ?? "1.0";
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"UniLink/{version} (Windows; University Wi-Fi Assistant)");
        _http.DefaultRequestHeaders.Referrer = new Uri(endpoint.Url);
        _http.DefaultRequestHeaders.Accept.ParseAdd("*/*");
        _http.DefaultRequestHeaders.CacheControl = new() { NoCache = true, NoStore = true };
    }

    public async Task<PortalDocument> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var address = request.RequestUri is null ? BaseAddress : new Uri(BaseAddress, request.RequestUri);
        if (!address.GetLeftPart(UriPartial.Authority).Equals(BaseAddress.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
            throw new PortalTransportException("Requests must stay on the configured portal server.");

        _certificate = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        var requestToken = timeout.Token;
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken);
            if (!response.IsSuccessStatusCode) throw new PortalTransportException("Portal HTTP status " + (int)response.StatusCode + ". Redirects are never followed.");
            if (response.Content.Headers.ContentLength > _maxResponseBytes) throw new PortalTransportException("Portal response exceeds the size limit.");
            using var stream = await response.Content.ReadAsStreamAsync(requestToken);
            using var buffer = new MemoryStream();
            var bytes = new byte[4096]; int n;
            while ((n = await stream.ReadAsync(bytes, requestToken)) > 0)
            {
                if (buffer.Length + n > _maxResponseBytes) throw new PortalTransportException("Portal response exceeds the size limit.");
                buffer.Write(bytes, 0, n);
            }
            return new(Encoding.UTF8.GetString(buffer.ToArray()), response.Headers);
        }
        catch (HttpRequestException) when (_certificate is not null) { throw new CertificateApprovalException(_certificate); }
        catch (HttpRequestException) { throw new PortalTransportException("Portal connection failed (network, TLS or DNS)."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new PortalTransportException("Portal request timed out."); }
        catch (IOException) { throw new PortalTransportException("Portal response was interrupted."); }
    }

    public void Dispose() => _http.Dispose();
}
