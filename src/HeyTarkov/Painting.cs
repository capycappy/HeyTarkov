using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace HeyTarkov;

/// <summary>Shapes the painted controls share.</summary>
public static class Painting
{
    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var d = Math.Min(radius, Math.Min(r.Width, r.Height) / 2) * 2;
        var path = new GraphicsPath();

        if (d <= 0)
        {
            path.AddRectangle(r);
            return path;
        }

        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }

    public static void FillRounded(Graphics g, RectangleF r, Color colour, float radius)
    {
        using var path = Rounded(r, radius);
        using var brush = new SolidBrush(colour);
        g.FillPath(brush, path);
    }

    /// <summary>Inset by half the pen so the stroke lands inside the bounds.</summary>
    public static void DrawRounded(Graphics g, RectangleF r, Color colour, float radius, float width = 1f)
    {
        var inset = RectangleF.FromLTRB(
            r.Left + width / 2, r.Top + width / 2, r.Right - width / 2, r.Bottom - width / 2);

        using var path = Rounded(inset, radius);
        using var pen = new Pen(colour, width);
        g.DrawPath(pen, path);
    }

    /// <summary>
    /// The microphone, drawn rather than typed: the emoji WinForms would give us
    /// renders at a different weight in every theme, and an icon font is a
    /// dependency this app will not take.
    /// </summary>
    /// <summary>
    /// A check mark inside the given square. Lives here because the checklist
    /// rows and the button that opens them draw the same mark, and two hand-
    /// placed polylines drift apart.
    /// </summary>
    public static void Tick(Graphics g, RectangleF box, Color colour, float stroke)
    {
        using var pen = new Pen(colour, stroke)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };

        var side = box.Width;

        g.DrawLines(pen, new[]
        {
            new PointF(box.Left + side * 0.20f, box.Top + side * 0.52f),
            new PointF(box.Left + side * 0.42f, box.Top + side * 0.74f),
            new PointF(box.Left + side * 0.80f, box.Top + side * 0.26f),
        });
    }

    public static void Mic(Graphics g, float cx, float cy, float span, Color colour, float stroke)
    {
        using var brush = new SolidBrush(colour);
        using var pen = new Pen(colour, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        var capW = span * 0.34f;
        var capH = span * 0.52f;
        var top = cy - span * 0.44f;

        using (var capsule = Rounded(new RectangleF(cx - capW / 2, top, capW, capH), capW / 2))
            g.FillPath(brush, capsule);

        var r = span * 0.34f;
        var cradleCy = top + capH * 0.70f;
        g.DrawArc(pen, cx - r, cradleCy - r, r * 2, r * 2, 20, 140);

        var stemTop = cradleCy + r;
        g.DrawLine(pen, cx, stemTop, cx, stemTop + span * 0.16f);
        g.DrawLine(pen, cx - span * 0.17f, stemTop + span * 0.16f, cx + span * 0.17f, stemTop + span * 0.16f);
    }
}

/// <summary>
/// A rounded surface. WinForms panels are square, so the corners are painted
/// over the page colour the parent already drew.
/// </summary>
public class Card : Panel
{
    public Card()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
    }

    /// <summary>Corner radius in 96 DPI units; scaled with the monitor.</summary>
    [DefaultValue(10)]
    public int Radius { get; set; } = 10;

    /// <summary>Off for the search field, which sits on its own border.</summary>
    [DefaultValue(true)]
    public bool Outlined { get; set; } = true;

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Theme.Page);

        var r = new RectangleF(0, 0, Width, Height);
        var radius = LogicalToDeviceUnits(Radius);

        Painting.FillRounded(e.Graphics, r, Theme.Panel, radius);
        if (Outlined) Painting.DrawRounded(e.Graphics, r, Theme.Edge, radius, LogicalToDeviceUnits(1));

        base.OnPaint(e);
    }
}

/// <summary>
/// A flat button the app paints itself, in two weights: filled to read as a
/// button worth pressing, outlined for the ones that sit quietly beside
/// something else.
/// </summary>
public sealed class PillButton : Button
{
    private bool _hot;
    private bool _down;

