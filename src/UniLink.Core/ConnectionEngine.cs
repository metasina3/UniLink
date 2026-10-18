namespace UniLink.Core;

public enum ConnectionState { Waiting, Paused, Connecting, Online, Retry, NoAccounts, Certificate, Attention }
public sealed record EngineStatus(ConnectionState State, string Detail, string Ssid = "", string Group = "", string Account = "", string Portal = "", DateTimeOffset? NextAction = null, DateTimeOffset? LastHeartbeat = null);

public sealed class ConnectionEngine : IDisposable
{
    private AppConfig _config;
    private readonly IConfigStore _store;
    private readonly IPortalFactory _factory;
    private readonly ActivityLog _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _cancelLock = new();
    private CancellationTokenSource? _operation;
    private volatile bool _networkChanged;
    private ActiveSession? _active;
    private NetworkSnapshot _lastNetwork = new([]);
    private DateTimeOffset _nextAttempt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastTick = DateTimeOffset.MinValue;
    private int _failures;
    private bool _paused;
    private string? _manualGroup;
    private string? _manualSignature;
    private string? _retryAccount;
    private string _networkSignature = "";
    private readonly Dictionary<string, int> _endpointCursor = [];
    private readonly HashSet<string> _interactiveBlocked = [];
    private int _portalOnlyCursor;
    public EngineStatus Status { get; private set; } = new(ConnectionState.Waiting, "Waiting for a configured network.");
    public CertificateInfo? PendingCertificate { get; private set; }
    public bool Paused => _paused;
    public AppConfig SnapshotConfig() => _config.Clone();

    private sealed record ActiveSession(string GroupId, string PortalId, string AccountId, NetworkLink Link, IPortalSession Client)
    {
        public DateTimeOffset NextHeartbeat { get; set; }
        public DateTimeOffset? LastHeartbeat { get; set; }
        public bool LiveOff { get; set; }
    }

    public ConnectionEngine(AppConfig config, IConfigStore store, IPortalFactory factory, ActivityLog log)
    {
        config.Validate(); _config = config.Clone(); _store = store; _factory = factory; _log = log;
    }

    public void NetworkChanged()
    {
        _networkChanged = true;
        lock (_cancelLock) _operation?.Cancel();
    }

