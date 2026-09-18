using System.Drawing.Drawing2D;

namespace HeyTarkov;

/// <summary>How the candidate list is ordered.</summary>
public enum CandidateSort
{
    /// <summary>The order the search produced: best guess first.</summary>
    Relevance,

    Name,

    /// <summary>The katakana reading, or for a key the name the game shows.</summary>
    Reading,

    /// <summary>The trader for a task, the map for an extract or a key.</summary>
    Group,

    Score,
}

/// <summary>
/// The column titles above the candidate list, each one a way to reorder it.
///
/// The list and this header are separate controls drawing what has to read as
/// one table, so the header takes its columns from the list itself rather than
/// working them out again - including the width the list's scroll bar takes.
/// </summary>
public sealed class CandidateHeader : Control
{
    private readonly CandidateList _list;
    private Font? _font;
    private Font? _active;

    /// <summary>Raised when a column title is clicked.</summary>
    public event Action<CandidateSort>? Picked;

    public CandidateHeader(CandidateList list)
    {
        _list = list;
        _list.SizeChanged += (_, _) => Invalidate();

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

        Cursor = Cursors.Hand;
    }

    public CandidateSort Sort { get; private set; } = CandidateSort.Relevance;

    public bool Descending { get; private set; }

    /// <summary>Whether the reading column is there - it is only in Japanese.</summary>
    [System.ComponentModel.DefaultValue(false)]
    public bool ShowReading { get; set; }

    public void Show(CandidateSort sort, bool descending)
    {
        Sort = sort;
        Descending = descending;
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RebuildFonts();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        RebuildFonts();
    }

    private void RebuildFonts()
    {
        _font?.Dispose();
        _active?.Dispose();

        var size = Font.SizeInPoints * 0.84f;
        _font = new Font("Yu Gothic UI", size);
        _active = new Font("Yu Gothic UI", size, FontStyle.Bold);

        Height = _font.Height + LogicalToDeviceUnits(10);
    }

    private CandidateLayout At => CandidateLayout.For(_list, 0, _list.ClientSize.Width);

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != MouseButtons.Left) return;

        var at = At;

        var picked = e.X >= at.ScoreX - LogicalToDeviceUnits(8) ? CandidateSort.Score
            : e.X >= at.GroupX ? CandidateSort.Group
            : ShowReading && e.X >= at.ReadingX ? CandidateSort.Reading
            : CandidateSort.Name;

        Picked?.Invoke(picked);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_font is null) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Panel);

        var at = At;

        Column(g, Strings.CandidateColumnName, at.NameX, at.ReadingX - at.NameX, CandidateSort.Name, right: false);

        if (ShowReading)
        {
            Column(g, Strings.CandidateColumnReading, at.ReadingX, at.GroupX - at.ReadingX,
                CandidateSort.Reading, right: false);
        }

        Column(g, Strings.CandidateColumnGroup, at.GroupX, at.GroupW, CandidateSort.Group, right: true);
        Column(g, Strings.CandidateColumnScore, at.ScoreX - LogicalToDeviceUnits(8),
            at.ScoreW + LogicalToDeviceUnits(8), CandidateSort.Score, right: true);

        // A hairline under the titles, so they read as a header rather than as
        // a first row.
        using var pen = new Pen(Theme.Edge);
        var y = Height - 1;
        g.DrawLine(pen, LogicalToDeviceUnits(8), y, _list.ClientSize.Width - LogicalToDeviceUnits(8), y);
    }

    private void Column(Graphics g, string text, int x, int width, CandidateSort sort, bool right)
    {
        if (width <= 0) return;

        var on = Sort == sort;
        var colour = on ? Theme.Accent : Theme.Faint;
        var font = on ? _active! : _font!;

        var textWidth = TextRenderer.MeasureText(g, text, font,
            new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;

        var arrow = on ? LogicalToDeviceUnits(12) : 0;

        // A right-aligned column keeps its title over its numbers, so the arrow
        // goes on the left of the title rather than pushing it out of line.
        var textX = right ? x + width - textWidth : x;
        var bounds = new Rectangle(textX, 0, textWidth + LogicalToDeviceUnits(2), Height);

        TextRenderer.DrawText(g, text, font, bounds, colour,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
            | TextFormatFlags.NoPadding);

        if (!on) return;

        Arrow(g, right ? textX - arrow : textX + textWidth + LogicalToDeviceUnits(5), colour);
    }

    /// <summary>Which way the column runs. Small: it is a footnote to the title.</summary>
    private void Arrow(Graphics g, int x, Color colour)
    {
        var w = LogicalToDeviceUnits(7);
        var h = LogicalToDeviceUnits(4);
        var cy = Height / 2f;

        var top = Descending ? cy - h / 2f : cy + h / 2f;
        var tip = Descending ? cy + h / 2f : cy - h / 2f;

        using var brush = new SolidBrush(colour);
        g.FillPolygon(brush, new[]
        {
            new PointF(x, top),
            new PointF(x + w, top),
            new PointF(x + w / 2f, tip),
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _font?.Dispose();
            _active?.Dispose();
        }

        base.Dispose(disposing);
    }
}
