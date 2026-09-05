using System.Drawing.Drawing2D;

namespace HeyTarkov;

/// <summary>One row of the candidate list.</summary>
public sealed class CandidateRow(TaskMatch match, string? reading)
{
    public TaskMatch Match { get; } = match;

    /// <summary>The katakana reading, when the microphone is in Japanese.</summary>
    public string? Reading { get; } = reading;

    /// <summary>
    /// What a screen reader announces. The list is painted, so this is the only
    /// place the row exists as text.
    /// </summary>
    public override string ToString()
    {
        var kind = Match.Task.Kind switch
        {
            EntryKind.Map => Strings.KindMap,
            EntryKind.Extract => Strings.KindExit,
            _ => Match.Task.Event.Length > 0 ? Strings.KindEvent(Match.Task.Event) : "",
        };

        var group = Match.Task.Group.Length > 0 ? $" / {Match.Task.Group}" : "";
        return $"{kind}{Match.Task.Display}{group} {Match.Score:P0}";
    }
}

/// <summary>
/// The candidate list, painted by hand. The stock ListBox can only show one
/// string per row, which forced the name, the trader, the score and the reading
/// into one line of text; here each gets its own column and the kind gets a
/// colour, so the right row is found by shape rather than by reading.
/// </summary>
public sealed class CandidateList : ListBox
{
    private Font? _name;
    private Font? _nameSelected;
    private Font? _small;
    private Font? _badge;

    public CandidateList()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        BorderStyle = BorderStyle.None;
        IntegralHeight = false;
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
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

    /// <summary>
    /// Names are English, readings are katakana: one family cannot set both
    /// well, so each column gets the face that suits it.
    /// </summary>
    private void RebuildFonts()
    {
        _name?.Dispose();
        _nameSelected?.Dispose();
        _small?.Dispose();
        _badge?.Dispose();

        var size = Font.SizeInPoints;
        _name = new Font("Segoe UI", size);
        _nameSelected = new Font("Segoe UI Semibold", size);
        _small = new Font("Yu Gothic UI", size * 0.88f);
        _badge = new Font("Segoe UI Semibold", size * 0.74f, FontStyle.Bold);

        ItemHeight = Font.Height + LogicalToDeviceUnits(9);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count || _name is null) return;
        if (Items[e.Index] is not CandidateRow row) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var full = e.Bounds;
        var selected = (e.State & DrawItemState.Selected) != 0;

        using (var brush = new SolidBrush(Theme.Panel)) g.FillRectangle(brush, full);

        var pad = LogicalToDeviceUnits(8);
        var gap = LogicalToDeviceUnits(10);

        if (selected)
        {
            var lift = new RectangleF(full.X + pad, full.Y, full.Width - pad * 2, full.Height);
            Painting.FillRounded(g, lift, Theme.PanelHi, LogicalToDeviceUnits(6));

            var barH = full.Height - LogicalToDeviceUnits(10);
            Painting.FillRounded(g,
                new RectangleF(full.X + pad, full.Y + (full.Height - barH) / 2f, LogicalToDeviceUnits(3), barH),
                Theme.Accent, LogicalToDeviceUnits(2));
        }

        var kind = Theme.Of(row.Match.Task);
        var x = full.X + pad + LogicalToDeviceUnits(12);

        x += Badge(g, Theme.BadgeOf(row.Match.Task), kind, x, full) + gap;

        // Right-hand columns are placed first; the name takes whatever is left.
        var right = full.Right - pad - LogicalToDeviceUnits(4);
        var scoreW = LogicalToDeviceUnits(40);
        var groupW = LogicalToDeviceUnits(96);

        var scoreColour = row.Match.Score >= 0.995 ? Theme.Accent : Theme.Faint;
        Column(g, $"{row.Match.Score:P0}", _small!, scoreColour, right - scoreW, scoreW, full);

        var groupX = right - scoreW - LogicalToDeviceUnits(8) - groupW;
        if (row.Match.Task.Group.Length > 0)
            Column(g, row.Match.Task.Group, _small!, Theme.Muted, groupX, groupW, full);

        var readingX = full.X + (int)(full.Width * 0.46f);
        var readingW = groupX - LogicalToDeviceUnits(10) - readingX;

        if (row.Reading is not null && readingW > LogicalToDeviceUnits(40))
            LeftText(g, row.Reading, _small!, Theme.Faint, readingX, readingW, full);

        var nameW = (row.Reading is not null && readingW > LogicalToDeviceUnits(40) ? readingX : groupX)
                    - LogicalToDeviceUnits(10) - x;

        if (nameW > 0)
            LeftText(g, row.Match.Task.Display, selected ? _nameSelected! : _name!, Theme.Text, x, nameW, full);
    }

    /// <summary>The kind, as a pill. Returns its width so the name can follow it.</summary>
    private int Badge(Graphics g, string text, Color colour, int x, Rectangle row)
    {
        var padX = LogicalToDeviceUnits(6);
        var size = TextRenderer.MeasureText(g, text, _badge!, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding);

        var w = size.Width + padX * 2;
        var h = _badge!.Height + LogicalToDeviceUnits(3);
        var box = new RectangleF(x, row.Y + (row.Height - h) / 2f, w, h);

        Painting.FillRounded(g, box, Color.FromArgb(34, colour), LogicalToDeviceUnits(3));
        TextRenderer.DrawText(g, text, _badge, Rectangle.Round(box), colour,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        return w;
    }

    private static void LeftText(Graphics g, string text, Font font, Color colour, int x, int w, Rectangle row) =>
        TextRenderer.DrawText(g, text, font, new Rectangle(x, row.Y, w, row.Height), colour,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
            | TextFormatFlags.NoPrefix);

    private static void Column(Graphics g, string text, Font font, Color colour, int x, int w, Rectangle row) =>
        TextRenderer.DrawText(g, text, font, new Rectangle(x, row.Y, w, row.Height), colour,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
            | TextFormatFlags.NoPrefix);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _name?.Dispose();
            _nameSelected?.Dispose();
            _small?.Dispose();
            _badge?.Dispose();
        }

        base.Dispose(disposing);
    }
}
