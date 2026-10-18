using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;
using UniLink.Core;

namespace UniLink.Windows;

internal sealed class MainForm : AdaptiveForm
{
    private readonly ConnectionEngine _engine;
    private readonly WindowsNetworkMonitor? _network;
    private readonly ActivityLog _log;
    private readonly bool _background, _uiCheck;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 3000 };
    private readonly NotifyIcon? _tray;
    private readonly Panel _content = new() { BackColor = Theme.Background };
    private readonly LayoutSurface _header = new() { BackColor = Theme.Surface };
    private readonly Dictionary<string, PageSurface> _pages = [];
    private readonly Dictionary<string, Button> _navigation = [];
    private readonly List<ContextMenuStrip> _menus = [];
    private readonly ToolTip _tips = new() { AutoPopDelay = 12000, InitialDelay = 500 };
    private readonly Label _state = Theme.Label("Waiting", 16, true);
    private readonly Label _detail = Theme.Label("", 9);
    private readonly Dictionary<string, Label> _facts = [];
    private readonly ComboBox _connectGroup = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _accountGroup = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DataGridView _groups = Theme.Grid(), _accounts = Theme.Grid();
    private readonly RichTextBox _activity = new() { DetectUrls = false, ReadOnly = true, ScrollBars = RichTextBoxScrollBars.Vertical, WordWrap = true, Font = new Font("Consolas", 9) };
    private readonly CheckBox _auto = new() { Text = "Enabled", AutoSize = false };
    private readonly CheckBox _closeToTray = new() { Text = "Enabled", AutoSize = false };
    private readonly CheckBox _startup = new() { Text = "Enabled", AutoSize = false };
    private readonly List<Button> _accountActions = [], _groupActions = [];
    private Label _emptyAccounts = null!, _emptyGroups = null!;
    private Button _trust = null!;
    private LayoutSurface _dashboard = null!;
    private bool _exiting, _ticking, _command;
    private string _groupListKey = "", _accountListKey = "";
    private ConnectionState? _lastTrayState;
    internal EngineStatus? PreviewStatus { get; set; }

    public MainForm(ConnectionEngine engine, WindowsNetworkMonitor? network, ActivityLog log, bool background, bool uiCheck = false)
    {
        _engine = engine; _network = network; _log = log; _background = background; _uiCheck = uiCheck;
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f); Text = "UniLink"; ClientSize = new Size(900, 550); MinimumSize = new Size(656, 378);
        StartPosition = FormStartPosition.CenterScreen; KeyPreview = true;
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Information;
        SuspendLayout(); Controls.AddRange([_header, _content]); BuildHeader();
        BuildDashboard(); BuildNetworks(); BuildAccounts(); BuildActivity(); BuildSettings(); Theme.Apply(this); ResumeLayout(true);
        if (!uiCheck)
        {
            _tray = new NotifyIcon { Icon = Icon, Text = "UniLink", Visible = true };
            var menu = Theme.Menu(); _menus.Add(menu);
            menu.Items.Add("Open UniLink", null, (_, _) => ShowWindow());
            menu.Items.Add("Connect / resume", null, async (_, _) => await Command(() => _engine.ConnectAsync()));
            menu.Items.Add("Pause and sign out", null, async (_, _) => await Command(_engine.PauseAsync));
            menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Exit", null, async (_, _) => await ExitAsync());
            _tray.ContextMenuStrip = menu; _tray.DoubleClick += (_, _) => ShowWindow(); _tray.BalloonTipClicked += (_, _) => ShowWindow();
            if (_network is not null) _network.Changed += OnNetworkChanged;
            SystemEvents.PowerModeChanged += OnPowerChanged; SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        }
        _log.Added += OnLog; _activity.Text = _log.Export();
        _timer.Tick += async (_, _) => await TickAsync(); FormClosing += OnClosing;
        Shown += async (_, _) =>
        {
            Theme.FitToScreen(this); if (_background) Hide(); RefreshLists();
            Navigate(_engine.SnapshotConfig().Groups.All(g => g.Accounts.Count == 0) ? "Accounts" : "Connection"); RenderStatus();
            if (!_uiCheck) { await TickAsync(); _timer.Start(); }
        };
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D5)
            { Navigate(_pages.Keys.ElementAt(e.KeyCode - Keys.D1)); e.Handled = true; e.SuppressKeyPress = true; }
        };
        Disposed += (_, _) =>
        {
            _timer.Stop(); _timer.Dispose(); _tray?.Dispose(); _tips.Dispose();
            if (_network is not null) _network.Changed -= OnNetworkChanged;
            if (!_uiCheck) { SystemEvents.PowerModeChanged -= OnPowerChanged; SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged; }
            _log.Added -= OnLog; foreach (var menu in _menus) menu.Dispose();
        };
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e); if (_header is null || _content is null) return;
        var top = Theme.Px(this, ClientSize.Height < Theme.Px(this, 420) ? 42 : 82); _header.SetBounds(0, 0, ClientSize.Width, top);
        _content.SetBounds(0, top, ClientSize.Width, Math.Max(0, ClientSize.Height - top));
        foreach (var page in _pages.Values) page.Bounds = _content.ClientRectangle;
    }
    private void BuildHeader()
    {
        var brand = Theme.Label("UniLink", 14, true); brand.ForeColor = Theme.AccentHover;
        var caption = Theme.Label("University Wi-Fi", 9); caption.ForeColor = Theme.Muted;
        var version = Theme.Label("v1.2.1", 8.5f); version.ForeColor = Theme.Muted;
        var exit = Theme.Button("Exit"); exit.BackColor = Theme.Surface; exit.Click += async (_, _) => await ExitAsync();
        _header.Controls.AddRange([brand, caption, version, exit]);
        foreach (var name in new[] { "Connection", "Networks", "Accounts", "Activity", "Settings" })
        {
            var button = Theme.Button(name); button.BackColor = Theme.Surface;
            button.Click += (_, _) => Navigate(name); _navigation[name] = button; _header.Controls.Add(button);
        }
        _header.Arrange = () =>
        {
            int P(int n) => Theme.Px(_header, n);
            var compact = _header.Height < P(60); brand.Visible = caption.Visible = version.Visible = !compact;
            brand.SetBounds(P(16), P(4), P(94), P(32));
            caption.SetBounds(P(118), P(8), Math.Max(0, _header.Width - P(310)), P(24));
            version.SetBounds(_header.Width - P(146), P(8), P(60), P(24));
            exit.SetBounds(_header.Width - P(78), P(4), P(62), P(30));
            var x = P(16); var width = Math.Min(P(112), Math.Max(0, (_header.Width - P(compact ? 110 : 32)) / 5));
            foreach (var button in _navigation.Values) { button.SetBounds(x, P(compact ? 5 : 44), width, P(32)); x += width; }
        };
    }
    public void ShowWindow() { if (IsDisposed) return; Show(); WindowState = FormWindowState.Normal; Activate(); }
    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    { if (IsHandleCreated && !IsDisposed) BeginInvoke(() => { if (!IsDisposed) { Theme.FitToScreen(this, false); PerformLayout(); } }); }
    private void OnNetworkChanged() => _engine.NetworkChanged();
    private void OnPowerChanged(object sender, PowerModeChangedEventArgs e)
    { if (e.Mode is PowerModes.Resume or PowerModes.Suspend) _engine.NetworkChanged(); }
    private void OnLog(string line)
    {
        if (!IsHandleCreated || IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => OnLog(line)); return; }
        _activity.AppendText(line + Environment.NewLine); if (_activity.TextLength > 160000) _activity.Text = _log.Export();
    }
    private async Task TickAsync()
    {
        if (_ticking || _exiting || _network is null) return; _ticking = true;
        try { await _engine.TickAsync(_network.Read(), DateTimeOffset.UtcNow); if (!_exiting && !IsDisposed) RenderStatus(); }
        catch (Exception ex) { _log.Write("Network check could not complete (" + ex.GetType().Name + ")."); }
        finally { _ticking = false; }
    }
    private async Task Command(Func<Task> action)
    {
        if (_command || _exiting) return; _command = true;
        try { await action(); RefreshLists(); RenderStatus(); }
        catch (Exception ex)
        {
            var message = ex is InvalidDataException or TimeZoneNotFoundException or InvalidTimeZoneException or InvalidOperationException ? ex.Message : "The change could not be saved (" + ex.GetType().Name + ").";
            MessageBox.Show(this, message, "UniLink", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally { _command = false; }
    }
    private void Navigate(string name)
    {
        foreach (var (key, page) in _pages) page.Visible = key == name;
        foreach (var (key, button) in _navigation)
        { button.BackColor = key == name ? Theme.Selection : Theme.Surface; button.ForeColor = key == name ? Theme.AccentHover : Theme.Muted; }
        if (name == "Settings")
        { var config = _engine.SnapshotConfig(); _auto.Checked = config.AutoConnect; _closeToTray.Checked = config.CloseToTray; _startup.Checked = !_uiCheck && StartupRegistration.Enabled; }
        RefreshLists(); _pages[name].BringToFront(); _pages[name].PerformLayout();
    }
    internal void InspectPage(string name) { Navigate(name); RenderStatus(); PerformLayout(); }
    private PageSurface NewPage(string name, string subtitle)
    {
        var page = new PageSurface(name, subtitle) { Visible = false, Bounds = _content.ClientRectangle };
        _pages[name] = page; _content.Controls.Add(page); return page;
    }
    private void BuildDashboard()
    {
        var page = NewPage("Connection", "Your campus session and background keepalive.");
        _dashboard = FillLayout(page.Body); var canvas = _dashboard; var status = new CardPanel(); var details = new CardPanel();
        _detail.ForeColor = Theme.Muted; _detail.TextAlign = ContentAlignment.MiddleLeft; status.Controls.AddRange([_state, _detail]);
        _trust = Theme.Button("Review certificate", true); _trust.Visible = false; _trust.Click += async (_, _) => await ReviewCertificateAsync(); status.Controls.Add(_trust);
        var captions = new List<Label>();
        foreach (var key in new[] { "Network", "Group", "Account", "Portal", "Last keepalive", "Next action" })
        {
            var label = Theme.Label(key, 9); label.ForeColor = Theme.Muted; captions.Add(label);
            var value = Theme.Label("—", 9); _facts[key] = value; details.Controls.AddRange([label, value]);
        }
        var groupCaption = Theme.Label("Network group", 9); groupCaption.ForeColor = Theme.Muted;
        var connect = Theme.Button("Connect", true); connect.Click += async (_, _) => await Command(() => _engine.ConnectAsync((_connectGroup.SelectedItem as Choice)?.Id));
        var pause = Theme.Button("Pause & sign out"); pause.Click += async (_, _) => await Command(_engine.PauseAsync);
        var portal = Theme.Link("Open login page"); portal.LinkClicked += (_, _) =>
        { var g = _engine.SnapshotConfig().Groups.FirstOrDefault(g => g.Id == (_connectGroup.SelectedItem as Choice)?.Id); if (g?.Portals.FirstOrDefault() is { } p) Open(p.Url); };
        var privacy = Theme.Link("Wi-Fi privacy settings"); privacy.LinkClicked += (_, _) => Open("ms-settings:privacy-location");
        var note = Theme.Label("Runs in the tray when closed.", 8.5f); note.ForeColor = Theme.Muted;
        canvas.Controls.AddRange([status, details, groupCaption, _connectGroup, connect, pause, portal, privacy, note]);
        canvas.Arrange = () =>
        {
            int P(int n) => Theme.Px(canvas, n); var w = canvas.ClientSize.Width; var h = canvas.ClientSize.Height; var compact = h < P(320);
            var gap = P(compact ? 6 : 12); var statusH = P(compact ? 54 : 78);
            var detailsH = P(compact ? 76 : 116); var inset = P(compact ? 10 : 14); var reserve = _trust.Visible ? P(162) : 0;
            status.SetBounds(0, 0, w, statusH);
            _state.SetBounds(inset, P(compact ? 1 : 6), Math.Max(0, w - inset * 2 - reserve), P(compact ? 30 : 32));
            _detail.SetBounds(inset, P(compact ? 31 : 40), Math.Max(0, w - inset * 2 - reserve), P(compact ? 22 : 30));
            _trust.SetBounds(w - P(156), (statusH - P(32)) / 2, P(144), P(32));
            details.SetBounds(0, status.Bottom + gap, w, detailsH); var half = w / 2; var rowH = (detailsH - P(12)) / 3;
            var keys = _facts.Keys.ToArray();
            for (var index = 0; index < keys.Length; index++)
            {
                var col = index / 3; var row = index % 3; var x = col * half + inset; var labelW = P(col == 0 ? 62 : 88);
                captions[index].SetBounds(x, P(6) + row * rowH, labelW, rowH);
                _facts[keys[index]].SetBounds(x + labelW + P(6), P(6) + row * rowH, Math.Max(0, half - inset * 2 - labelW - P(6)), rowH);
            }
            var actionTop = details.Bottom + gap; groupCaption.Visible = !compact;
            groupCaption.SetBounds(0, actionTop, w, P(20)); if (!compact) actionTop += P(24);
            var comboHeight = _connectGroup.PreferredHeight; var actionsH = Math.Max(P(32), comboHeight);
            _connectGroup.SetBounds(0, actionTop + (actionsH - comboHeight) / 2, Math.Max(0, w - P(252)), comboHeight);
            connect.SetBounds(w - P(242), actionTop, P(98), P(32)); pause.SetBounds(w - P(136), actionTop, P(136), P(32));
            var linksTop = actionTop + actionsH + gap;
            portal.SetBounds(0, linksTop, P(116), P(22)); privacy.SetBounds(P(130), linksTop, P(152), P(22));
            note.SetBounds(P(300), linksTop, Math.Max(0, w - P(300)), P(22));
        };
    }
    private Button MoreButton(params (string Text, Func<Task> Action)[] actions)
    {
        var button = Theme.Button("More…"); var menu = Theme.Menu(); _menus.Add(menu);
        foreach (var (text, action) in actions) menu.Items.Add(text, null, async (_, _) => await action());
        button.Click += (_, _) => menu.Show(button, new Point(0, button.Height)); return button;
    }
    private static LayoutSurface FillLayout(Panel parent)
    { var layout = new LayoutSurface { Dock = DockStyle.Fill }; parent.Controls.Add(layout); return layout; }
    private Label EmptyGrid(Panel parent, string text)
    {
        var label = Theme.Label(text, 10); label.TextAlign = ContentAlignment.MiddleCenter; label.ForeColor = Theme.Muted; label.BackColor = Theme.Surface;
        label.Dock = DockStyle.Fill; parent.Controls.Add(label); label.BringToFront(); return label;
    }
    private void BuildNetworks()
    {
        var page = NewPage("Networks", "SSID names, login addresses and accounts belong to a network group."); var layout = FillLayout(page.Body);
        var add = Theme.Button("Add group", true); var edit = Theme.Button("Edit"); var remove = Theme.Button("Remove");
        add.Click += async (_, _) => await EditGroupAsync(null); edit.Click += async (_, _) => await EditGroupAsync(SelectedId(_groups));
        remove.Click += async (_, _) => await Command(async () =>
        {
            var id = SelectedId(_groups); if (id is null || MessageBox.Show(this, "Remove this group and its saved accounts?", "UniLink", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var config = _engine.SnapshotConfig(); config.Groups.RemoveAll(g => g.Id == id); await _engine.ApplyConfigAsync(config);
        });
        var more = MoreButton(("Move group up", () => MoveGroupAsync(-1)), ("Move group down", () => MoveGroupAsync(1)),
            ("Export network template…", () => { ExportTemplate(); return Task.CompletedTask; }), ("Import network template…", ImportTemplateAsync));
        var bar = new ActionBar(); bar.Add(add, 116); bar.Add(edit, 64); bar.Add(remove, 80); bar.Add(more, 76);
        var table = new CardPanel(); table.Controls.Add(_groups); _emptyGroups = EmptyGrid(table, "Add a group to configure your university network.");
        _groupActions.AddRange([edit, remove]); layout.Controls.AddRange([bar, table]);
        Theme.Columns(_groups, ("name", "Group", 125, 125), ("ssid", "SSIDs", 115, 120), ("portal", "Primary portal", 190, 200), ("count", "Accounts", 70, 70), ("enabled", "Enabled", 65, 65));
        _groups.SelectionChanged += (_, _) => UpdateSelectionActions(); _groups.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await EditGroupAsync(SelectedId(_groups)); };
        layout.Arrange = () =>
        { var h = Theme.Px(layout, 32); bar.SetBounds(0, 0, layout.Width, h); table.SetBounds(0, h + Theme.Px(layout, 8), layout.Width, Math.Max(0, layout.Height - h - Theme.Px(layout, 8))); };
    }
    private void BuildAccounts()
    {
        var page = NewPage("Accounts", "The default account is tried first; backups follow your saved order."); var layout = FillLayout(page.Body);
        _accountGroup.SelectedIndexChanged += (_, _) => RefreshAccounts();
        var add = Theme.Button("Add account", true); var edit = Theme.Button("Edit"); var remove = Theme.Button("Remove"); var primary = Theme.Button("Set default"); var use = Theme.Button("Use account");
        add.Click += async (_, _) => await EditAccountAsync(null); edit.Click += async (_, _) => await EditAccountAsync(SelectedId(_accounts));
        remove.Click += async (_, _) => await Command(async () =>
        {
            if (SelectedId(_accounts) is not { } id || (_accountGroup.SelectedItem as Choice)?.Id is not { } groupId) return;
            if (MessageBox.Show(this, "Remove this saved account?", "UniLink", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var config = _engine.SnapshotConfig(); config.Groups.First(g => g.Id == groupId).Accounts.RemoveAll(a => a.Id == id); await _engine.ApplyConfigAsync(config);
        });
        primary.Click += async (_, _) => await Command(async () =>
        {
            if (SelectedId(_accounts) is not { } id || (_accountGroup.SelectedItem as Choice)?.Id is not { } groupId) return;
            var config = _engine.SnapshotConfig(); config.Groups.First(g => g.Id == groupId).DefaultAccountId = id; await _engine.ApplyConfigAsync(config);
        });
        use.Click += async (_, _) => await Command(async () =>
        { if (SelectedId(_accounts) is { } id && (_accountGroup.SelectedItem as Choice)?.Id is { } groupId) { await _engine.ConnectAsync(groupId, id); Navigate("Connection"); } });
        var more = MoreButton(("Move account up", () => MoveAccountAsync(-1)), ("Move account down", () => MoveAccountAsync(1)), ("Clear account block…", () => Command(async () =>
        {
            if (SelectedId(_accounts) is not { } id || (_accountGroup.SelectedItem as Choice)?.Id is not { } groupId) return;
            if (MessageBox.Show(this, "Try this account again now? Use this after fixing its password or adding more quota.", "UniLink", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) await _engine.ResetAccountAsync(groupId, id);
        })));
        var bar = new ActionBar(); bar.Add(add, 116); bar.Add(edit, 60); bar.Add(remove, 76); bar.Add(primary, 100); bar.Add(use, 104); bar.Add(more, 72);
        _accountActions.AddRange([edit, remove, primary, use]); var table = new CardPanel(); table.Controls.Add(_accounts);
        _emptyAccounts = EmptyGrid(table, "No accounts yet. Add your student username and password.");
        layout.Controls.AddRange([_accountGroup, bar, table]);
        Theme.Columns(_accounts, ("name", "Account", 125, 125), ("username", "Username", 140, 150), ("role", "Priority", 80, 80), ("state", "Status", 190, 220));
        _accounts.SelectionChanged += (_, _) => UpdateSelectionActions(); _accounts.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await EditAccountAsync(SelectedId(_accounts)); };
        layout.Arrange = () =>
        {
            _accountGroup.SetBounds(0, 0, Math.Min(layout.Width, Theme.Px(layout, 360)), _accountGroup.PreferredHeight);
            bar.SetBounds(0, Theme.Px(layout, 38), layout.Width, Theme.Px(layout, 32));
            table.SetBounds(0, Theme.Px(layout, 78), layout.Width, Math.Max(0, layout.Height - Theme.Px(layout, 78)));
        };
    }
    private void UpdateSelectionActions()
    {
        foreach (var button in _accountActions) button.Enabled = SelectedId(_accounts) is not null;
        foreach (var button in _groupActions) button.Enabled = SelectedId(_groups) is not null;
    }
    private void BuildActivity()
    {
        var page = NewPage("Activity", "Local connection events. Export a log when you need to investigate a problem."); var layout = FillLayout(page.Body);
        var export = Theme.Button("Export log"); export.Click += (_, _) =>
        {
            using var dialog = new SaveFileDialog { Filter = "Text log|*.txt", FileName = "UniLink-diagnostics.txt" }; if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try { File.WriteAllText(dialog.FileName, _log.Export(), new System.Text.UTF8Encoding(false)); }
            catch (IOException) { MessageBox.Show(this, "The log file could not be saved.", "UniLink"); }
        };
        var box = new CardPanel(); box.Controls.Add(_activity); layout.Controls.AddRange([export, box]);
        layout.Arrange = () =>
        {
            export.SetBounds(0, 0, Theme.Px(layout, 116), Theme.Px(layout, 32)); box.SetBounds(0, Theme.Px(layout, 40), layout.Width, Math.Max(0, layout.Height - Theme.Px(layout, 40)));
            var p = Theme.Px(layout, 12); _activity.SetBounds(p, p, Math.Max(0, box.Width - p * 2), Math.Max(0, box.Height - p * 2));
        };
    }
    private void BuildSettings()
    {
        var page = NewPage("Settings", "Choose how UniLink starts and runs in the background."); var canvas = FillLayout(page.Body);
        var rows = new List<(CardPanel Card, Label Title, Label Description, CheckBox Check)>();
        foreach (var (check, title, description) in new[]
        {
            (_auto, "Automatic sign-in", "Connect when a saved network is detected."),
            (_closeToTray, "Keep running when closed", "Keep your session active in the tray."),
            (_startup, "Start with Windows", "Launch after you sign in to Windows.")
        })
        {
            var card = new CardPanel(); var name = Theme.Label(title, 9.5f, true); var desc = Theme.Label(description, 9); desc.ForeColor = Theme.Muted;
            card.Controls.AddRange([name, desc, check]); rows.Add((card, name, desc, check)); canvas.Controls.Add(card);
        }
        var save = Theme.Button("Save settings", true); save.Click += async (_, _) => await Command(async () =>
        {
            var config = _engine.SnapshotConfig(); config.AutoConnect = _auto.Checked; config.CloseToTray = _closeToTray.Checked;
            await _engine.ApplyConfigAsync(config); StartupRegistration.Set(_startup.Checked); _log.Write("Application settings saved.");
        });
        var note = Theme.Label("Use a permanent folder for startup. Accounts stay protected on this PC.", 8.5f); note.ForeColor = Theme.Muted;
        _tips.SetToolTip(note, "Keep UniLink.exe in a permanent folder before enabling startup. Saved accounts are encrypted for your Windows user.");
        canvas.Controls.AddRange([save, note]);
        canvas.Arrange = () =>
        {
            int P(int n) => Theme.Px(canvas, n); var w = canvas.Width; var compact = canvas.Height < P(300); var gap = P(compact ? 6 : 10);
            var cardH = Math.Min(P(76), Math.Max(P(48), (canvas.Height - P(40) - gap * 3) / 3)); var y = 0;
            foreach (var (card, title, desc, check) in rows)
            {
                card.SetBounds(0, y, w, cardH); var labelW = Math.Max(0, w - P(126));
                title.SetBounds(P(12), P(4), labelW, P(22)); desc.SetBounds(P(12), P(26), labelW, Math.Max(P(22), cardH - P(28)));
                check.SetBounds(w - P(98), (cardH - P(26)) / 2, P(86), P(26)); y += cardH + gap;
            }
            save.SetBounds(0, y, P(122), P(32)); note.SetBounds(P(136), y, Math.Max(0, w - P(136)), P(32));
        };
    }
    private void RefreshLists()
    {
        var config = _engine.SnapshotConfig();
        var key = string.Join("|", config.Groups.Select(g => $"{g.Id}:{g.Name}:{g.Enabled}:{string.Join(',', g.Ssids)}:{g.MatchByPortal}:{g.Accounts.Count}:{g.Portals.FirstOrDefault()?.Url}"));
        if (key != _groupListKey)
        {
            _groupListKey = key; var selected = SelectedId(_groups); _groups.Rows.Clear();
            foreach (var g in config.Groups)
            { var row = _groups.Rows[_groups.Rows.Add(g.Name, string.Join(", ", g.Ssids) + (g.MatchByPortal ? " + portal" : ""), g.Portals.FirstOrDefault()?.Url, g.Accounts.Count, g.Enabled ? "Yes" : "No")]; row.Tag = g.Id; }
            SelectRow(_groups, selected); UpdateCombo(_connectGroup, config.Groups); UpdateCombo(_accountGroup, config.Groups);
        }
        _emptyGroups.Visible = _groups.Rows.Count == 0;
        RefreshAccounts(); UpdateSelectionActions();
    }
    private void RefreshAccounts()
    {
        if (_accountGroup.SelectedItem is not Choice choice) { _accounts.Rows.Clear(); _emptyAccounts.Visible = true; _accountListKey = ""; return; }
        var group = _engine.SnapshotConfig().Groups.FirstOrDefault(g => g.Id == choice.Id); if (group is null) return;
        var key = group.Id + group.DefaultAccountId + string.Join("|", group.Accounts.Select(a => $"{a.Id}:{a.Label}:{a.Username}:{a.Enabled}:{a.ExhaustedMonth}:{a.QuotaNextProbeAt}:{a.NeedsAttention}"));
        if (_accountListKey == key) return; _accountListKey = key;
        var selected = SelectedId(_accounts); _accounts.Rows.Clear();
        foreach (var a in group.Accounts)
        {
            var state = !a.Enabled ? "Disabled" : a.NeedsAttention ? "Check credentials" : !a.Eligible(group.MonthAt(DateTimeOffset.UtcNow), DateTimeOffset.UtcNow)
                ? a.QuotaNextProbeAt is { } retry ? "Quota reset probe: " + retry.ToLocalTime().ToString("HH:mm") : "Quota used — resets next month" : "Ready";
            var row = _accounts.Rows[_accounts.Rows.Add(a.Label, a.Username, a.Id == group.DefaultAccountId ? "Default" : "Backup", state)]; row.Tag = a.Id;
        }
        SelectRow(_accounts, selected); _emptyAccounts.Visible = _accounts.Rows.Count == 0; UpdateSelectionActions();
    }
    private static void UpdateCombo(ComboBox combo, List<NetworkGroup> groups)
    {
        var selected = (combo.SelectedItem as Choice)?.Id; combo.BeginUpdate(); combo.Items.Clear();
        foreach (var g in groups) combo.Items.Add(new Choice(g.Id, g.Name));
        var index = combo.Items.Cast<Choice>().ToList().FindIndex(c => c.Id == selected); combo.SelectedIndex = index >= 0 ? index : combo.Items.Count > 0 ? 0 : -1; combo.EndUpdate();
    }
    private static string? SelectedId(DataGridView grid) => grid.SelectedRows.Count > 0 ? grid.SelectedRows[0].Tag as string : null;
    private static void SelectRow(DataGridView grid, string? id)
    {
        foreach (DataGridViewRow row in grid.Rows) if ((string?)row.Tag == id) { row.Selected = true; grid.CurrentCell = row.Cells[0]; return; }
    }
    private async Task EditGroupAsync(string? id) => await Command(async () =>
    {
        var config = _engine.SnapshotConfig(); var previous = config.Groups.FirstOrDefault(g => g.Id == id);
        using var dialog = new GroupEditor(previous); if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (previous is null) config.Groups.Add(dialog.Result); else config.Groups[config.Groups.IndexOf(previous)] = dialog.Result;
        await _engine.ApplyConfigAsync(config);
    });
    private async Task EditAccountAsync(string? id) => await Command(async () =>
    {
        if (_accountGroup.SelectedItem is not Choice choice) return;
        var config = _engine.SnapshotConfig(); var group = config.Groups.First(g => g.Id == choice.Id); var previous = group.Accounts.FirstOrDefault(a => a.Id == id);
        using var dialog = new AccountEditor(previous); if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (previous is null) { group.Accounts.Add(dialog.Result); group.DefaultAccountId ??= dialog.Result.Id; }
        else group.Accounts[group.Accounts.IndexOf(previous)] = dialog.Result;
        await _engine.ApplyConfigAsync(config);
    });
    private async Task MoveGroupAsync(int direction) => await Command(async () =>
    {
        var config = _engine.SnapshotConfig(); var i = config.Groups.FindIndex(g => g.Id == SelectedId(_groups)); var j = i + direction;
        if (i < 0 || j < 0 || j >= config.Groups.Count) return;
        (config.Groups[i], config.Groups[j]) = (config.Groups[j], config.Groups[i]); await _engine.ApplyConfigAsync(config);
    });
    private async Task MoveAccountAsync(int direction) => await Command(async () =>
    {
        if (_accountGroup.SelectedItem is not Choice choice) return;
        var config = _engine.SnapshotConfig(); var group = config.Groups.First(g => g.Id == choice.Id); var i = group.Accounts.FindIndex(a => a.Id == SelectedId(_accounts)); var j = i + direction;
        if (i < 0 || j < 0 || j >= group.Accounts.Count) return;
        (group.Accounts[i], group.Accounts[j]) = (group.Accounts[j], group.Accounts[i]); await _engine.ApplyConfigAsync(config);
    });
    private async Task ReviewCertificateAsync() => await Command(async () =>
    {
        if (_engine.PendingCertificate is not { } cert) return;
        using var dialog = new CertificateDialog(cert);
        if (dialog.ShowDialog(this) == DialogResult.OK) await _engine.ApproveCertificateAsync(cert);
    });
    private void RenderStatus()
    {
        var s = PreviewStatus ?? _engine.Status; _state.Text = s.State switch
        {
            ConnectionState.Online => "Connected", ConnectionState.Connecting => "Signing in…", ConnectionState.Certificate => "Review certificate",
            ConnectionState.Paused => "Paused", ConnectionState.NoAccounts => "Account needed", ConnectionState.Attention => "Needs attention",
            ConnectionState.Retry => "Waiting to retry", _ => "Waiting for campus Wi-Fi"
        };
        _state.ForeColor = s.State == ConnectionState.Online ? Theme.Green : s.State is ConnectionState.Attention or ConnectionState.Certificate ? Theme.Red : Theme.Accent;
        _detail.Text = s.Detail; _tips.SetToolTip(_detail, s.Detail); _trust.Visible = _engine.PendingCertificate is not null || _uiCheck && s.State == ConnectionState.Certificate; _trust.Enabled = _engine.PendingCertificate is not null; _dashboard.PerformLayout();
        _facts["Network"].Text = Empty(s.Ssid); _facts["Group"].Text = Empty(s.Group); _facts["Account"].Text = Empty(s.Account); _facts["Portal"].Text = Empty(s.Portal);
        _facts["Last keepalive"].Text = s.LastHeartbeat?.ToLocalTime().ToString("HH:mm:ss") ?? "—";
        _facts["Next action"].Text = s.NextAction is { } next ? next.ToLocalTime().ToString(next.ToLocalTime().Date == DateTime.Now.Date ? "HH:mm:ss" : "yyyy-MM-dd HH:mm") : "—";
        foreach (var fact in _facts.Values) _tips.SetToolTip(fact, fact.Text);
        if (_tray is not null) _tray.Text = "UniLink — " + _state.Text;
        if (_tray is not null && _lastTrayState != s.State && s.State is ConnectionState.Certificate or ConnectionState.Attention && !Visible)
            _tray.ShowBalloonTip(5000, "UniLink", _state.Text + ". Open UniLink to continue.", ToolTipIcon.Info);
        _lastTrayState = s.State; RefreshLists();
    }
    private static string Empty(string s) => string.IsNullOrEmpty(s) ? "—" : s;
    private void ExportTemplate()
    {
        using var dialog = new SaveFileDialog { Filter = "UniLink network template|*.json", FileName = "UniLink-networks.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(_engine.SnapshotConfig().NetworkTemplate(), AppConfig.JsonOptions), new System.Text.UTF8Encoding(false)); }
        catch (IOException) { MessageBox.Show(this, "The template could not be saved.", "UniLink"); }
    }
    private async Task ImportTemplateAsync() => await Command(async () =>
    {
        using var dialog = new OpenFileDialog { Filter = "UniLink network template|*.json" }; if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (new FileInfo(dialog.FileName).Length > 1048576) throw new InvalidDataException("Template is too large.");
        AppConfig imported;
        try { imported = (JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(dialog.FileName), AppConfig.JsonOptions) ?? throw new InvalidDataException("Invalid template.")).NetworkTemplate(); imported.Validate(); }
        catch (JsonException) { throw new InvalidDataException("Invalid JSON template."); }
        var config = _engine.SnapshotConfig();
        foreach (var group in imported.Groups)
        { group.Id = Guid.NewGuid().ToString("N"); foreach (var p in group.Portals) p.Id = Guid.NewGuid().ToString("N"); config.Groups.Add(group); }
        await _engine.ApplyConfigAsync(config);
    });
    private static void Open(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { MessageBox.Show("Windows could not open the requested page.", "UniLink"); }
    }
    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_exiting || _uiCheck) return;
        if (e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing) { _timer.Stop(); _engine.NetworkChanged(); return; }
        e.Cancel = true;
        if (_engine.SnapshotConfig().CloseToTray) Hide(); else await ExitAsync();
    }
    private async Task ExitAsync()
    {
        if (_exiting) return; _exiting = true; _timer.Stop();
        try { await _engine.PauseAsync(); }
        finally { if (_tray is not null) _tray.Visible = false; Close(); }
    }
}
