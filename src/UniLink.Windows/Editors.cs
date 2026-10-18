using System.Text.Json;
using UniLink.Core;

namespace UniLink.Windows;

internal class EditorWindow : AdaptiveForm
{
    private readonly Label _heading, _note;
    private DialogFooter? _footer;
    protected Panel Body { get; } = new() { BackColor = Theme.Background };
    protected EditorWindow(string title, string note, Size size, bool resizable = true)
    {
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 9.5f);
        Text = title; ClientSize = size; MinimumSize = new Size(440, 300); StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false; MaximizeBox = resizable; ShowInTaskbar = false;
        if (!resizable) FormBorderStyle = FormBorderStyle.FixedDialog;
        _heading = Theme.Label(title, 16, true); _note = Theme.Label(note, 9); _note.ForeColor = Theme.Muted;
        Controls.AddRange([_heading, _note, Body]); Shown += (_, _) => Theme.FitToScreen(this);
    }
    protected void SetActions(Button save, Button cancel)
    {
        cancel.DialogResult = DialogResult.Cancel; _footer = new DialogFooter(save, cancel); Controls.Add(_footer);
        AcceptButton = save; CancelButton = cancel; Theme.Apply(this); PerformLayout();
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e); if (_heading is null) return;
        int P(int n) => Theme.Px(this, n); var p = P(16); var compact = ClientSize.Height < P(360); var tiny = ClientSize.Height < P(330); var top = P(tiny ? 36 : compact ? 46 : 66); var footerH = P(tiny ? 48 : 52);
        var titleHeight = Theme.LineHeight(_heading, 28); var extra = titleHeight - P(28); top += extra;
        _heading.SetBounds(p, P(tiny ? 3 : 8), Math.Max(0, ClientSize.Width - p * 2), titleHeight);
        _note.Visible = !compact; _note.SetBounds(p, P(38) + extra, Math.Max(0, ClientSize.Width - p * 2), P(22));
        Body.SetBounds(0, top, ClientSize.Width, Math.Max(0, ClientSize.Height - top - footerH));
        _footer?.SetBounds(0, ClientSize.Height - footerH, ClientSize.Width, footerH);
    }
}

internal sealed class AccountEditor : EditorWindow
{
    private readonly TextBox _label = new(), _username = new(), _password = new() { UseSystemPasswordChar = true };
    private readonly CheckBox _enabled = new() { Text = "Account enabled", Checked = true, AutoSize = false };
    public Account Result { get; private set; }
    public AccountEditor(Account? account) : base(account is null ? "Add account" : "Edit account", "Save your campus sign-in details on this PC.", new Size(460, 300), false)
    {
        Result = account is null ? new Account() : JsonSerializer.Deserialize<Account>(JsonSerializer.Serialize(account))!;
        _label.Text = Result.Label; _username.Text = Result.Username; _password.Text = Result.Password; _enabled.Checked = Result.Enabled;
        _label.PlaceholderText = "My student account"; _username.PlaceholderText = "Student number";
        var layout = new LayoutSurface { Dock = DockStyle.Fill }; Body.Controls.Add(layout);
        var labels = new[] { Theme.Label("Account name"), Theme.Label("Username"), Theme.Label("Password") }; var inputs = new[] { _label, _username, _password };
        foreach (var label in labels) { label.ForeColor = Theme.Muted; layout.Controls.Add(label); }
        layout.Controls.AddRange(inputs);
        var show = new CheckBox { Text = "Show password", AutoSize = false }; show.CheckedChanged += (_, _) => _password.UseSystemPasswordChar = !show.Checked;
        layout.Controls.AddRange([show, _enabled]);
        layout.Arrange = () =>
        {
            int P(int n) => Theme.Px(layout, n); var p = P(16); var labelW = P(108); var rowH = P(38); var y = P(8);
            for (var i = 0; i < labels.Length; i++)
            {
                labels[i].SetBounds(p, y, labelW, P(28)); inputs[i].SetBounds(p + labelW, y, Math.Max(0, layout.Width - p * 2 - labelW), inputs[i].PreferredHeight); y += rowH;
            }
            var half = (layout.Width - p * 2) / 2; show.SetBounds(p, y, half, P(28)); _enabled.SetBounds(p + half, y, half, P(28));
        };
        var save = Theme.Button("Save account", true); var cancel = Theme.Button("Cancel");
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_label.Text) || string.IsNullOrWhiteSpace(_username.Text) || string.IsNullOrEmpty(_password.Text))
            { MessageBox.Show(this, "Enter an account name, username and password.", "UniLink"); return; }
            Result.Label = _label.Text.Trim(); Result.Username = _username.Text.Trim(); Result.Password = _password.Text; Result.Enabled = _enabled.Checked;
            DialogResult = DialogResult.OK; Close();
        };
        SetActions(save, cancel);
    }
}

