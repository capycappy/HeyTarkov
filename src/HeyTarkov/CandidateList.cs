using System.Drawing.Drawing2D;

namespace HeyTarkov;

/// <summary>One row of the candidate list.</summary>
public sealed class CandidateRow(
    TaskMatch match, string? reading, WikiSource? elsewhere = null, bool listed = false, bool chain = false)
{
    public TaskMatch Match { get; } = match;

    /// <summary>
    /// This row is one of a plain listing rather than an answer to something
    /// said or typed. Nothing was matched, so there is no score to show: a
    /// column of "0%" beside every key would read as a failure.
    /// </summary>
    public bool Listed { get; } = listed;

    /// <summary>
    /// One of a task's neighbours along its chain, shown because the previous
    /// or next button was pressed. Asked for by name, so the trader and map
    /// dropdowns do not hide it, and kept in the order of the chain.
    /// </summary>
    public bool Chain { get; } = chain;

    /// <summary>The katakana reading, when the microphone is in Japanese.</summary>
    public string? Reading { get; } = reading;

    /// <summary>
    /// Set when the chosen wiki has no page for this and the other one does.
    /// The row is still worth showing - what the user wanted exists, just not
    /// where they are looking - but it has to say so, and opening it has to go
    /// to the wiki that actually has it.
    /// </summary>
    public WikiSource? Elsewhere { get; } = elsewhere;

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
            EntryKind.Item => Strings.KindItem,
            EntryKind.Key => Strings.KindKey,
            _ => Match.Task.Event.Length > 0 ? Strings.KindEvent(Match.Task.Event) : "",
        };

        var group = Match.Task.Group.Length > 0 ? $" / {Match.Task.Group}" : "";
        var where = Elsewhere is { } other ? $" - {Strings.OnlyOnWiki(other)}" : "";
        var score = Listed ? "" : $" {Match.Score:P0}";
        return $"{kind}{Match.Task.Display}{group}{where}{score}";
    }
}

