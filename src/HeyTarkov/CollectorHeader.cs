using System.Drawing.Drawing2D;

namespace HeyTarkov;

/// <summary>
/// The two column titles above the checklist, each one a way to reorder it.
///
/// A header is the one place people already look for sorting, so it is worth
/// the row it takes - and it names the columns, which were until now two
/// unexplained strings side by side.
/// </summary>
public sealed class CollectorHeader : Control
{
    private Font? _font;
    private Font? _active;

    /// <summary>Raised when a column title is clicked.</summary>
    public event Action<CollectorSort>? Picked;

    public CollectorHeader()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

        Cursor = Cursors.Hand;
    }

    public CollectorSort Sort { get; private set; } = CollectorSort.Label;

    public bool Descending { get; private set; }

    public void Show(CollectorSort sort, bool descending)
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

        Height = _font.Height + LogicalToDeviceUnits(12);
    }

    /// <summary>Whether the Japanese column is there to be clicked.</summary>
    [System.ComponentModel.DefaultValue(false)]
    public bool ShowJapanese { get; set; }

    private CollectorLayout At => CollectorLayout.For(this, Width, ShowJapanese);

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != MouseButtons.Left) return;

        var at = At;

        var picked = e.X < at.NameX ? CollectorSort.Label
            : ShowJapanese && e.X >= at.JapaneseX ? CollectorSort.Japanese
            : CollectorSort.Name;

        Picked?.Invoke(picked);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_font is null) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Panel);

        var at = At;

        Column(g, Strings.CollectorColumnLabel, at.LabelX, at.LabelW, CollectorSort.Label);
        Column(g, Strings.CollectorColumnName, at.NameX, at.NameW, CollectorSort.Name);

        if (ShowJapanese)
        {
            Column(g, Strings.CollectorColumnJapanese, at.JapaneseX, at.JapaneseW,
                CollectorSort.Japanese);
        }

        // A hairline under the row, so the titles read as a header rather than
        // as a first item that cannot be ticked.
        using var pen = new Pen(Theme.Edge);
        var y = Height - 1;
        g.DrawLine(pen, LogicalToDeviceUnits(CollectorColumns.Pad), y,
            Width - LogicalToDeviceUnits(CollectorColumns.Pad), y);
    }

    private void Column(Graphics g, string text, int x, int width, CollectorSort sort)
    {
        if (width <= 0) return;

        var on = Sort == sort;
        var colour = on ? Theme.Accent : Theme.Faint;
        var font = on ? _active! : _font!;

        TextRenderer.DrawText(g, text, font, new Rectangle(x, 0, width, Height), colour,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
            | TextFormatFlags.NoPrefix);

        if (!on) return;

        var textWidth = TextRenderer.MeasureText(g, text, font,
            new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;

        Arrow(g, x + textWidth + LogicalToDeviceUnits(6), colour);
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