    public PillButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
    }

    /// <summary>Outlined rather than filled: present, but not the main action.</summary>
    [DefaultValue(false)]
    public bool Ghost { get; set; }

    /// <summary>
    /// What this sits on. Parent.BackColor cannot answer it: the layout panels
    /// are transparent so the cards show through them, and clearing to a
    /// transparent colour paints black.
    /// </summary>
    [DefaultValue(false)]
    public bool OnCard { get; set; }

    private Color Backdrop => OnCard ? Theme.Panel : Theme.Page;


    [DefaultValue(8)]
    public int Radius { get; set; } = 8;

    /// <summary>
    /// A control this must be exactly as tall as.
    ///
    /// The height is copied rather than calculated. Working out the same
    /// expression a ComboBox uses lands two pixels out, because a ComboBox asks
    /// Windows for its height and does not always get what it asked for - and
    /// two pixels is enough to push a button past the bottom of the row it is
    /// laid out in and have it clipped.
    /// </summary>
    [DefaultValue(null)]
    public Control? SameHeightAs { get; set; }

    protected override void SetBoundsCore(
        int x, int y, int width, int height, BoundsSpecified specified)
    {
        var wanted = SameHeightAs is { Height: > 0 } other ? other.Height : height;
        base.SetBoundsCore(x, y, width, wanted, specified);
    }

    /// <summary>A tick drawn before the text, for a button that opens a list of
    /// things to tick off.</summary>
    [DefaultValue(false)]
    public bool Ticked { get; set; }

    protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hot = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Backdrop);

        var r = new RectangleF(0, 0, Width, Height);
        var radius = LogicalToDeviceUnits(Radius);
        var on = Enabled;

        Color fill, ink;

        if (Ghost)
        {
            fill = _down ? Theme.PanelHi : Theme.Panel;
            ink = on ? Theme.Muted : Theme.Faint;
            Painting.FillRounded(g, r, fill, radius);
            Painting.DrawRounded(g, r, _hot && on ? Theme.Accent : Theme.Edge, radius, LogicalToDeviceUnits(1));
        }
        else
        {
            fill = !on ? Theme.PanelHi
                : _down ? Shade(Theme.Accent, -0.14f)
                : _hot ? Shade(Theme.Accent, 0.10f)
                : Theme.Accent;
            ink = on ? Theme.AccentText : Theme.Faint;
            Painting.FillRounded(g, r, fill, radius);
        }

        if (Focused && on)
            Painting.DrawRounded(g, Rectangle.Inflate(new Rectangle(0, 0, Width, Height), -2, -2),
                Ghost ? Theme.Accent : Theme.AccentText, radius, LogicalToDeviceUnits(1));

        var box = new Rectangle(0, 0, Width, Height);

        if (Ticked)
        {
            // The tick sits at the left edge and the word sits in the middle of
            // the button. Centring the two together would make the button read
            // as "tick KAPPA品"; what it is called is KAPPA品, and the tick is
            // a mark on the button rather than part of its name.
            var side = LogicalToDeviceUnits(13);
            var inset = LogicalToDeviceUnits(13);

            Painting.Tick(g, new RectangleF(inset, (Height - side) / 2f, side, side), ink,
                Math.Max(1.6f, side * 0.16f));

            var textWidth = TextRenderer.MeasureText(g, Text, Font,
                new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;

            // Unless the word is long enough to reach the tick, in which case it
            // gives way - overlapping is worse than off-centre.
            var clear = inset + side + LogicalToDeviceUnits(8);
            var centred = (Width - textWidth) / 2;

            box = centred >= clear
                ? new Rectangle(0, 0, Width, Height)
                : new Rectangle(clear, 0, Width - clear - LogicalToDeviceUnits(6), Height);

            TextRenderer.DrawText(g, Text, Font, box, ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPrefix);
            return;
        }

        TextRenderer.DrawText(g, Text, Font, box, ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private static Color Shade(Color c, float by)
    {
        int Move(int v) => Math.Clamp((int)(v + (by > 0 ? (255 - v) * by : v * by)), 0, 255);
        return Color.FromArgb(c.A, Move(c.R), Move(c.G), Move(c.B));
    }
}

/// <summary>
/// The search field. A TextBox cannot have rounded corners or an icon, so the
/// card draws both and the borderless box sits inside its padding.
/// </summary>
public sealed class SearchCard : Card
{
    public SearchCard()
    {
        Radius = 8;
    }

    /// <summary>
    /// Selects whatever is in the box when it is clicked into, so the next
    /// keystroke replaces it. Coming back to a search box means looking for
    /// something else; the last search is there to be read, not edited.
    ///
    /// Both events are needed. A click gives focus and then puts the caret
    /// where it landed, which undoes a SelectAll made on focus alone. The flag
    /// keeps it to the click that arrives with the focus - clicking again
    /// inside the box is someone placing the caret, and that has to still work.
    /// </summary>
    public static void SelectAllWhenClickedInto(TextBox box)
    {
        var arriving = false;

        box.GotFocus += (_, _) =>
        {
            arriving = true;
            box.SelectAll();
        };

        box.MouseUp += (_, _) =>
        {
            if (!arriving) return;

            arriving = false;
            box.SelectAll();
        };

        box.Leave += (_, _) => arriving = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Painting.DrawRounded(e.Graphics, new RectangleF(0, 0, Width, Height), Theme.Field,
            LogicalToDeviceUnits(Radius), LogicalToDeviceUnits(1));

        var cx = LogicalToDeviceUnits(19);
        var cy = Height / 2f;
        var r = LogicalToDeviceUnits(5);

        using var pen = new Pen(Theme.Faint, Math.Max(1f, LogicalToDeviceUnits(1) * 1.3f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };

        g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
        g.DrawLine(pen, cx + r * 0.75f, cy + r * 0.75f, cx + r * 1.6f, cy + r * 1.6f);
    }
}