internal sealed class GroupEditor : EditorWindow
{
    private readonly TextBox _name = new();
    private readonly RichTextBox _ssids = LinesBox();
    private readonly RichTextBox _urls = LinesBox();
    private readonly CheckBox _enabled = new() { Text = "Group enabled", AutoSize = false };
    private readonly CheckBox _portalOnly = new() { Text = "Detect other networks by an approved portal", AutoSize = false };
    private readonly CheckBox _switch = new() { Text = "Use backup accounts", AutoSize = false };
    private readonly NumericUpDown _heartbeat = new() { Minimum = 30, Maximum = 180, AutoSize = false };
    private readonly TextBox _timezone = new();
    private readonly RichTextBox _quota = LinesBox();
    private readonly RichTextBox _credentials = LinesBox();
    private readonly ListBox _pins = new();
    private readonly TextBox _pinDetail = new() { Multiline = true, ReadOnly = true, Font = new Font("Consolas", 9) };
    private readonly LayoutSurface[] _tabs = [new(), new(), new()];
    private readonly Button[] _tabButtons = [Theme.Button("Network"), Theme.Button("Rules"), Theme.Button("Certificates")];
    public NetworkGroup Result { get; private set; }
    public GroupEditor(NetworkGroup? group) : base(group is null ? "Add network group" : "Edit network group", "Wi-Fi names, login addresses and sign-in rules.", new Size(640, 450))
    {
        MinimumSize = new Size(616, 370);
        Result = group is null ? new NetworkGroup() : JsonSerializer.Deserialize<NetworkGroup>(JsonSerializer.Serialize(group))!;
        _name.Text = Result.Name; _ssids.Text = string.Join(Environment.NewLine, Result.Ssids); _urls.Text = string.Join(Environment.NewLine, Result.Portals.Select(p => p.Url));
        _enabled.Checked = Result.Enabled; _portalOnly.Checked = Result.MatchByPortal; _switch.Checked = Result.AutoSwitch;
        _heartbeat.Value = Result.HeartbeatSeconds; _timezone.Text = Result.TimeZoneId;
        _quota.Text = string.Join(Environment.NewLine, Result.QuotaPhrases); _credentials.Text = string.Join(Environment.NewLine, Result.CredentialPhrases);
        var layout = new LayoutSurface { Dock = DockStyle.Fill }; Body.Controls.Add(layout);
        layout.Controls.AddRange(_tabs); layout.Controls.AddRange(_tabButtons);
        for (var i = 0; i < _tabButtons.Length; i++) { var tab = i; _tabButtons[i].Click += (_, _) => InspectTab(tab); }
        layout.Arrange = () =>
        {
            int P(int n) => Theme.Px(layout, n); var x = P(16);
            foreach (var button in _tabButtons) { button.SetBounds(x, 0, P(112), P(30)); x += P(118); }
            foreach (var tab in _tabs) tab.SetBounds(P(16), P(40), Math.Max(0, layout.Width - P(32)), Math.Max(0, layout.Height - P(48)));
        };
        BuildNetwork(); BuildRules(); BuildCertificates(); RefreshPins();
        var save = Theme.Button("Save group", true); var cancel = Theme.Button("Cancel");
        save.Click += (_, _) =>
        {
            try
            {
                Result.Name = _name.Text.Trim(); Result.Ssids = Lines(_ssids, false);
                Result.Portals = Lines(_urls).Select(PortalEndpoint.NormalizeUrl).Distinct(StringComparer.OrdinalIgnoreCase).Select(url =>
                    Result.Portals.FirstOrDefault(p => p.Url.Equals(url, StringComparison.OrdinalIgnoreCase)) ?? new PortalEndpoint { Url = url }).ToList();
                Result.Enabled = _enabled.Checked; Result.MatchByPortal = _portalOnly.Checked; Result.AutoSwitch = _switch.Checked;
                Result.HeartbeatSeconds = (int)_heartbeat.Value; Result.TimeZoneId = _timezone.Text.Trim();
                Result.QuotaPhrases = Lines(_quota); Result.CredentialPhrases = Lines(_credentials);
                new AppConfig { Groups = [Result] }.Validate(); DialogResult = DialogResult.OK; Close();
            }
            catch (Exception ex) when (ex is InvalidDataException or TimeZoneNotFoundException or InvalidTimeZoneException)
            { MessageBox.Show(this, ex.Message, "Check group settings", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        };
        InspectTab(0); SetActions(save, cancel);
    }
    private void BuildNetwork()
    {
        var panel = _tabs[0]; var name = Theme.Label("Group name"); var ssids = Theme.Label("Wi-Fi SSIDs — one per line"); var urls = Theme.Label("HTTPS portals — one per line");
        foreach (var label in new[] { name, ssids, urls }) label.ForeColor = Theme.Muted;
        panel.Controls.AddRange([name, _name, ssids, _ssids, urls, _urls, _enabled, _switch, _portalOnly]);
        panel.Arrange = () =>
        {
            int P(int n) => Theme.Px(panel, n); var half = (panel.Width - P(12)) / 2; var nameW = P(100);
            name.SetBounds(0, 0, nameW, P(28)); _name.SetBounds(nameW, 0, Math.Max(0, panel.Width - nameW), _name.PreferredHeight);
            ssids.SetBounds(0, P(36), half, P(20)); urls.SetBounds(half + P(12), P(36), half, P(20));
            var listH = Math.Min(P(160), Math.Max(P(48), panel.Height - P(120)));
            _ssids.SetBounds(0, P(58), half, listH); _urls.SetBounds(half + P(12), P(58), half, listH);
            var y = _ssids.Bottom + P(6); _enabled.SetBounds(0, y, half, P(26)); _switch.SetBounds(half + P(12), y, half, P(26));
            _portalOnly.SetBounds(0, y + P(28), panel.Width, P(26));
        };
    }
    private void BuildRules()
    {
        var panel = _tabs[1]; var heartbeat = Theme.Label("Keepalive (seconds)"); var timezone = Theme.Label("Month reset time zone");
        var quota = Theme.Label("Quota error phrases"); var credentials = Theme.Label("Password error phrases");
        foreach (var label in new[] { heartbeat, timezone, quota, credentials }) label.ForeColor = Theme.Muted;
        panel.Controls.AddRange([heartbeat, _heartbeat, timezone, _timezone, quota, _quota, credentials, _credentials]);
        panel.Arrange = () =>
        {
            int P(int n) => Theme.Px(panel, n); var half = (panel.Width - P(12)) / 2; var right = half + P(12);
            heartbeat.SetBounds(0, 0, half, P(20)); timezone.SetBounds(right, 0, half, P(20));
            _heartbeat.SetBounds(0, P(24), Math.Min(half, P(128)), Math.Max(P(26), _heartbeat.PreferredSize.Height)); _timezone.SetBounds(right, P(24), half, _timezone.PreferredHeight);
            quota.SetBounds(0, P(60), half, P(20)); credentials.SetBounds(right, P(60), half, P(20));
            _quota.SetBounds(0, P(84), half, Math.Max(0, panel.Height - P(84))); _credentials.SetBounds(right, P(84), half, Math.Max(0, panel.Height - P(84)));
        };
    }
    private void BuildCertificates()
    {
        var panel = _tabs[2]; var hint = Theme.Label("Approved login servers. Select one to view its SHA-256 fingerprint.", 9); hint.ForeColor = Theme.Muted;
        var forget = Theme.Button("Forget certificate"); forget.Enabled = false;
        _pins.SelectedIndexChanged += (_, _) =>
        {
            forget.Enabled = _pins.SelectedIndex >= 0;
            _pinDetail.Text = _pins.SelectedItem is Choice c ? Result.Portals.First(p => p.Id == c.Id).CertificateSha256 : "";
        };
        forget.Click += (_, _) => { if (_pins.SelectedItem is Choice c) { Result.Portals.First(p => p.Id == c.Id).CertificateSha256 = ""; RefreshPins(); } };
        panel.Controls.AddRange([hint, _pins, _pinDetail, forget]);
        panel.Arrange = () =>
        {
            int P(int n) => Theme.Px(panel, n); hint.SetBounds(0, 0, panel.Width, P(24));
            var listH = Math.Max(P(48), panel.Height - P(120)); _pins.SetBounds(0, P(30), panel.Width, listH);
            _pinDetail.SetBounds(0, _pins.Bottom + P(6), panel.Width, P(44)); forget.SetBounds(0, panel.Height - P(32), P(148), P(32));
        };
    }
    internal void InspectTab(int selected)
    {
        for (var i = 0; i < _tabs.Length; i++)
        {
            _tabs[i].Visible = i == selected; _tabButtons[i].BackColor = i == selected ? Theme.Selection : Theme.Input;
            _tabButtons[i].ForeColor = i == selected ? Theme.AccentHover : Theme.Muted;
        }
        _tabs[selected].BringToFront(); _tabs[selected].PerformLayout();
    }
    private void RefreshPins()
    {
        _pins.Items.Clear(); foreach (var p in Result.Portals) _pins.Items.Add(new Choice(p.Id, p.Url + (p.CertificateSha256.Length > 0 ? "  — Approved" : "  — Not approved")));
        _pinDetail.Text = ""; if (_pins.Items.Count > 0) _pins.SelectedIndex = 0;
    }
    private static RichTextBox LinesBox() => new() { DetectUrls = false, ScrollBars = RichTextBoxScrollBars.Vertical, AcceptsTab = false };
    private static List<string> Lines(TextBoxBase box, bool trim = true) => box.Lines.Select(s => trim ? s.Trim() : s).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToList();
}

internal sealed class CertificateDialog : EditorWindow
{
    public CertificateDialog(CertificateInfo cert) : base("Review portal certificate", cert.Changed ? "The saved certificate has changed. Review before approving." : "Approve the expected university server before signing in.", new Size(620, 400))
    {
        MinimumSize = new Size(536, 370);
        var layout = new LayoutSurface { Dock = DockStyle.Fill }; Body.Controls.Add(layout);
        var names = new[] { "Portal", "Subject", "Issuer", "Valid until", "SHA-256" };
        var values = new[] { cert.Url, cert.Subject, cert.Issuer, cert.Expires.ToString("yyyy-MM-dd"), cert.Fingerprint };
        var labels = names.Select(n => Theme.Label(n, 9)).ToArray();
        var inputs = values.Select((value, index) => new TextBox { Text = value, ReadOnly = true, Multiline = index == 4, WordWrap = true }).ToArray();
        inputs[4].Font = new Font("Consolas", 9);
        foreach (var label in labels) label.ForeColor = Theme.Muted;
        layout.Controls.AddRange(labels); layout.Controls.AddRange(inputs);
        var hint = Theme.Label("Compare this SHA-256 fingerprint with the university login page or university IT before approving.", 9); hint.ForeColor = Theme.Muted; layout.Controls.Add(hint);
        layout.Arrange = () =>
        {
            int P(int n) => Theme.Px(layout, n); var p = P(16); var labelW = P(80); var y = P(4); var row = Math.Max(P(26), Math.Min(P(32), (layout.Height - P(96)) / 4));
            for (var i = 0; i < labels.Length; i++)
            {
                var h = i == 4 ? P(44) : inputs[i].PreferredHeight; labels[i].SetBounds(p, y, labelW, P(26));
                inputs[i].SetBounds(p + labelW, y, Math.Max(0, layout.Width - p * 2 - labelW), h); y += i == 4 ? P(50) : row;
            }
            hint.SetBounds(p, y + P(4), Math.Max(0, layout.Width - p * 2), Math.Max(P(30), layout.Height - y - P(8)));
        };
        var trust = Theme.Button("Trust certificate", true); var cancel = Theme.Button("Cancel"); trust.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        SetActions(trust, cancel); Shown += (_, _) => { foreach (var text in inputs) { text.SelectionStart = 0; text.SelectionLength = 0; } ActiveControl = trust; };
    }
}