    public async Task TickAsync(NetworkSnapshot network, DateTimeOffset now, CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct)) return;
        CancellationTokenSource? operation = null;
        try
        {
            _lastNetwork = network;
            operation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            lock (_cancelLock) _operation = operation;
            var token = operation.Token;
            if (_lastTick != DateTimeOffset.MinValue && now < _lastTick.AddSeconds(-1))
            {
                _nextAttempt = now;
                if (_active is not null) _active.NextHeartbeat = now;
                _log.Write("System clock moved backwards; checking the session now.");
            }
            _lastTick = now;
            var signature = string.Join("|", network.Links.Select(l => l.Signature).Order(StringComparer.Ordinal));
            if (_networkChanged || signature != _networkSignature)
            {
                _networkChanged = false; _networkSignature = signature;
                await DropSessionAsync(false, token);
                _manualGroup = null; _manualSignature = null; PendingCertificate = null;
                _failures = 0; _nextAttempt = now; _retryAccount = null;
                _endpointCursor.Clear(); _interactiveBlocked.Clear();
                _log.Write("Network changed; session will be checked on the current interface.");
            }

            var resetGroups = ResetExpiredQuotas(now);
            if (resetGroups.Count > 0) { _nextAttempt = now; _retryAccount = null; }
            if (_paused || !_config.AutoConnect && _manualGroup is null)
            {
                Status = new(ConnectionState.Paused, _paused ? "Paused. Click Connect to resume." : "Automatic sign-in is disabled."); return;
            }
            if (network.Links.Count == 0)
            {
                await DropSessionAsync(false, token);
                Status = new(ConnectionState.Waiting, network.Problem ?? "No connected Wi-Fi or Ethernet interface."); return;
            }
            if (_manualSignature is not null && !network.Links.Any(l => l.Signature == _manualSignature))
            { _manualGroup = null; _manualSignature = null; }

            if (_active is not null)
            {
                var group = _config.Groups.FirstOrDefault(g => g.Id == _active.GroupId && g.Enabled);
                var account = group?.Accounts.FirstOrDefault(a => a.Id == _active.AccountId && a.Enabled);
                if (group is null || account is null || !network.Links.Any(l => l.Signature == _active.Link.Signature)) await DropSessionAsync(false, token);
                else if (resetGroups.Contains(group.Id) && group.DefaultAccountId is { } defaultId && defaultId != account.Id && group.Accounts.Any(a => a.Id == defaultId && a.Eligible(group.MonthAt(now), now)))
                {
                    _log.Write("New calendar month: returning to the default account.");
                    await DropSessionAsync(true, token); _retryAccount = null; _nextAttempt = now;
                }
                else
                {
                    if (_active.LiveOff || now < _active.NextHeartbeat) { ShowOnline(group, account); return; }
                    var active = _active;
                    try
                    {
                        var reply = await active.Client.HeartbeatAsync(account, group, token);
                        token.ThrowIfCancellationRequested();
                        if (reply.Kind is ReplyKind.Online or ReplyKind.LiveOff)
                        {
                            active.LastHeartbeat = now; active.NextHeartbeat = now.AddSeconds(group.HeartbeatSeconds);
                            active.LiveOff = reply.Kind == ReplyKind.LiveOff; _failures = 0;
                            ShowOnline(group, account); _log.Write("Keepalive accepted."); return;
                        }
                        _retryAccount = account.Id;
                        await DropSessionAsync(false, token);
                        if (reply.Kind == ReplyKind.SignedOut)
                        { _log.Write("Portal ended the session; retrying the same account."); Backoff(now, "Portal requested sign-in again.", false); }
                        else HandleReply(reply, group, account, now);
                        return;
                    }
                    catch (CertificateApprovalException ex) { await DropSessionAsync(false, token); RequireCertificate(ex.Certificate); return; }
                    catch (PortalTransportException ex)
                    {
                        _retryAccount = account.Id; await DropSessionAsync(false, token);
                        AdvanceEndpoint(group); Backoff(now, ex.Message, false); return;
                    }
                }
            }

            var candidate = FindCandidate(network);
            if (candidate is null)
            {
                var needsPin = _config.Groups.Any(g => g.Enabled && g.MatchByPortal && g.Portals.All(p => p.CertificateSha256.Length == 0));
                Status = new(ConnectionState.Waiting, network.LocationDenied
                    ? "Windows denied SSID access. Enable Location access, or select a group and connect manually."
                    : needsPin ? "Portal-only groups need one manual connection to approve their server certificate." : "Connected network is not in an enabled group.", string.Join(", ", network.Links.Select(l => l.Ssid ?? "SSID unavailable")));
                return;
            }
            var (g, link) = candidate.Value;
            if (PendingCertificate is { } pending)
            {
                if (g.Portals.Any(p => p.Id == pending.PortalId)) { RequireCertificate(pending); return; }
                PendingCertificate = null;
            }
            var ordered = g.OrderedAccounts().ToList();
            if (!g.AutoSwitch && ordered.Count > 1)
                ordered = ordered.Where(a => a.Id == _retryAccount).Take(1).DefaultIfEmpty(ordered[0]).ToList();
            var eligible = ordered.Where(a => a.Eligible(g.MonthAt(now), now) && !_interactiveBlocked.Contains(a.Id)).ToList();
            if (eligible.Count == 0)
            {
                var exhausted = ordered.Any(a => a.ExhaustedMonth.Length > 0 && !a.Eligible(g.MonthAt(now), now) && !a.NeedsAttention);
                var nextQuotaCheck = ordered.Where(a => a.Enabled && !a.NeedsAttention && a.QuotaNextProbeAt > now).Select(a => a.QuotaNextProbeAt!.Value).Append(g.NextReset(now)).Min();
                Status = new(ConnectionState.NoAccounts,
                    g.Accounts.Count == 0 ? "Add an account to this group." : "No eligible account. Check disabled/rejected accounts or wait for the monthly reset.",
                    link.Ssid ?? "SSID unavailable", g.Name, NextAction: exhausted ? nextQuotaCheck : null);
                return;
            }
            var selected = eligible.FirstOrDefault(a => a.Id == _retryAccount) ?? eligible[0];
            var endpoint = g.Portals[_endpointCursor.GetValueOrDefault(g.Id) % g.Portals.Count];
            if (now < _nextAttempt)
            {
                Status = new(ConnectionState.Retry, Status.Detail, link.Ssid ?? "SSID unavailable", g.Name, selected.Label, endpoint.Url, _nextAttempt); return;
            }
            // Address-only automatic matching must use a previously approved endpoint.
            if (g.MatchByPortal && _manualGroup != g.Id && endpoint.CertificateSha256.Length == 0)
            { AdvanceEndpoint(g); _nextAttempt = now.AddSeconds(3); return; }
            _log.Write($"Portal transport uses interface #{link.InterfaceIndex}, local IPv4 {link.LocalAddress}; system proxy is disabled.");
            Status = new(ConnectionState.Connecting, "Contacting portal...", link.Ssid ?? "SSID unavailable", g.Name, selected.Label, endpoint.Url);
            IPortalSession? client = _factory.Create(endpoint, link);
            try
            {
                await client.ProbeAsync(token);
                var reply = await client.LoginAsync(selected, g, token);
                token.ThrowIfCancellationRequested();
                if (reply.Kind == ReplyKind.Online)
                {
                    if (selected.ExhaustedMonth.Length > 0 || selected.QuotaNextProbeAt.HasValue)
                    {
                        selected.ExhaustedMonth = ""; selected.QuotaNextProbeAt = null; _store.Save(_config);
                    }
                    _active = new(g.Id, endpoint.Id, selected.Id, link, client) { NextHeartbeat = now.AddSeconds(g.HeartbeatSeconds) };
                    client = null; _retryAccount = selected.Id; _failures = 0;
                    ShowOnline(g, selected); _log.Write("Sign-in accepted; background keepalive started.");
                }
                else HandleReply(reply, g, selected, now);
            }
            catch (CertificateApprovalException ex) { RequireCertificate(ex.Certificate); }
            catch (PortalTransportException ex)
            {
                AdvanceEndpoint(g);
                if (g.MatchByPortal && _manualGroup is null) _portalOnlyCursor++;
                Backoff(now, ex.Message, false);
            }
            finally { client?.Dispose(); }
        }
        catch (OperationCanceledException) { /* Network/suspend/exit cancellation never changes quota state. */ }
        catch (Exception ex)
        {
            _paused = true;
            Status = new(ConnectionState.Attention, "An internal or storage error occurred. Close and reopen UniLink; saved accounts have not been reset.");
            _log.Write("Operation stopped safely (" + ex.GetType().Name + ").");
        }
        finally { lock (_cancelLock) _operation = null; operation?.Dispose(); _gate.Release(); }
    }

    private (NetworkGroup Group, NetworkLink Link)? FindCandidate(NetworkSnapshot snapshot)
    {
        if (_manualGroup is not null && _manualSignature is not null)
        {
            var manual = _config.Groups.FirstOrDefault(g => g.Enabled && g.Id == _manualGroup);
            var link = snapshot.Links.FirstOrDefault(l => l.Signature == _manualSignature);
            if (manual is not null && link is not null) return (manual, link);
        }
        foreach (var group in _config.Groups.Where(g => g.Enabled))
            foreach (var link in snapshot.Links)
                if (link.Ssid is not null && group.Ssids.Contains(link.Ssid, StringComparer.Ordinal)) return (group, link);
        var portalOnly = _config.Groups.Where(g => g.Enabled && g.MatchByPortal && g.Portals.Any(p => p.CertificateSha256.Length > 0)).ToList();
        if (portalOnly.Count == 0) return null;
        return (portalOnly[_portalOnlyCursor % portalOnly.Count], snapshot.Links[0]);
    }

    private HashSet<string> ResetExpiredQuotas(DateTimeOffset now)
    {
        var reset = new HashSet<string>(); var changed = false;
        foreach (var group in _config.Groups)
        {
            var month = group.MonthAt(now);
            if (string.CompareOrdinal(month, group.LastMonth) > 0)
            {
                foreach (var account in group.Accounts.Where(a => a.ExhaustedMonth.Length > 0 && string.CompareOrdinal(a.ExhaustedMonth, month) < 0))
                { account.ExhaustedMonth = ""; account.QuotaNextProbeAt = null; reset.Add(group.Id); }
                group.LastMonth = month; changed = true;
            }
        }
        if (changed) { _store.Save(_config); if (reset.Count > 0) _log.Write("Monthly quota blocks expired; accounts may be tried again. Server remains authoritative."); }
        return reset;
    }

    private void HandleReply(PortalReply reply, NetworkGroup group, Account account, DateTimeOffset now)
    {
        var message = ActivityLog.Sanitize(reply.Message, _config);
        if (message.Length > 0) _log.Write("Portal: " + message);
        switch (reply.Kind)
        {
            case ReplyKind.Quota:
                account.ExhaustedMonth = group.MonthAt(now); account.QuotaNextProbeAt = group.QuotaProbeAfter(now); _store.Save(_config);
                _retryAccount = null; _failures = 0; _nextAttempt = now.AddSeconds(3);
                Status = new(ConnectionState.Retry, "Account quota exhausted. The next eligible account will be tried in order.", Group: group.Name, NextAction: _nextAttempt);
                _log.Write(account.QuotaNextProbeAt.HasValue ? "Quota not reset yet on day 1; this account can be probed again in one hour." : "Account marked exhausted for this calendar month."); break;
            case ReplyKind.Credentials:
                account.NeedsAttention = true; _store.Save(_config);
                _retryAccount = null; _failures = 0; _nextAttempt = now.AddSeconds(5);
                Status = new(ConnectionState.Attention, "Credentials rejected. Edit the account; automatic retries for it are stopped.");
                _log.Write("Rejected credentials require editing or an explicit retry."); break;
            case ReplyKind.Challenge:
                _interactiveBlocked.Add(account.Id); _retryAccount = null;
                Status = new(ConnectionState.Attention, "This account needs interactive authentication. Open the portal manually.");
                _nextAttempt = now.AddMinutes(15); break;
            default:
                _retryAccount = account.Id;
                Backoff(now, "Unclassified portal response. " + message, true); break;
        }
    }

    private void AdvanceEndpoint(NetworkGroup g) => _endpointCursor[g.Id] = (_endpointCursor.GetValueOrDefault(g.Id) + 1) % g.Portals.Count;
    private void Backoff(DateTimeOffset now, string detail, bool ambiguous)
    {
        _failures = Math.Min(_failures + 1, 12);
        var seconds = ambiguous ? Math.Min(900, 300 * _failures) : Math.Min(300, 5 * (1 << Math.Min(_failures - 1, 6)));
        _nextAttempt = now.AddSeconds(seconds);
        Status = new(ConnectionState.Retry, detail, NextAction: _nextAttempt);
        _log.Write(detail + $" Retry in {seconds}s; account order is unchanged.");
    }
    private void RequireCertificate(CertificateInfo certificate)
    {
        PendingCertificate = certificate;
        Status = new(ConnectionState.Certificate, certificate.Changed ? "Server certificate changed. Review it before sending credentials." : "Approve the portal certificate before first sign-in.", Portal: certificate.Url);
    }
    private void ShowOnline(NetworkGroup group, Account account)
    {
        var a = _active!;
        Status = new(ConnectionState.Online, a.LiveOff ? "Signed in. Server disabled keepalive; monitoring network changes." : "Signed in. UniLink is keeping the session alive.",
            a.Link.Ssid ?? "SSID unavailable", group.Name, account.Label,
            group.Portals.First(p => p.Id == a.PortalId).Url, a.LiveOff ? null : a.NextHeartbeat, a.LastHeartbeat);
    }
    private async Task DropSessionAsync(bool logout, CancellationToken ct)
    {
        var active = _active; _active = null;
        if (active is null) return;
        try
        {
            if (logout && !_networkChanged)
            {
                var account = _config.Groups.SelectMany(g => g.Accounts).FirstOrDefault(a => a.Id == active.AccountId);
                if (account is not null)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(3));
                    await active.Client.LogoutAsync(account, timeout.Token);
                    _log.Write("Sign-out requested.");
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or PortalTransportException or CertificateApprovalException) { _log.Write("Sign-out could not be confirmed; server will expire the old session."); }
        finally { active.Client.Dispose(); }
    }

    public async Task ApplyConfigAsync(AppConfig replacement)
    {
        replacement.Validate();
        lock (_cancelLock) _operation?.Cancel();
        await _gate.WaitAsync();
        try
        {
            foreach (var group in replacement.Groups)
            {
                var old = _config.Groups.FirstOrDefault(g => g.Id == group.Id);
                if (old is null) continue;
                group.LastMonth = old.LastMonth;
                foreach (var account in group.Accounts)
                {
                    var previous = old.Accounts.FirstOrDefault(a => a.Id == account.Id);
                    if (previous is not null && account.Username == previous.Username && account.Password == previous.Password)
                    { account.ExhaustedMonth = previous.ExhaustedMonth; account.QuotaNextProbeAt = previous.QuotaNextProbeAt; account.NeedsAttention = previous.NeedsAttention; }
                    else { account.ExhaustedMonth = ""; account.QuotaNextProbeAt = null; account.NeedsAttention = false; }
                }
            }
            _store.Save(replacement);
            if (!CanKeepSession(replacement)) await DropSessionAsync(true, CancellationToken.None);
            else if (_active is { } retained)
            {
                var interval = replacement.Groups.First(g => g.Id == retained.GroupId).HeartbeatSeconds;
                retained.NextHeartbeat = retained.NextHeartbeat < DateTimeOffset.UtcNow.AddSeconds(interval) ? retained.NextHeartbeat : DateTimeOffset.UtcNow.AddSeconds(interval);
            }
            _config = replacement.Clone(); _retryAccount = null; _nextAttempt = DateTimeOffset.MinValue;
            PendingCertificate = null; _interactiveBlocked.Clear(); _failures = 0;
        }
        finally { _gate.Release(); }
    }

    private bool CanKeepSession(AppConfig replacement)
    {
        if (_active is not { } active || _networkChanged) return false;
        var old = _config.Groups.First(g => g.Id == active.GroupId);
        var group = replacement.Groups.FirstOrDefault(g => g.Id == active.GroupId && g.Enabled);
        if (group is null || old.DefaultAccountId != group.DefaultAccountId || !replacement.AutoConnect && _manualGroup != group.Id) return false;
        if (_manualGroup != group.Id && !group.MatchByPortal && !group.Ssids.Contains(active.Link.Ssid ?? "", StringComparer.Ordinal)) return false;
        var oldAccount = old.Accounts.First(a => a.Id == active.AccountId);
        var account = group.Accounts.FirstOrDefault(a => a.Id == active.AccountId && a.Enabled);
        var oldPortal = old.Portals.First(p => p.Id == active.PortalId);
        var portal = group.Portals.FirstOrDefault(p => p.Id == active.PortalId);
        return account is not null && portal is not null && account.Username == oldAccount.Username && account.Password == oldAccount.Password
            && portal.Url == oldPortal.Url && portal.CertificateSha256 == oldPortal.CertificateSha256;
    }

    public async Task ConnectAsync(string? groupId = null, string? accountId = null)
    {
        lock (_cancelLock) _operation?.Cancel();
        await _gate.WaitAsync();
        try
        {
            await DropSessionAsync(true, CancellationToken.None);
            _paused = false; _failures = 0; _nextAttempt = DateTimeOffset.MinValue;
            _interactiveBlocked.Clear(); _retryAccount = accountId;
            if (groupId is not null)
            {
                var group = _config.Groups.FirstOrDefault(g => g.Id == groupId && g.Enabled);
                var link = group is null ? null : _lastNetwork.Links.FirstOrDefault(l => l.Ssid is not null && group.Ssids.Contains(l.Ssid, StringComparer.Ordinal)) ?? _lastNetwork.Links.FirstOrDefault();
                if (link is not null) { _manualGroup = groupId; _manualSignature = link.Signature; }
            }
            Status = new(ConnectionState.Waiting, "Ready to connect.");
        }
        finally { _gate.Release(); }
    }

    public async Task PauseAsync()
    {
        lock (_cancelLock) _operation?.Cancel();
        await _gate.WaitAsync();
        try { _paused = true; await DropSessionAsync(true, CancellationToken.None); Status = new(ConnectionState.Paused, "Paused. Sign-out was requested; automatic sign-in is stopped."); }
        finally { _gate.Release(); }
    }

    public async Task ApproveCertificateAsync(CertificateInfo approved)
    {
        await _gate.WaitAsync();
        try
        {
            if (PendingCertificate != approved) throw new InvalidOperationException("The pending certificate has changed. Review it again.");
            var endpoint = _config.Groups.SelectMany(g => g.Portals).First(p => p.Id == approved.PortalId && p.Url == approved.Url);
            var replacement = _config.Clone();
            replacement.Groups.SelectMany(g => g.Portals).First(p => p.Id == endpoint.Id).CertificateSha256 = approved.Fingerprint;
            _store.Save(replacement); _config = replacement;
            PendingCertificate = null; _nextAttempt = DateTimeOffset.MinValue;
            _log.Write("Portal certificate approved and pinned for this endpoint.");
        }
        finally { _gate.Release(); }
    }

    public async Task ResetAccountAsync(string groupId, string accountId)
    {
        await _gate.WaitAsync();
        try
        {
            var account = _config.Groups.First(g => g.Id == groupId).Accounts.First(a => a.Id == accountId);
            account.NeedsAttention = false; account.ExhaustedMonth = ""; account.QuotaNextProbeAt = null; _interactiveBlocked.Remove(account.Id);
            _store.Save(_config); _failures = 0; _nextAttempt = DateTimeOffset.MinValue;
            _log.Write("Account block cleared by the user.");
        }
        finally { _gate.Release(); }
    }
    public void Dispose() { lock (_cancelLock) _operation?.Cancel(); _active?.Client.Dispose(); _gate.Dispose(); }
}
