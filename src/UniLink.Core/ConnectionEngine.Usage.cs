namespace UniLink.Core;

public sealed class UsageTarget(string groupId, string accountId, string username, string password, PortalEndpoint portal, NetworkLink link)
{
    public string GroupId { get; } = groupId;
    public string AccountId { get; } = accountId;
    public string Username { get; } = username;
    public string Password { get; } = password;
    public PortalEndpoint Portal { get; } = portal;
    public NetworkLink Link { get; } = link;
}

public sealed partial class ConnectionEngine
{
    public async Task<UsageTarget?> GetUsageTargetAsync(string groupId, string accountId, bool activeOnly, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var group = _config.Groups.FirstOrDefault(g => g.Id == groupId && g.Enabled);
            var account = group?.Accounts.FirstOrDefault(a => a.Id == accountId && a.Enabled && !a.NeedsAttention);
            if (group?.UsagePortalUrl is not { Length: > 0 } url || account is null || _networkChanged) return null;
            var active = _active?.GroupId == groupId ? _active : null;
            if (activeOnly && (active?.AccountId != accountId || Status.State != ConnectionState.Online)) return null;
            var link = active?.Link ?? _lastNetwork.Links.FirstOrDefault(l => l.Ssid is not null && group.Ssids.Contains(l.Ssid, StringComparer.Ordinal));
            if (link is null) return null;

            var portal = new PortalEndpoint { Id = groupId, Url = url, CertificateSha256 = group.UsageCertificateSha256 };
            return new(groupId, accountId, account.Username, account.Password, portal, link);
        }
        finally { _gate.Release(); }
    }

    public async Task SaveUsageAsync(UsageTarget target, AccountUsage usage, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            var replacement = _config.Clone();
            var group = replacement.Groups.FirstOrDefault(g => g.Id == target.GroupId && g.Enabled);
            if (group is null || group.UsagePortalUrl != target.Portal.Url || group.UsageCertificateSha256 != target.Portal.CertificateSha256) return;
            var account = group.Accounts.FirstOrDefault(a => a.Id == target.AccountId && a.Enabled && !a.NeedsAttention &&
                a.Username == target.Username && a.Password == target.Password);
            if (account is null) return;

            account.LastUsage = usage;
            _store.Save(replacement);
            _config = replacement;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> ApproveUsageCertificateAsync(CertificateInfo certificate, string previousFingerprint, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var replacement = _config.Clone();
            var group = replacement.Groups.FirstOrDefault(g => g.Id == certificate.PortalId && g.Enabled);
            if (group is null || group.UsagePortalUrl != certificate.Url || group.UsageCertificateSha256 != previousFingerprint)
                return false;

            group.UsageCertificateSha256 = certificate.Fingerprint;
            foreach (var account in group.Accounts) account.LastUsage = null;
            _store.Save(replacement);
            _config = replacement;
            return true;
        }
        finally { _gate.Release(); }
    }
}
