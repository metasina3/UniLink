using UniLink.Core;

namespace UniLink.Windows;

internal sealed partial class MainForm
{
    private static readonly TimeSpan UsageRefreshInterval = TimeSpan.FromMinutes(30);
    private readonly UsagePortalClient _usageClient = new(WindowsNetworkMonitor.ValidateBaseLink);
    private readonly LayoutSurface _usageStrip = new() { BackColor = Theme.Surface, Visible = false };
    private readonly Label _usageAmount = Theme.Label("—", 10, true);
    private readonly UsageBar _usageBar = new();
    private readonly Button _usageRefresh = Theme.Button("Refresh data"), _accountUsageButton = Theme.Button("Check data");
    private readonly Dictionary<string, UsageError> _usageErrors = [];
    private readonly Dictionary<string, UsageCertificate> _usageCertificates = [];
    private readonly object _usageLock = new();
    private CancellationTokenSource? _usageOperation;
    private string? _checkingUsageFor;
    private bool _showUsage;

    private sealed record UsageError(string Message, DateTimeOffset RetryAfter);
    private sealed record UsageCertificate(CertificateInfo Certificate, string PreviousFingerprint);

    private void BuildUsageStrip()
    {
        var caption = Theme.Label("Data left", 9);
        caption.ForeColor = Theme.Muted;
        _usageStrip.Controls.AddRange([caption, _usageAmount, _usageBar, _usageRefresh]);
        _usageRefresh.Click += async (_, _) =>
        {
            if (_engine.Status is { State: ConnectionState.Online, GroupId: { } group, AccountId: { } account })
                await ReadUsageAsync(group, account);
        };
        _usageStrip.Arrange = () =>
        {
            int P(int n) => Theme.Px(_usageStrip, n);
            var width = _usageStrip.ClientSize.Width;
            var height = _usageStrip.ClientSize.Height;
            var buttonW = P(120);
            var barW = _usageBar.Share.HasValue ? Math.Min(P(172), width / 4) : 0;
            var barX = width - buttonW - barW - P(12);
            caption.SetBounds(0, 0, P(63), height);
            _usageAmount.SetBounds(P(68), 0, Math.Max(0, barX - P(78)), height);
            _usageBar.SetBounds(barX, Math.Max(0, (height - P(8)) / 2), barW, P(8));
            _usageRefresh.SetBounds(width - buttonW, Math.Max(0, (height - P(25)) / 2), buttonW, P(25));
        };
    }

    private (NetworkGroup? Group, Account? Account) ActiveUsage(EngineStatus status)
    {
        if (status.State != ConnectionState.Online || status.GroupId is null || status.AccountId is null) return default;
        var group = _engine.SnapshotConfig().Groups.FirstOrDefault(g => g.Id == status.GroupId && g.Enabled && g.UsagePortalUrl is { Length: > 0 });
        return (group, group?.Accounts.FirstOrDefault(a => a.Id == status.AccountId && a.Enabled && !a.NeedsAttention));
    }

    private void RenderUsage(EngineStatus status)
    {
        var (group, account) = ActiveUsage(status);
        _usageStrip.Visible = _showUsage = group is not null && account is not null;
        if (group is null || account is null) return;

        _usageAmount.Text = UsageStatus(group, account);
        var usage = account.LastUsage;
        decimal? share = null;
        if (usage?.TotalMb is decimal total && usage.RemainingMb is decimal left)
            share = total > 0 ? Math.Clamp(left, 0, total) / total : 0;
        _usageBar.Share = share;
        _usageBar.Visible = share.HasValue;
        _usageAmount.ForeColor = share is <= .2m ? Theme.Red : Theme.Text;
        var description = UsageDescription(group, account);
        _tips.SetToolTip(_usageAmount, description);
        _tips.SetToolTip(_usageBar, description);
        _usageRefresh.Text = _checkingUsageFor == account.Id ? "Checking…" : CertificateFor(group) is null ? "Refresh data" : "Verify server";
        _usageRefresh.Enabled = !_uiCheck && _usageOperation is null;
        _usageStrip.PerformLayout();
    }

    private void UpdateAccountUsageAction()
    {
        var id = (_accountGroup.SelectedItem as Choice)?.Id;
        var group = id is null ? null : _engine.SnapshotConfig().Groups.FirstOrDefault(g => g.Id == id);
        var selected = SelectedId(_accounts);
        _accountUsageButton.Enabled = !_uiCheck && _usageOperation is null && group is { Enabled: true, UsagePortalUrl.Length: > 0 } &&
            group.Accounts.Any(a => a.Id == selected && a.Enabled && !a.NeedsAttention);
        _accountUsageButton.Text = selected is not null && _checkingUsageFor == selected ? "Checking…" :
            group is not null && CertificateFor(group) is not null ? "Verify server" : "Check data";
    }

    private void RefreshActiveUsageIfDue()
    {
        if (_usageOperation is not null || _command || !Visible || !_pages["Connection"].Visible) return;
        var (group, account) = ActiveUsage(_engine.Status);
        if (group is null || account is null || CertificateFor(group) is not null) return;
        var now = DateTimeOffset.UtcNow;
        if (_usageErrors.TryGetValue(account.Id, out var error) && error.RetryAfter > now) return;
        if (account.LastUsage is { } usage && now >= usage.CheckedAt && now - usage.CheckedAt < UsageRefreshInterval) return;
        _ = ReadUsageAsync(group.Id, account.Id, activeOnly: true);
    }

