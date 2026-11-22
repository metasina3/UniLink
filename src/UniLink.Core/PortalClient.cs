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
    private readonly PortalTransport _transport;

    public PortalSession(PortalEndpoint endpoint, NetworkLink link, Func<NetworkLink, bool>? verifyInterface = null)
    {
        _transport = new PortalTransport(endpoint, link, verifyInterface);
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
        request.Headers.TryAddWithoutValidation("Origin", _transport.BaseAddress.GetLeftPart(UriPartial.Authority));
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
        request.Headers.TryAddWithoutValidation("Origin", _transport.BaseAddress.GetLeftPart(UriPartial.Authority));
        _ = await SendAsync(request, ct);
    }

    private async Task<string> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        (await _transport.SendAsync(request, ct)).Body;

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
    public void Dispose() => _transport.Dispose();
}
