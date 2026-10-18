using System.Text.Json;

namespace UniLink.Core;

public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public bool AutoConnect { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public List<NetworkGroup> Groups { get; set; } = [];
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public AppConfig Clone() => JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;
    public static AppConfig CreateDefault() => new()
    {
        Groups = [new NetworkGroup { Name = "Urmia University", Ssids = ["UUWN"], Portals = [new PortalEndpoint { Url = "https://192.168.100.40:8090/" }] }]
    };

    public void Validate()
    {
        if (Version != 1) throw new InvalidDataException("Unsupported settings version.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in Groups)
        {
            if (!ids.Add(g.Id) || string.IsNullOrWhiteSpace(g.Name)) throw new InvalidDataException("Group names and IDs must be valid.");
            if (g.Portals.Count == 0 || g.Ssids.Count == 0 && !g.MatchByPortal) throw new InvalidDataException("Add an SSID, or explicitly enable portal-only matching.");
            if (g.Ssids.Any(s => string.IsNullOrEmpty(s) || System.Text.Encoding.UTF8.GetByteCount(s) > 32)) throw new InvalidDataException("An SSID must contain 1 to 32 UTF-8 bytes.");
            if (g.HeartbeatSeconds is < 30 or > 180) throw new InvalidDataException("Heartbeat interval must be between 30 and 180 seconds.");
            _ = TimeZoneInfo.FindSystemTimeZoneById(g.TimeZoneId);
            foreach (var p in g.Portals)
            {
                if (!ids.Add(p.Id)) throw new InvalidDataException("Duplicate portal ID.");
                p.Url = PortalEndpoint.NormalizeUrl(p.Url);
                if (p.CertificateSha256.Length != 0 && (p.CertificateSha256.Length != 64 || !p.CertificateSha256.All(Uri.IsHexDigit))) throw new InvalidDataException("Invalid certificate fingerprint.");
            }
            foreach (var a in g.Accounts)
            {
                if (!ids.Add(a.Id) || string.IsNullOrWhiteSpace(a.Label) || string.IsNullOrWhiteSpace(a.Username) || string.IsNullOrEmpty(a.Password)) throw new InvalidDataException("Each account needs a name, username and password.");
            }
            if (g.DefaultAccountId is not null && !g.Accounts.Any(a => a.Id == g.DefaultAccountId)) g.DefaultAccountId = null;
            if (g.QuotaPhrases.Any(string.IsNullOrWhiteSpace) || g.CredentialPhrases.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("Error phrases cannot be blank.");
        }
    }

    public AppConfig NetworkTemplate()
    {
        var template = Clone();
        foreach (var g in template.Groups)
        {
            g.Accounts.Clear(); g.DefaultAccountId = null; g.LastMonth = "";
            foreach (var p in g.Portals) p.CertificateSha256 = "";
        }
        return template;
    }
}

public sealed class NetworkGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New network";
    public bool Enabled { get; set; } = true;
    public bool MatchByPortal { get; set; }
    public bool AutoSwitch { get; set; } = true;
    public int HeartbeatSeconds { get; set; } = 180;
    public string TimeZoneId { get; set; } = "Iran Standard Time";
    public List<string> Ssids { get; set; } = [];
    public List<PortalEndpoint> Portals { get; set; } = [];
    public List<Account> Accounts { get; set; } = [];
    public string? DefaultAccountId { get; set; }
    public string LastMonth { get; set; } = "";
    public List<string> QuotaPhrases { get; set; } =
    [
        "your data transfer has been exceeded", "data transfer limit", "data transfer quota", "maximum data transfer", "data usage limit",
        "internet usage limit", "quota exceeded", "quota exhausted", "traffic quota exhausted",
        "حجم شما به پایان", "حجم اینترنت شما تمام", "اتمام حجم", "سهمیه شما به پایان"
    ];
    public List<string> CredentialPhrases { get; set; } =
    ["invalid username/password", "invalid username or password", "incorrect password", "wrong password", "invalid credentials", "نام کاربری یا رمز عبور اشتباه"];

    public string MonthAt(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId)).ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
    public DateTimeOffset NextReset(DateTimeOffset utc)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        var local = TimeZoneInfo.ConvertTime(utc, zone);
        var next = new DateTime(local.Year, local.Month, 1).AddMonths(1);
        return new DateTimeOffset(next, zone.GetUtcOffset(next));
    }
    public DateTimeOffset? QuotaProbeAfter(DateTimeOffset utc)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        var local = TimeZoneInfo.ConvertTime(utc, zone); var due = utc.AddHours(1);
        // A university's reset job may run after midnight. Probe at most hourly on day 1.
        return local.Day == 1 && TimeZoneInfo.ConvertTime(due, zone).Day == 1 ? due : null;
    }
    public IEnumerable<Account> OrderedAccounts() => Accounts.Where(a => a.Enabled).OrderBy(a => a.Id == DefaultAccountId ? 0 : 1);
}