    private async Task ReadUsageAsync(string groupId, string? accountId, bool activeOnly = false)
    {
        if (_uiCheck || _exiting || _command || _usageOperation is not null) return;
        var group = _engine.SnapshotConfig().Groups.FirstOrDefault(g => g.Id == groupId);
        if (group is not { Enabled: true, UsagePortalUrl.Length: > 0 })
        {
            if (!activeOnly) MessageBox.Show(this, "Enable this network group and set its data portal to check usage.", "Check data");
            return;
        }
        var targets = group.Accounts.Where(a => a.Enabled && !a.NeedsAttention && (accountId is null || a.Id == accountId)).ToList();
        if (targets.Count == 0) return;

        using var operation = new CancellationTokenSource();
        lock (_usageLock) _usageOperation = operation;
        var ct = operation.Token;
        try
        {
            if (CertificateFor(group) is { } pending)
            {
                if (activeOnly) return;
                using var dialog = new CertificateDialog(pending.Certificate, usage: true);
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var approved = await _engine.ApproveUsageCertificateAsync(pending.Certificate, pending.PreviousFingerprint, ct);
                _usageCertificates.Remove(group.Id);
                if (!approved) return;
            }

            foreach (var account in targets)
            {
                ct.ThrowIfCancellationRequested();
                _checkingUsageFor = account.Id;
                RenderStatus();
                var target = await _engine.GetUsageTargetAsync(group.Id, account.Id, activeOnly, ct);
                if (target is null)
                {
                    if (!activeOnly) RememberUsageError(account.Id, "Connect to this campus network.", TimeSpan.FromMinutes(1));
                    continue;
                }
                try
                {
                    var usage = await _usageClient.ReadAsync(target, ct);
                    ct.ThrowIfCancellationRequested();
                    await _engine.SaveUsageAsync(target, usage, ct);
                    _usageErrors.Remove(account.Id);
                }
                catch (CertificateApprovalException ex)
                {
                    _usageCertificates[group.Id] = new(ex.Certificate, target.Portal.CertificateSha256);
                    break;
                }
                catch (UsageSignInException ex)
                {
                    _usageErrors[account.Id] = new(ex.Message, DateTimeOffset.MaxValue);
                }
                catch (PortalTransportException ex)
                {
                    RememberUsageError(account.Id, ex.Message, TimeSpan.FromMinutes(5));
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            RememberUsageError(_checkingUsageFor ?? targets[0].Id, "Data usage could not be saved or checked.", TimeSpan.FromMinutes(5));
            _log.Write("Data usage check could not complete (" + ex.GetType().Name + ").");
        }
        finally
        {
            lock (_usageLock) _usageOperation = null;
            _checkingUsageFor = null;
            if (!_exiting && !IsDisposed) RenderStatus();
        }
    }

    private void CancelUsage()
    {
        lock (_usageLock) _usageOperation?.Cancel();
    }

    private UsageCertificate? CertificateFor(NetworkGroup group) =>
        _usageCertificates.TryGetValue(group.Id, out var pending) && pending.Certificate.Url == group.UsagePortalUrl &&
        pending.PreviousFingerprint == group.UsageCertificateSha256 ? pending : null;

    private void RememberUsageError(string accountId, string message, TimeSpan retry)
    {
        _usageErrors[accountId] = new(message, DateTimeOffset.UtcNow + retry);
    }

    private void ForgetUsageFeedback(NetworkGroup group)
    {
        _usageCertificates.Remove(group.Id);
        foreach (var account in group.Accounts) _usageErrors.Remove(account.Id);
    }

    private string UsageStatus(NetworkGroup group, Account account)
    {
        if (group.UsagePortalUrl is not { Length: > 0 }) return "—";
        if (account.LastUsage is { } usage)
        {
            var value = usage.TotalMb is { } total && usage.RemainingMb is { } left
                ? $"{FormatData(left)} of {FormatData(total)}"
                : $"Unlimited · {FormatData(usage.UsedMb)} used";
            return _usageErrors.ContainsKey(account.Id) || CertificateFor(group) is not null ? value + " (last read)" : value;
        }
        if (_checkingUsageFor == account.Id) return "Checking…";
        if (CertificateFor(group) is not null) return "Verify server certificate";
        return _usageErrors.ContainsKey(account.Id) ? "Could not check" : "—";
    }

    private string UsageDescription(NetworkGroup group, Account account)
    {
        var detail = UsageStatus(group, account);
        if (account.LastUsage is { } usage) detail += $"\nLast checked {usage.CheckedAt.ToLocalTime():g}";
        if (CertificateFor(group) is not null) detail += "\nReview the usage portal certificate before checking data.";
        if (_usageErrors.TryGetValue(account.Id, out var error)) detail += "\n" + error.Message;
        return detail;
    }

    private static string FormatData(decimal mb) => mb switch
    {
        >= 1_048_576 => $"{mb / 1_048_576:0.##} TB",
        >= 1024 => $"{mb / 1024:0.##} GB",
        _ => $"{mb:0.##} MB"
    };

    private sealed class UsageBar : Control
    {
        private decimal? _share;
        public decimal? Share
        {
            get => _share;
            set
            {
                if (_share == value) return;
                _share = value;
                AccessibleName = value is { } percent ? $"Data left {percent:P0}" : "Data left";
                Invalidate();
            }
        }

        public UsageBar()
        {
            BackColor = Theme.Input;
            TabStop = false;
            AccessibleRole = AccessibleRole.ProgressBar;
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_share is not { } share) return;
            var color = share <= .2m ? Theme.Red : Theme.AccentHover;
            using var brush = new SolidBrush(color);
            e.Graphics.FillRectangle(brush, 0, 0, (int)Math.Round(ClientSize.Width * share), ClientSize.Height);
        }
    }
}