/// <summary>
/// The candidate list, painted by hand. The stock ListBox can only show one
/// string per row, which forced the name, the trader, the score and the reading
/// into one line of text; here each gets its own column and the kind gets a
/// colour, so the right row is found by shape rather than by reading.
/// </summary>
/// <summary>
/// Where the candidate list's columns sit for a given width. The list and the
/// header above it both ask this, so each title stays over its column.
/// </summary>
public readonly record struct CandidateLayout(
    int NameX, int ReadingX, int GroupX, int GroupW, int ScoreX, int ScoreW)
{
    /// <summary>
    /// The right-hand column is the match score for lines read from a picture,
    /// nothing for keys (which have no chain), and the &lt; &gt; otherwise -
    /// wide enough for two buttons a thumb can find.
    /// </summary>
    public static CandidateLayout For(CandidateList list, int left, int width)
    {
        var control = list;
        var pad = control.LogicalToDeviceUnits(8);
        var right = left + width - pad - control.LogicalToDeviceUnits(4);
        var scoreW = control.LogicalToDeviceUnits(list.ShowScores ? 40 : list.ShowReadings ? 0 : 64);
        var groupW = control.LogicalToDeviceUnits(96);
        var groupX = right - scoreW - control.LogicalToDeviceUnits(scoreW > 0 ? 12 : 0) - groupW;

        return new CandidateLayout(
            left + pad + control.LogicalToDeviceUnits(12),
            left + (int)(width * 0.46f),
            groupX, groupW, right - scoreW, scoreW);
    }
}

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

    /// <summary>
    /// Show a task's maps in the trader column.
    ///
    /// Set while the list is narrowed to one trader, where that column
    /// otherwise repeats the same name down every row and says nothing. Where
    /// the task is done is the next thing worth knowing.
    /// </summary>
    [System.ComponentModel.DefaultValue(false)]
    public bool MapsForTasks { get; set; }

    /// <summary>
    /// Show the match column. Only for lines read from a picture, where a row
    /// can be a misreading and the score is how to tell. Everywhere else that
    /// column holds the &lt; &gt; of the selected task instead.
    /// </summary>
    [System.ComponentModel.DefaultValue(false)]
    public bool ShowScores { get; set; }

    /// <summary>
    /// Show the reading column. Only for keys, where it holds the name the game
    /// prints - which is what a player reads off the screen. For everything
    /// else the reading is a hint, shown when the pointer rests on the row.
    /// </summary>
    [System.ComponentModel.DefaultValue(false)]
    public bool ShowReadings { get; set; }

    /// <summary>
    /// A row's &lt; (false) or &gt; (true) was pressed. The row is selected
    /// first, so the step starts from it.
    /// </summary>
    public event Action<bool>? ChainPicked;

    /// <summary>
    /// The pointer moved to another row (-1 for none), or onto or off one of
    /// its &lt; &gt; (which one, or null).
    /// </summary>
    public event Action<int, bool?>? Hovered;

    private int _hotRow = -1;
    private bool? _hot;
    private (int Row, bool Way)? _pressed;

    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmLButtonDblClk = 0x0203;

    private static bool HasChain(WikiEntry task) =>
        task.Kind == EntryKind.Task && (task.Previous.Count > 0 || task.Next.Count > 0);

    private bool ShowsChain => !ShowScores && !ShowReadings;

    /// <summary>Where the &lt; or &gt; of a row sits.</summary>
    private Rectangle ChainButton(Rectangle row, bool forward)
    {
        var at = CandidateLayout.For(this, row.X, row.Width);
        var w = LogicalToDeviceUnits(28);
        var h = Math.Min(LogicalToDeviceUnits(22), row.Height - LogicalToDeviceUnits(4));
        var gap = LogicalToDeviceUnits(6);
        var right = at.ScoreX + at.ScoreW;
        var x = forward ? right - w : right - w * 2 - gap;
        return new Rectangle(x, row.Y + (row.Height - h) / 2, w, h);
    }

    private static bool Leads(WikiEntry task, bool forward) =>
        (forward ? task.Next : task.Previous).Count > 0;

    /// <summary>The row under a point, and the &lt; or &gt; there if it leads anywhere.</summary>
    private (int Row, bool? Way) HitTest(Point point)
    {
        var index = IndexFromPoint(point);
        if (index < 0 || index >= Items.Count || !GetItemRectangle(index).Contains(point)) return (-1, null);
        if (!ShowsChain || Items[index] is not CandidateRow row || !HasChain(row.Match.Task)) return (index, null);

        var bounds = GetItemRectangle(index);

        foreach (var way in new[] { false, true })
        {
            if (Leads(row.Match.Task, way) && ChainButton(bounds, way).Contains(point)) return (index, way);
        }

        return (index, null);
    }

    /// <summary>
    /// A press on the &lt; or &gt; is taken before the list sees it. Let
    /// through, it would move the selection first, and two quick presses would
    /// arrive as a double click - which opens the page.
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg is WmLButtonDown or WmLButtonDblClk or WmLButtonUp)
        {
            var point = new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16)));

            if (m.Msg == WmLButtonUp)
            {
                if (_pressed is { } pressed)
                {
                    _pressed = null;

                    if (HitTest(point) is var hit && hit.Row == pressed.Row && hit.Way == pressed.Way)
                    {
                        SelectedIndex = pressed.Row;
                        ChainPicked?.Invoke(pressed.Way);
                    }

                    return;
                }
            }
            else if (HitTest(point) is { Way: { } way } hit)
            {
                _pressed = (hit.Row, way);
                return;
            }
        }

        base.WndProc(ref m);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Hover(HitTest(e.Location));
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        Hover((-1, null));
    }

    private void Hover((int Row, bool? Way) hit)
    {
        if (_hotRow == hit.Row && _hot == hit.Way) return;

        var was = _hotRow;
        var wayChanged = _hot != hit.Way;

        _hotRow = hit.Row;
        _hot = hit.Way;
        Cursor = hit.Way is null ? Cursors.Default : Cursors.Hand;

        if (wayChanged)
        {
            if (was >= 0 && was < Items.Count) Invalidate(GetItemRectangle(was));
            if (hit.Row >= 0 && hit.Row < Items.Count) Invalidate(GetItemRectangle(hit.Row));
        }

        Hovered?.Invoke(hit.Row, hit.Way);
    }

    /// <summary>
    /// What the trader column shows for a row. A map's row says the map's own
    /// name there: the column is what that column sorts by, and a blank cell
    /// cannot be sorted among the others.
    /// </summary>
    public string GroupText(WikiEntry task)
    {
        if (task.Kind == EntryKind.Map) return task.Name;
        if (!MapsForTasks || task.Kind != EntryKind.Task) return task.Group;

        return task.Maps.Count switch
        {
            0 => "",
            1 => task.Maps[0],
            _ => $"{task.Maps[0]} +{task.Maps.Count - 1}",
        };
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
        var at = CandidateLayout.For(this, full.X, full.Width);
        var right = at.ScoreX + at.ScoreW;
        var scoreW = at.ScoreW;
        var groupW = at.GroupW;
        var groupX = at.GroupX;

        var chain = ShowsChain && HasChain(row.Match.Task);

        if (chain)
        {
            foreach (var way in new[] { false, true })
            {
                ChainButtonFace(g, ChainButton(full, way), way, Leads(row.Match.Task, way),
                    hot: _hotRow == e.Index && _hot == way, selected);
            }
        }

        if (row.Elsewhere is { } other)
        {
            // Where it can be found, in place of a score and a trader - neither
            // of which answers the question this row raises.
            Column(g, Strings.OnlyOnWiki(other), _small!, Theme.Clip,
                groupX, groupW + (chain ? 0 : scoreW + LogicalToDeviceUnits(8)), full);
        }
        else
        {
            if (ShowScores && !row.Listed)
            {
                var scoreColour = row.Match.Score >= 0.995 ? Theme.Accent : Theme.Faint;
                Column(g, $"{row.Match.Score:P0}", _small!, scoreColour, right - scoreW, scoreW, full);
            }

            if (GroupText(row.Match.Task) is { Length: > 0 } group)
                Column(g, group, _small!, Theme.Muted, groupX, groupW, full);
        }

        var readingX = at.ReadingX;
        var readingW = groupX - LogicalToDeviceUnits(10) - readingX;
        var reading = ShowReadings && row.Reading is not null && readingW > LogicalToDeviceUnits(40);

        if (reading)
            LeftText(g, row.Reading!, _small!, Theme.Faint, readingX, readingW, full);

        var nameW = (reading ? readingX : groupX) - LogicalToDeviceUnits(10) - x;

        if (nameW > 0)
            LeftText(g, row.Match.Task.Display, selected ? _nameSelected! : _name!, Theme.Text, x, nameW, full);
    }

    /// <summary>The kind, as a pill. Returns its width so the name can follow it.</summary>
    /// <summary>Every word a badge can say. The widest of them sets the width
    /// of all of them.</summary>
    private static readonly string[] BadgeWords = { "TASK", "MAP", "EXIT", "ITEM", "KEY", "EVENT", "STORY" };

    /// <summary>
    /// One width for every badge, so the names after them start in one
    /// column. Sized to its word, "EVENT" pushed its name further right than
    /// "TASK" did, and a list read down its names zig-zagged.
    /// </summary>
    private int BadgeWidth(Graphics g)
    {
        var padX = LogicalToDeviceUnits(6);
        var widest = BadgeWords.Max(word => TextRenderer.MeasureText(g, word, _badge!,
            new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width);

        return widest + padX * 2;
    }

    private int Badge(Graphics g, string text, Color colour, int x, Rectangle row)
    {
        var w = BadgeWidth(g);
        var h = _badge!.Height + LogicalToDeviceUnits(3);
        var box = new RectangleF(x, row.Y + (row.Height - h) / 2f, w, h);

        Painting.FillRounded(g, box, Color.FromArgb(34, colour), LogicalToDeviceUnits(3));
        TextRenderer.DrawText(g, text, _badge, Rectangle.Round(box), colour,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        return w;
    }

    /// <summary>
    /// A &lt; or &gt;, as a small outlined button - the same rounded outline as
    /// the buttons above the list, so the row reads as part of the same window.
    /// Where there is nothing that way only a faint mark is left, so the pair
    /// keeps its place and the one that works is not mistaken for the other.
    /// </summary>
    private void ChainButtonFace(Graphics g, Rectangle box, bool forward, bool enabled, bool hot, bool selected)
    {
        var radius = LogicalToDeviceUnits(6);

        if (enabled)
        {
            if (hot)
            {
                Painting.FillRounded(g, box, Color.FromArgb(40, Theme.Accent), radius);
                Painting.DrawRounded(g, box, Theme.Accent, radius);
            }
            else
            {
                Painting.FillRounded(g, box, selected ? Theme.Panel : Theme.PanelHi, radius);
                Painting.DrawRounded(g, box, selected ? Theme.Field : Theme.Edge, radius);
            }
        }

        var colour = !enabled ? Color.FromArgb(50, Theme.Faint) : hot ? Theme.Accent : Theme.Muted;

        using var pen = new Pen(colour, LogicalToDeviceUnits(2) * 0.8f);
        pen.StartCap = LineCap.Round;
        pen.EndCap = LineCap.Round;
        pen.LineJoin = LineJoin.Round;

        var cx = box.X + box.Width / 2f;
        var cy = box.Y + box.Height / 2f;
        var w = box.Height * 0.13f;
        var h = box.Height * 0.24f;
        var way = forward ? 1 : -1;

        g.DrawLines(pen, new[]
        {
            new PointF(cx - way * w, cy - h),
            new PointF(cx + way * w, cy),
            new PointF(cx - way * w, cy + h),
        });
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
