using System.Drawing.Drawing2D;

namespace HeyTarkov;

/// <summary>
/// The other way of asking: a picture instead of a voice. A disc the size of
/// the microphone, standing beside it, so the two read as a pair - speak, or
/// show - rather than as a microphone with a small button somewhere else.
///
/// The disc holds a viewfinder with lines of text inside it: a capture, read.
/// Lit while the list is showing what a picture was read as; pressing it then
/// puts the ordinary list back.
/// </summary>
public sealed class ScanButton : Control
{
    private bool _hot;
    private bool _down;
    private bool _active;

    public ScanButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    /// <summary>The picture's rows are what the list shows.</summary>
    [System.ComponentModel.DefaultValue(false)]
    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value) return;

            _active = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var ring = LogicalToDeviceUnits(2);
        var side = Math.Min(Width, Height) - ring;
        if (side <= 2) return;

        var box = new RectangleF((Width - side) / 2f, (Height - side) / 2f, side, side);

        var ringColour = _active || _hot ? Theme.Accent : Theme.Edge;
        var ink = _active ? Theme.AccentText : _hot ? Theme.Text : Theme.Muted;
        var fill = _active ? Theme.Accent : _down ? Theme.Panel : Theme.PanelHi;

        using (var brush = new SolidBrush(fill)) g.FillEllipse(brush, box);

        using (var pen = new Pen(ringColour, ring))
            g.DrawEllipse(pen, box.X + ring / 2f, box.Y + ring / 2f, side - ring, side - ring);

        var span = side * 0.5f;
        var glyph = new RectangleF(box.X + (side - span) / 2f, box.Y + (side - span) / 2f, span, span);
        Painting.ScanGlyph(g, glyph, ink, Math.Max(1.4f, side * 0.03f));

        if (Focused && ShowFocusCues)
        {
            var inset = ring * 2.5f;
            using var pen = new Pen(Theme.Accent, LogicalToDeviceUnits(1)) { DashStyle = DashStyle.Dot };
            g.DrawEllipse(pen, box.X + inset, box.Y + inset, side - inset * 2, side - inset * 2);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hot = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hot = false; _down = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _down = true; Focus(); Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _down = false; Invalidate(); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            e.Handled = true;
            OnClick(EventArgs.Empty);
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnChangeUICues(UICuesEventArgs e) { Invalidate(); base.OnChangeUICues(e); }
}
