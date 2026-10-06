using System.ComponentModel;

namespace HeyTarkov;

/// <summary>
/// Which language the microphone listens in, as a switch standing beside it:
/// a speech bubble on top, then JA and EN one above the other, the chosen one
/// lit.
///
/// It was a dropdown in the settings card, which put the thing that changes
/// most often among the things that never change, and then a two-letter button
/// nobody could tell was about speaking. Standing next to the microphone, with
/// a bubble over it, it reads as "what the microphone hears".
/// </summary>
public sealed class SpeakSwitch : Control
{
    private RecognitionLanguage _language = RecognitionLanguage.Japanese;
    private int _hot = -1;
    private Font? _font;

    /// <summary>Raised when the other language is clicked.</summary>
    public event Action<RecognitionLanguage>? Picked;

    public SpeakSwitch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    [DefaultValue(RecognitionLanguage.Japanese)]
    public RecognitionLanguage Language
    {
        get => _language;
        set
        {
            if (_language == value) return;

            _language = value;
            Invalidate();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _font?.Dispose();
        _font = new Font("Yu Gothic UI", Font.SizeInPoints * 0.86f, FontStyle.Bold);
    }

    /// <summary>
    /// The bubble, then the two cells, left to right: it sits in a row of
    /// buttons now, in the microphone's settings, and reads as one of them.
    /// </summary>
    private (RectangleF Bubble, RectangleF Ja, RectangleF En) Cells()
    {
        var edge = LogicalToDeviceUnits(1);
        var gap = LogicalToDeviceUnits(2);
        var bubble = LogicalToDeviceUnits(20);
        var height = Height - edge * 2;
        var cell = (Width - edge * 2 - bubble - gap * 2) / 2f;

        return (
            new RectangleF(edge, edge, bubble, height),
            new RectangleF(edge + bubble + gap, edge, cell, height),
            new RectangleF(edge + bubble + gap + cell + gap, edge, cell, height));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var (bubble, ja, en) = Cells();

        // The bubble is small and sits in the middle of its band.
        var side = LogicalToDeviceUnits(14);
        var glyph = new RectangleF(bubble.X + (bubble.Width - side) / 2f, bubble.Y + (bubble.Height - side) / 2f, side, side);
        Painting.SpeechGlyph(g, glyph, Theme.Muted, Math.Max(1.4f, side * 0.11f));

        Cell(g, ja, "日本語", _language == RecognitionLanguage.Japanese, _hot == 1);
        Cell(g, en, "ENG", _language == RecognitionLanguage.English, _hot == 2);

        if (Focused && ShowFocusCues)
        {
            Painting.DrawRounded(g, Rectangle.Inflate(ClientRectangle, -1, -1), Theme.Accent,
                LogicalToDeviceUnits(6), LogicalToDeviceUnits(1), System.Drawing.Drawing2D.DashStyle.Dot);
        }
    }

    /// <summary>One language. The chosen one is a lit pill; the other is a word
    /// waiting to be clicked, a little brighter under the mouse.</summary>
    private void Cell(Graphics g, RectangleF box, string text, bool chosen, bool hot)
    {
        var inset = LogicalToDeviceUnits(1);
        var pill = RectangleF.Inflate(box, -inset, -inset);
        var radius = LogicalToDeviceUnits(5);

        if (chosen) Painting.FillRounded(g, pill, Theme.Accent, radius);
        else if (hot) Painting.FillRounded(g, pill, Theme.PanelHi, radius);

        var ink = chosen ? Theme.AccentText : hot ? Theme.Text : Theme.Muted;

        TextRenderer.DrawText(g, text, _font ?? Font, Rectangle.Round(pill), ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private int CellAt(Point at)
    {
        var (_, ja, en) = Cells();
        return ja.Contains(at) ? 1 : en.Contains(at) ? 2 : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hot = CellAt(e.Location);
        if (hot == _hot) return;

        _hot = hot;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hot = -1;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        Focus();

        var wanted = CellAt(e.Location) switch
        {
            1 => RecognitionLanguage.Japanese,
            2 => RecognitionLanguage.English,
            _ => (RecognitionLanguage?)null,
        };

        if (wanted is { } language && language != _language) Picked?.Invoke(language);
    }

    /// <summary>Space or Enter flips it, for anyone who reached it with Tab.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            e.Handled = true;
            Picked?.Invoke(_language == RecognitionLanguage.Japanese
                ? RecognitionLanguage.English
                : RecognitionLanguage.Japanese);
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnChangeUICues(UICuesEventArgs e) { Invalidate(); base.OnChangeUICues(e); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _font?.Dispose();
        base.Dispose(disposing);
    }
}
