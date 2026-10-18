using System.Drawing.Imaging;
using UniLink.Core;

namespace UniLink.Windows;

// Developer-only, offline UI inspection. Uses synthetic accounts and never opens a portal or user settings.
internal static class UiChecks
{
    public static int Run(string directory)
    {
        Directory.CreateDirectory(directory); var failures = new List<string>(); var inspected = 0;
        try
        {
            var config = AppConfig.CreateDefault(); config.AutoConnect = false;
            var group = config.Groups[0]; group.Name = "Urmia University";
            group.Accounts = [new Account { Label = "My student account", Username = "demo.student", Password = "synthetic-password" },
                new Account { Label = "حساب جایگزین دانشگاه", Username = "demo.backup", Password = "synthetic-password", ExhaustedMonth = group.MonthAt(DateTimeOffset.UtcNow) }];
            group.DefaultAccountId = group.Accounts[0].Id;
            var log = new ActivityLog(); log.Write("Sign-in accepted; background keepalive started."); log.Write("Keepalive accepted.");
            using var engine = new ConnectionEngine(config, new NoDiskStore(), new NoNetworkFactory(), log);
            using (var form = new MainForm(engine, null, log, false, true))
            {
                form.Show(); Pump(form); form.MinimumSize = Size.Empty;
                form.PreviewStatus = new(ConnectionState.Online, "Signed in. Your session is being kept alive in the background.", "UUWN", group.Name, group.Accounts[0].Label, group.Portals[0].Url, DateTimeOffset.UtcNow.AddSeconds(180), DateTimeOffset.UtcNow);
                foreach (var (width, height) in new[] { (900, 550), (760, 420), (640, 340), (640, 300), (1100, 700) })
                {
                    form.ClientSize = new Size(Theme.Px(form, width), Theme.Px(form, height)); Pump(form);
                    foreach (var page in new[] { "Connection", "Networks", "Accounts", "Activity", "Settings" })
                    {
                        form.InspectPage(page); Pump(form); CheckTree(form, failures, $"{page}/{width}x{height}");
                        if (page is "Networks" or "Accounts")
                            foreach (var grid in Descendants(form).OfType<DataGridView>().Where(g => g.Visible))
                                if (grid.Height < Theme.Px(grid, 110)) failures.Add($"{page}: table too short");
                        Save(form, directory, $"{page.ToLowerInvariant()}-{width}x{height}-dpi{form.DeviceDpi}.png"); inspected++;
                    }
                }
                form.PreviewStatus = new(ConnectionState.Retry, "Unclassified portal response. A long explanatory message should stay inside the status card without covering any action buttons or the network details.");
                form.InspectPage("Connection"); Pump(form); CheckTree(form, failures, "long status"); Save(form, directory, "connection-long-status.png"); inspected++;
                form.PreviewStatus = new(ConnectionState.Certificate, "Approve the expected university server certificate before your first sign-in.", Portal: group.Portals[0].Url);
                form.InspectPage("Connection"); Pump(form); CheckTree(form, failures, "certificate status"); Save(form, directory, "connection-certificate.png"); inspected++;
                form.ClientSize = new Size(Theme.Px(form, 640), Theme.Px(form, 300));
                foreach (var state in new[] { ConnectionState.Retry, ConnectionState.Certificate })
                {
                    form.PreviewStatus = new(state, "Approve the expected university server certificate before signing in. A long status message must not cover the action buttons.", Portal: group.Portals[0].Url);
                    form.InspectPage("Connection"); Pump(form); CheckTree(form, failures, "compact " + state); Save(form, directory, "compact-" + state + ".png"); inspected++;
                }
                form.Close();
            }
            foreach (var account in new Account?[] { null, group.Accounts[0] })
            {
                using var dialog = new AccountEditor(account); dialog.Show(); Pump(dialog);
                CheckTree(dialog, failures, account is null ? "add account" : "edit account");
                Save(dialog, directory, account is null ? "add-account.png" : "edit-account.png"); dialog.Close(); inspected++;
            }
            using (var dialog = new GroupEditor(group))
            {
                dialog.Show(); Pump(dialog);
                foreach (var advanced in new[] { 0, 1, 2 })
                {
                    dialog.InspectTab(advanced); Pump(dialog); CheckTree(dialog, failures, "group tab " + advanced);
                    Save(dialog, directory, "group-tab-" + advanced + ".png"); inspected++;
                }
                dialog.MinimumSize = Size.Empty; dialog.ClientSize = new Size(Theme.Px(dialog, 560), Theme.Px(dialog, 300));
                foreach (var tab in new[] { 0, 1, 2 }) { dialog.InspectTab(tab); Pump(dialog); CheckTree(dialog, failures, "small group tab " + tab); Save(dialog, directory, "group-small-" + tab + ".png"); inspected++; }
                dialog.Close();
            }
            using (var dialog = new CertificateDialog(new("test", group.Portals[0].Url, new string('A', 64), "CN=Campus authentication", "CN=University IT", DateTime.UtcNow.AddYears(1), false)))
            {
                dialog.Show(); Pump(dialog); CheckTree(dialog, failures, "certificate"); Save(dialog, directory, "certificate.png"); inspected++;
                dialog.MinimumSize = Size.Empty; dialog.ClientSize = new Size(Theme.Px(dialog, 520), Theme.Px(dialog, 300)); Pump(dialog);
                CheckTree(dialog, failures, "compact certificate"); Save(dialog, directory, "certificate-small.png"); dialog.Close(); inspected++;
            }
            var text = $"{inspected} native WinForms views inspected; {failures.Count} layout failures.\n" + string.Join("\n", failures);
            File.WriteAllText(Path.Combine(directory, "ui-checks.txt"), text); return failures.Count == 0 ? 0 : 1;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(directory, "ui-checks-error.txt"), ex.ToString()); return 2; }
    }
    private static void Pump(Control control)
    { control.PerformLayout(); foreach (Control child in control.Controls) PumpLayout(child); Application.DoEvents(); control.PerformLayout(); Application.DoEvents(); }
    private static void PumpLayout(Control control) { control.PerformLayout(); foreach (Control child in control.Controls) PumpLayout(child); }
    private static IEnumerable<Control> Descendants(Control control)
    { foreach (Control child in control.Controls) { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; } }
    private static void CheckTree(Control parent, List<string> failures, string name)
    {
        if (parent is Form && !Descendants(parent).Any(c => c.Visible && c is Button && c.Width > 0)) failures.Add(name + ": no visible action buttons");
        if (parent is ScrollableControl scroll && scroll.AutoScroll) failures.Add(name + ": unexpected page scrollbar: " + parent.GetType().Name);
        if (parent is DataGridView grid && grid.Controls.OfType<HScrollBar>().Any(bar => bar.Visible)) failures.Add(name + ": horizontal table scrollbar");
        if (parent is DataGridView or TextBoxBase or ComboBox or NumericUpDown or ListBox) return;
        var visible = parent.Controls.Cast<Control>().Where(c => c.Visible && c.Width > 0 && c.Height > 0).ToList();
        foreach (var child in visible)
        {
            {
                var bounds = child.Bounds; var area = parent.ClientRectangle; area.Inflate(2, 2);
                if (!area.Contains(bounds)) failures.Add($"{name}: {child.GetType().Name} '{child.Text}' outside {parent.GetType().Name}: {bounds} / {parent.ClientRectangle}");
            }
            if (child is Label or Button or CheckBox)
            {
                var lineHeight = TextRenderer.MeasureText(child.Text, child.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Height;
                if (lineHeight > child.Height) failures.Add($"{name}: text line too tall for {child.GetType().Name} '{child.Text}'");
            }
            if (child is Button button)
            {
                var measured = TextRenderer.MeasureText(button.Text, button.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                if (measured.Width > button.Width - button.Padding.Horizontal - Theme.Px(button, 4)) failures.Add($"{name}: button text clipped: {button.Text}");
            }
            CheckTree(child, failures, name);
        }
        for (var i = 0; i < visible.Count; i++)
            for (var j = i + 1; j < visible.Count; j++)
            {
                if (visible[i].Dock == DockStyle.Fill || visible[j].Dock == DockStyle.Fill) continue;
                var a = visible[i].Bounds; var b = visible[j].Bounds; a.Inflate(-1, -1); b.Inflate(-1, -1);
                if (a.IntersectsWith(b)) failures.Add($"{name}: overlapping {visible[i].GetType().Name} '{visible[i].Text}' and {visible[j].GetType().Name} '{visible[j].Text}'");
            }
    }
    private static void Save(Form form, string directory, string name)
    { using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(directory, name), ImageFormat.Png); }
    private sealed class NoDiskStore : IConfigStore { public void Save(AppConfig config) { } }
    private sealed class NoNetworkFactory : IPortalFactory
    { public IPortalSession Create(PortalEndpoint endpoint, NetworkLink link) => throw new InvalidOperationException("UI check must not connect to a portal."); }
}
