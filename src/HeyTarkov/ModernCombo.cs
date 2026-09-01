using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace HeyTarkov;

/// <summary>
/// A dropdown the app draws itself.
///
/// The stock ComboBox still renders as a sunken 3D box with a grey button on
/// the right - the Windows 95 shape - and none of FlatStyle, BackColor or the
/// dark mode switch changes that outline. Owner drawing covers the items and
/// the closed value; the frame and the arrow are painted over what the OS drew,
/// after it has drawn it.
/// </summary>
public sealed class ModernCombo : ComboBox
{
    private const int WmPaint = 0x000F;

    /// <summary>Room on the right for the chevron, in 96 DPI units.</summary>
    private const int ArrowWidth = 26;

    private bool _hot;

    /// <summary>
    /// What this sits on. The square border has to be painted out in the
    /// surrounding colour, and a card is not the page.
    /// </summary>
    [DefaultValue(false)]
    public bool OnCard { get; set; }

    private Color Backdrop => OnCard ? Theme.Panel : Theme.Page;

    public ModernCombo()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
    }

    /// <summary>
    /// Left alone, a combo box is as tall as its font plus a couple of pixels -
    /// the 1995 proportion, and far shorter than the buttons beside it.
    /// </summary>
    private int DesiredHeight => Font.Height + LogicalToDeviceUnits(13);

    /// <summary>
    /// Forced, not merely assigned: the form's DPI scaling pass runs after the
    /// handle is created and puts the height it measured before then back,
    /// which is where the short box came from.
    /// </summary>
    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified) =>
        base.SetBoundsCore(x, y, width, DesiredHeight, specified);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyMetrics();
        ApplyColours();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (IsHandleCreated) ApplyMetrics();
    }

    private void ApplyMetrics()
    {
        ItemHeight = Font.Height + LogicalToDeviceUnits(8);
        Height = DesiredHeight;
    }

    /// <summary>Re-read the palette after the light/dark setting flips.</summary>
    public void ApplyColours()
    {
        BackColor = Theme.PanelHi;
        ForeColor = Theme.Text;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    /// <summary>
    /// Both the closed value and the rows of the open list arrive here; the
    /// ComboBoxEdit flag is what tells them apart.
    /// </summary>
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;

        var g = e.Graphics;
        var closed = (e.State & DrawItemState.ComboBoxEdit) != 0;
        var selected = !closed && (e.State & DrawItemState.Selected) != 0;

        using (var brush = new SolidBrush(closed ? Theme.PanelHi : selected ? Theme.PanelHi : Theme.Panel))
            g.FillRectangle(brush, e.Bounds);

        if (selected)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var barH = e.Bounds.Height - LogicalToDeviceUnits(8);
            Painting.FillRounded(g,
                new RectangleF(e.Bounds.X + LogicalToDeviceUnits(4),
                    e.Bounds.Y + (e.Bounds.Height - barH) / 2f, LogicalToDeviceUnits(3), barH),
                Theme.Accent, LogicalToDeviceUnits(2));
        }

        var pad = LogicalToDeviceUnits(closed ? 10 : 14);
        var text = Items[e.Index]?.ToString() ?? "";

        TextRenderer.DrawText(g, text, Font,
            new Rectangle(e.Bounds.X + pad, e.Bounds.Y,
                e.Bounds.Width - pad - LogicalToDeviceUnits(closed ? ArrowWidth : 8), e.Bounds.Height),
            selected ? Theme.Accent : Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
            | TextFormatFlags.NoPrefix);
    }

    /// <summary>
    /// The frame and the arrow belong to the OS, which draws them during
    /// WM_PAINT; painting after it is the only way to replace them without
    /// replacing the control.
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != WmPaint || !IsHandleCreated) return;

        using var g = CreateGraphics();
        PaintFrame(g);
    }

    private void PaintFrame(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // A COMBOBOX hands out a device context shorter than its own Height, so
        // a frame drawn to Width x Height loses its bottom edge off the end of
        // it. The context's own bounds are the only ones that reach every side.
        var area = g.VisibleClipBounds;
        var arrow = LogicalToDeviceUnits(ArrowWidth);
        var line = LogicalToDeviceUnits(1);
        var radius = LogicalToDeviceUnits(6);

        // Underneath all of this the OS has drawn its own square border around
        // the whole control, and filled the corners to match. Rounding the
        // frame on top of that leaves the square showing at every corner, so
        // the outermost ring is painted out in the surrounding colour first and
        // the rounded frame drawn one pixel inside where it used to be.
        var frame = RectangleF.FromLTRB(
            area.Left + line, area.Top + line, area.Right - line, area.Bottom - line);

        using (var outside = new GraphicsPath { FillMode = FillMode.Alternate })
        using (var rounded = Painting.Rounded(frame, radius))
        {
            // Inflated past the edge: an anti-aliased path boundary sitting
            // exactly on the outermost pixel covers only half of it, which
            // leaves the OS border showing through at 50%.
            outside.AddRectangle(RectangleF.Inflate(area, line * 2, line * 2));
            outside.AddPath(rounded, false);
            using var brush = new SolidBrush(Backdrop);
            g.FillPath(brush, outside);
        }

        // Cover the button the OS drew, then put a chevron where it was.
        using (var brush = new SolidBrush(Theme.PanelHi))
            g.FillRectangle(brush, frame.Right - arrow, frame.Top, arrow, frame.Height);

        var cx = frame.Right - arrow / 2f;
        var cy = frame.Top + frame.Height / 2f;
        var size = LogicalToDeviceUnits(4);

        using (var pen = new Pen(Enabled ? Theme.Muted : Theme.Faint, Math.Max(1.3f, line * 1.4f))
               {
                   StartCap = LineCap.Round,
                   EndCap = LineCap.Round,
                   LineJoin = LineJoin.Round,
               })
        {
            g.DrawLines(pen, new[]
            {
                new PointF(cx - size, cy - size / 2f),
                new PointF(cx, cy + size / 2f),
                new PointF(cx + size, cy - size / 2f),
            });
        }

        var border = !Enabled ? Theme.Edge
            : Focused || DroppedDown ? Theme.Accent
            : _hot ? Theme.Muted
            : Theme.Field;

        Painting.DrawRounded(g, frame, border, radius, line);
    }
}
