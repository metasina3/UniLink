namespace UniLink.Windows;

// Logical pixels follow Windows' text scale; layouts use the available client area.
internal class AdaptiveForm : Form
{
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (IsHandleCreated && !IsDisposed) BeginInvoke(() => { if (!IsDisposed) { Theme.FitToScreen(this, false); PerformLayout(); } });
    }
}

internal sealed class PageSurface : Panel
{
    private readonly Label _title, _subtitle;
    public Panel Body { get; } = new() { BackColor = Theme.Background };
    public PageSurface(string title, string subtitle)
    {
        BackColor = Theme.Background;
        _title = Theme.Label(title, 16, true); _subtitle = Theme.Label(subtitle, 9); _subtitle.ForeColor = Theme.Muted;
        Controls.AddRange([_title, _subtitle, Body]);
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e); if (_title is null) return;
        int P(int n) => Theme.Px(this, n); var compact = Height < P(400); var p = P(16); var w = Math.Max(0, ClientSize.Width - p * 2);
        _title.SetBounds(p, P(8), w, P(28)); _subtitle.Visible = !compact;
        _subtitle.SetBounds(p, P(38), w, P(22)); var top = P(compact ? 44 : 70);
        Body.SetBounds(p, top, w, Math.Max(0, ClientSize.Height - top - P(12)));
    }
}

internal sealed class LayoutSurface : Panel
{
    public Action? Arrange { get; set; }
    public LayoutSurface() { BackColor = Theme.Background; DoubleBuffered = true; }
    protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); Arrange?.Invoke(); }
}

internal sealed class ActionBar : Panel
{
    private readonly List<(Control Control, int Width)> _items = [];
    public ActionBar() { BackColor = Theme.Background; }
    public void Add(Control control, int width) { _items.Add((control, width)); Controls.Add(control); }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e); var x = 0;
        foreach (var (control, logicalWidth) in _items)
        {
            var width = Theme.Px(this, logicalWidth); var height = control is ComboBox combo ? combo.PreferredHeight : Theme.Px(this, 32);
            control.SetBounds(x, Math.Max(0, (ClientSize.Height - height) / 2), width, height); x += width + Theme.Px(this, 6);
        }
    }
}

internal sealed class CardPanel : Panel
{
    public CardPanel() { BackColor = Theme.Surface; DoubleBuffered = true; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); using var pen = new Pen(Theme.Border);
        if (Width > 0 && Height > 0) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}

internal sealed class DialogFooter : Panel
{
    private readonly Button _save, _cancel;
    public DialogFooter(Button save, Button cancel) { _save = save; _cancel = cancel; BackColor = Theme.Surface; Controls.AddRange([save, cancel]); }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e); if (_cancel is null) return;
        int P(int n) => Theme.Px(this, n); var cancelW = P(88); var saveW = P(_save.Text.Length > 12 ? 152 : 112); var h = P(32);
        _cancel.SetBounds(Width - P(16) - cancelW, (Height - h) / 2, cancelW, h);
        _save.SetBounds(_cancel.Left - P(8) - saveW, (Height - h) / 2, saveW, h);
    }
}