public sealed class PortalEndpoint
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Url { get; set; } = "";
    public string CertificateSha256 { get; set; } = "";
    public static string NormalizeUrl(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var u) || u.Scheme != "https" || !string.IsNullOrEmpty(u.UserInfo) || !string.IsNullOrEmpty(u.Query) || !string.IsNullOrEmpty(u.Fragment))
            throw new InvalidDataException("Enter an HTTPS portal URL without credentials, query or fragment.");
        // Relative protocol endpoints are resolved under this directory.
        var path = u.AbsolutePath.EndsWith('/') ? u.AbsolutePath : u.AbsolutePath + "/";
        return u.GetLeftPart(UriPartial.Authority) + path;
    }
}

public sealed class Account
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string ExhaustedMonth { get; set; } = "";
    public DateTimeOffset? QuotaNextProbeAt { get; set; }
    public bool NeedsAttention { get; set; }
    public bool Eligible(string month, DateTimeOffset? now = null) => Enabled && !NeedsAttention &&
        (string.IsNullOrEmpty(ExhaustedMonth) || string.CompareOrdinal(ExhaustedMonth, month) < 0 ||
         ExhaustedMonth == month && now is { } current && QuotaNextProbeAt is { } retry && current >= retry);
}

public sealed record NetworkLink(string InterfaceId, string? Ssid, string LocalAddress, int InterfaceIndex, string Signature);
public sealed record NetworkSnapshot(IReadOnlyList<NetworkLink> Links, bool LocationDenied = false, string? Problem = null);
public interface IConfigStore { void Save(AppConfig config); }

public sealed class ActivityLog
{
    private readonly List<string> _lines = [];
    public event Action<string>? Added;
    public void Write(string message)
    {
        // Callers provide fixed messages or sanitized server text; no URLs with query strings.
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}  {message}";
        lock (_lines) { _lines.Add(line); if (_lines.Count > 800) _lines.RemoveAt(0); }
        Added?.Invoke(line);
    }
    public string Export() { lock (_lines) return string.Join(Environment.NewLine, _lines); }
    public static string Sanitize(string message, AppConfig config)
    {
        var text = System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(message, "<[^>]*>", "", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100)));
        foreach (var secret in config.Groups.SelectMany(g => g.Accounts).SelectMany(a => new[] { a.Username, a.Password }).Where(s => s.Length > 0).Distinct().OrderByDescending(s => s.Length))
            foreach (var value in new[] { secret, Uri.EscapeDataString(secret), System.Net.WebUtility.UrlEncode(secret) }.Distinct().OrderByDescending(s => s.Length))
                text = text.Replace(value, "[hidden]", StringComparison.OrdinalIgnoreCase);
        text = new string(text.Where(c => !char.IsControl(c)).Take(400).ToArray());
        return text;
    }
}
