using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace HeyTarkov;

/// <summary>
/// The hold-to-talk button and the input meter as one disc: hold it and it fills
/// from the bottom with the level. The microphone inverts where the fill has
/// passed it, so the glyph stays readable at any height, and the top of the
/// range turns red when the input is close to clipping.
///
/// It repaints only when a level arrives, and levels only arrive while the
/// microphone is open - there is no timer and nothing runs while the window
/// sits idle behind the game.
/// </summary>
public sealed class LevelDial : Control
{
    /// <summary>Above this fraction the input is close to clipping.</summary>
    private const float ClipFrom = 0.86f;

    /// <summary>
    /// Peaks jump around; letting the fill fall gradually makes it readable
    /// without a timer, since a new level only arrives while capturing.
    /// </summary>
    private const int Fall = 7;

    private int _value;
    private bool _holding;
    private bool _hot;

    public LevelDial()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = "押している間だけ聞き取ります";
    }

    /// <summary>Raised when the button goes down, by mouse or by space.</summary>
    public event EventHandler? HoldStarted;

    /// <summary>Raised when it comes back up. Always paired with a start.</summary>
    public event EventHandler? HoldEnded;

    /// <summary>0-100, matching AudioLevel.ToMeter.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            var next = Math.Clamp(value, 0, 100);
            if (next < _value) next = Math.Max(next, _value - Fall);
            if (next == _value) return;

            _value = next;
            Invalidate();
        }
    }

    /// <summary>
    /// What this sits on. Parent.BackColor cannot answer it: the layout panels
    /// are transparent so the cards show through them, and clearing to a
    /// transparent colour paints black.
    /// </summary>
    [DefaultValue(false)]
    public bool OnCard { get; set; }

    private Color Backdrop => OnCard ? Theme.Panel : Theme.Page;

    public bool Holding => _holding;

    /// <summary>Drop to empty at once, without the fall - the microphone closed.</summary>
    public void Reset()
    {
        if (_value == 0) return;
        _value = 0;
        Invalidate();
    }

    // ------------------------------------------------------------- pressing

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        Begin();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left) End();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Space or Keys.Enter) Begin();
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode is Keys.Space or Keys.Enter) End();
    }

    /// <summary>Space would otherwise never reach OnKeyDown on a raw Control.</summary>
    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Space or Keys.Enter || base.IsInputKey(keyData);

    protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { End(); Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { if (!Enabled) End(); Invalidate(); base.OnEnabledChanged(e); }

    private void Begin()
    {
        if (_holding || !Enabled) return;
        _holding = true;
        Invalidate();
        HoldStarted?.Invoke(this, EventArgs.Empty);
    }

    private void End()
    {
        if (!_holding) return;
        _holding = false;
        Invalidate();
        HoldEnded?.Invoke(this, EventArgs.Empty);
    }

    // -------------------------------------------------------------- drawing

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Backdrop);

        var ring = LogicalToDeviceUnits(2);
        var side = Math.Min(Width, Height) - ring;
        if (side <= 2) return;

        var box = new RectangleF((Width - side) / 2f, (Height - side) / 2f, side, side);
        var cx = box.X + side / 2f;
        var cy = box.Y + side / 2f;
        var level = Enabled ? _value / 100f : 0f;

        var ringColour = !Enabled ? Theme.Edge
            : _holding ? Theme.Accent
            : _hot ? Theme.Accent
            : Theme.Edge;

        var glyphColour = !Enabled ? Theme.Faint
            : _holding ? Theme.Accent
            : Theme.Muted;

        using (var brush = new SolidBrush(Theme.PanelHi)) g.FillEllipse(brush, box);

        if (level > 0) FillLevel(g, box, cx, cy, side / 2f, level);

        using (var pen = new Pen(ringColour, ring))
            g.DrawEllipse(pen, box.X + ring / 2f, box.Y + ring / 2f, side - ring, side - ring);

        var span = side * 0.575f;
        var stroke = Math.Max(1.4f, side * 0.028f);
        Painting.Mic(g, cx, cy, span, glyphColour, stroke);

        if (level > 0) InvertMicUnderFill(g, box, cx, cy, side / 2f, level, span, stroke);

        // Inside the ring, not outside it: the control is exactly the disc, so
        // an outer ring would be clipped away by its own bounds.
        if (Focused && Enabled)
        {
            var inset = ring * 2.5f;
            using var pen = new Pen(Theme.Accent, LogicalToDeviceUnits(1)) { DashStyle = DashStyle.Dot };
            g.DrawEllipse(pen, box.X + inset, box.Y + inset, side - inset * 2, side - inset * 2);
        }
    }

    private static void FillLevel(Graphics g, RectangleF box, float cx, float cy, float r, float level)
    {
        var state = g.Save();
        try
        {
            using var clip = new GraphicsPath();
            clip.AddEllipse(box);
            g.SetClip(clip, CombineMode.Intersect);

            var safe = Math.Min(level, ClipFrom);
            using (var brush = new SolidBrush(Theme.Accent))
                g.FillRectangle(brush, cx - r, cy + r - r * 2 * safe, r * 2, r * 2 * safe);

            if (level > ClipFrom)
                using (var brush = new SolidBrush(Theme.Clip))
                    g.FillRectangle(brush, cx - r, cy + r - r * 2 * level, r * 2, r * 2 * (level - ClipFrom));

            // A bright line at the surface, so the height reads at a glance.
            var top = cy + r - r * 2 * level;
            using (var pen = new Pen(Color.FromArgb(200, 255, 255, 255), 1.2f))
                g.DrawLine(pen, cx - r, top, cx + r, top);
        }
        finally
        {
            g.Restore(state);
        }
    }

    /// <summary>Repaint the swallowed part of the glyph in ink, so it stays legible.</summary>
    private static void InvertMicUnderFill(
        Graphics g, RectangleF box, float cx, float cy, float r, float level, float span, float stroke)
    {
        var state = g.Save();
        try
        {
            using var clip = new GraphicsPath();
            clip.AddEllipse(box);
            g.SetClip(clip, CombineMode.Intersect);
            g.IntersectClip(new RectangleF(cx - r, cy + r - r * 2 * level, r * 2, r * 2 * level));
            Painting.Mic(g, cx, cy, span, Theme.AccentText, stroke);
        }
        finally
        {
            g.Restore(state);
        }
    }
}
