using System.Runtime.InteropServices;

namespace UniLink.Windows;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(15, 18, 24);
    public static readonly Color Surface = Color.FromArgb(22, 27, 35);
    public static readonly Color Input = Color.FromArgb(29, 35, 45);
    public static readonly Color Border = Color.FromArgb(43, 51, 65);
    public static readonly Color Text = Color.FromArgb(230, 235, 243);
    public static readonly Color Muted = Color.FromArgb(146, 158, 178);
    public static readonly Color Accent = Color.FromArgb(40, 103, 217);
    public static readonly Color AccentHover = Color.FromArgb(91, 151, 255);
    public static readonly Color Selection = Color.FromArgb(31, 52, 84);
    public static readonly Color Green = Color.FromArgb(111, 212, 167);
    public static readonly Color Red = Color.FromArgb(245, 147, 153);
    public static int Px(Control control, int value) => (int)Math.Round(value * control.DeviceDpi / 96d);

    public static Label Label(string text, float size = 9.5f, bool bold = false) => new()
    {
        Text = text, AutoSize = false, AutoEllipsis = true, BackColor = Color.Transparent, ForeColor = Text,
        Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), TextAlign = ContentAlignment.MiddleLeft,
        UseMnemonic = false, Margin = Padding.Empty
    };
    public static Button Button(string text, bool primary = false) => new()
    {
        Text = text, AutoSize = false, Height = 32, Width = 104, FlatStyle = FlatStyle.Flat,
        BackColor = primary ? Accent : Input, ForeColor = Text, Cursor = Cursors.Hand,
        UseVisualStyleBackColor = false, UseMnemonic = false, Margin = Padding.Empty, TabStop = true,
        FlatAppearance = { BorderSize = 0, MouseOverBackColor = primary ? AccentHover : Color.FromArgb(40, 48, 62), MouseDownBackColor = Selection }
    };
    public static LinkLabel Link(string text) => new()
    {
        Text = text, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, LinkColor = AccentHover,
        ActiveLinkColor = Text, VisitedLinkColor = AccentHover, LinkBehavior = LinkBehavior.HoverUnderline,
        BackColor = Color.Transparent, UseMnemonic = false
    };
    public static DataGridView Grid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false, MultiSelect = false, RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Surface, BorderStyle = BorderStyle.None, EnableHeadersVisualStyles = false,
            GridColor = Border, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None, RowTemplate = { Height = 32 },
            ColumnHeadersHeight = 32, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            AutoGenerateColumns = false, ScrollBars = ScrollBars.Vertical, ShowCellToolTips = true, ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText
        };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Surface, ForeColor = Text, SelectionBackColor = Selection, SelectionForeColor = Text,
            Padding = new Padding(6, 0, 6, 0), Font = new Font("Segoe UI", 9), WrapMode = DataGridViewTriState.False
        };
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        { BackColor = Input, ForeColor = Muted, Font = new Font("Segoe UI", 9), Padding = new Padding(6, 0, 6, 0), SelectionBackColor = Input };
        void ResizeRows() { grid.RowTemplate.Height = Px(grid, 32); grid.ColumnHeadersHeight = Px(grid, 32); foreach (DataGridViewRow row in grid.Rows) row.Height = Px(grid, 32); }
        grid.HandleCreated += (_, _) => ResizeRows(); grid.DpiChangedAfterParent += (_, _) => ResizeRows();
        return grid;
    }
    public static void Columns(DataGridView grid, params (string Id, string Caption, int Minimum, int Weight)[] columns)
    {
        foreach (var c in columns)
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = c.Id, HeaderText = c.Caption, MinimumWidth = c.Minimum, FillWeight = c.Weight, SortMode = DataGridViewColumnSortMode.NotSortable });
        void ResizeColumns() { for (var i = 0; i < columns.Length; i++) grid.Columns[i].MinimumWidth = Px(grid, columns[i].Minimum); }
        grid.HandleCreated += (_, _) => ResizeColumns(); grid.DpiChangedAfterParent += (_, _) => ResizeColumns();
    }
    public static ContextMenuStrip Menu() => new()
    { BackColor = Surface, ForeColor = Text, ShowImageMargin = false, Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()) };
    private sealed class DarkMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Surface;
        public override Color MenuItemSelected => Selection;
        public override Color MenuItemBorder => Selection;
        public override Color MenuBorder => Border;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Border;
    }
    public static void Apply(Control control)
    {
        if (control is Form form)
        {
            form.BackColor = Background; form.ForeColor = Text;
            form.HandleCreated += (_, _) => SetDarkTitleBar(form);
        }
        if (control is TextBoxBase box) { box.BackColor = Input; box.ForeColor = Text; box.BorderStyle = BorderStyle.FixedSingle; }
        if (control is ComboBox combo) { combo.BackColor = Input; combo.ForeColor = Text; combo.FlatStyle = FlatStyle.Flat; }
        if (control is NumericUpDown number) { number.BackColor = Input; number.ForeColor = Text; }
        if (control is ListBox list) { list.BackColor = Input; list.ForeColor = Text; list.BorderStyle = BorderStyle.FixedSingle; }
        if (control is CheckBox check) { check.ForeColor = Text; check.UseVisualStyleBackColor = false; check.FlatStyle = FlatStyle.Flat; }
        foreach (Control child in control.Controls) Apply(child);
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    private static void SetDarkTitleBar(Form form)
    {
        try { var enabled = 1; _ = DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int)); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }
    public static void FitToScreen(Form form, bool center = true)
    {
        if (form.WindowState == FormWindowState.Maximized) return;
        var work = Screen.FromControl(form).WorkingArea; var margin = Px(form, 12);
        var width = Math.Min(form.Width, work.Width - margin * 2); var height = Math.Min(form.Height, work.Height - margin * 2);
        if (form.MinimumSize.Width > width || form.MinimumSize.Height > height) form.MinimumSize = new Size(Math.Min(form.MinimumSize.Width, width), Math.Min(form.MinimumSize.Height, height));
        form.Size = new Size(width, height);
        form.Location = center ? new Point(work.Left + (work.Width - width) / 2, work.Top + (work.Height - height) / 2)
            : new Point(Math.Clamp(form.Left, work.Left, work.Right - width), Math.Clamp(form.Top, work.Top, work.Bottom - height));
    }
}

internal sealed record Choice(string Id, string Name) { public override string ToString() => Name; }
