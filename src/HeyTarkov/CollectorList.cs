using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace HeyTarkov;

/// <summary>
/// Where the columns start, in 96 DPI units. Shared so the header and the rows
/// cannot drift apart - they are two controls drawing what reads as one table.
/// </summary>
internal static class CollectorColumns
{
    /// <summary>How far in the tick box reaches, and where the label starts.</summary>
    public const int Box = 34;

    /// <summary>Width of the short-label column. "BEAR Buddy" is the longest.</summary>
    public const int Label = 96;

    public const int Pad = 8;

    public const int Gap = 10;
}

/// <summary>Which column the list is ordered by.</summary>
public enum CollectorSort
{
    /// <summary>By the stash label - the order the eye scans in.</summary>
    Label,

    /// <summary>By the full name, which is how both wikis list them.</summary>
    Name,

    /// <summary>By the Japanese name, where the wiki gives one.</summary>
    Japanese,
}

/// <summary>
/// Where each column starts and how wide it is, worked out once from the width
/// on offer. The header and the rows are separate controls drawing what has to
/// read as one table, so they ask the same function rather than each doing
/// their own arithmetic.
/// </summary>
public readonly record struct CollectorLayout(int LabelX, int LabelW, int NameX, int NameW,
                                              int JapaneseX, int JapaneseW)
{
    public static CollectorLayout For(Control control, int width, bool japanese)
    {
        var pad = control.LogicalToDeviceUnits(CollectorColumns.Pad);
        var gap = control.LogicalToDeviceUnits(CollectorColumns.Gap);

        var labelX = control.LogicalToDeviceUnits(CollectorColumns.Box);
        var labelW = control.LogicalToDeviceUnits(CollectorColumns.Label);

        var nameX = labelX + labelW + gap;
        var rest = width - pad - control.LogicalToDeviceUnits(6) - nameX;

        if (!japanese) return new CollectorLayout(labelX, labelW, nameX, rest, 0, 0);

        // The English name is the one that has to survive being read, so it
        // keeps the larger share when the window is narrow.
        var nameW = Math.Max(control.LogicalToDeviceUnits(90), (int)(rest * 0.55f) - gap);

        return new CollectorLayout(labelX, labelW, nameX, nameW,
            nameX + nameW + gap, rest - nameW - gap);
    }
}

/// <summary>One item in the checklist, and whether it is already in the stash.</summary>
public sealed class CollectorRow(WikiEntry item, bool held)
{
    public WikiEntry Item { get; } = item;

    public bool Held { get; set; } = held;

    /// <summary>What a screen reader announces; the list itself is painted.</summary>
    public override string ToString()
    {
        var label = Item.ShortName is null ? "" : $"{Item.ShortName} - ";
        return $"{(Held ? "[x]" : "[ ]")} {label}{Item.Name}";
    }
}

/// <summary>
/// The Collector checklist.
///
/// The stash label leads each row, in its own column, because that is the
/// string a person is matching against the game - they are looking at a
/// container full of "BeardOil" and "Plague mask", not at
/// "Deadlyslob's beard oil". The full name follows for the ones the label does
/// not settle.
/// </summary>
public sealed class CollectorList : ListBox
{
    private Font? _label;
    private Font? _labelHeld;
    private Font? _name;
    private Font? _held;

    /// <summary>Raised when a row's box is ticked or cleared.</summary>
    public event Action<CollectorRow>? Toggled;

    /// <summary>Raised when a row is opened - double click, or Enter.</summary>
    public event Action<CollectorRow>? Opened;

    public CollectorList()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        BorderStyle = BorderStyle.None;
        IntegralHeight = false;
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
    }

    /// <summary>Whether the Japanese name is worth the width.</summary>
    [DefaultValue(false)]
    public bool ShowJapanese { get; set; }

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
        _label?.Dispose();
        _labelHeld?.Dispose();
        _name?.Dispose();
        _held?.Dispose();

        var size = Font.SizeInPoints;
        _label = new Font("Segoe UI Semibold", size);
        _labelHeld = new Font("Segoe UI Semibold", size, FontStyle.Strikeout);
        _name = new Font("Yu Gothic UI", size * 0.9f);
        _held = new Font("Yu Gothic UI", size * 0.9f, FontStyle.Strikeout);

        ItemHeight = Font.Height + LogicalToDeviceUnits(11);
    }

    /// <summary>
    /// Only the box ticks the row, and the column it sits in is twice its width
    /// so it is not a fifteen-pixel target.
    ///
    /// Ticking anywhere was tried and taken back out. Opening the wiki is a
    /// double click, and two clicks on a row that ticks is two ticks - which
    /// with "still needed only" on meant: the row is ticked, it leaves the
    /// list, the next row slides up under the cursor, and the second click
    /// lands on that one instead. A double click could walk down the list
    /// ticking things nobody meant to tick.
    /// </summary>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButtons.Left || e.X > BoxColumn) return;

        var index = IndexFromPoint(e.Location);
        if (index >= 0 && index < Items.Count) Toggle(index);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);

        if (e.X <= BoxColumn) return;   // that was two clicks on the box

        var index = IndexFromPoint(e.Location);
        if (index >= 0 && Items[index] is CollectorRow row) Opened?.Invoke(row);
    }

    /// <summary>How far in the box column reaches.</summary>
    private int BoxColumn => LogicalToDeviceUnits(CollectorColumns.Box);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space && SelectedIndex >= 0)
        {
            Toggle(SelectedIndex);
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Enter && SelectedItem is CollectorRow row)
        {
            Opened?.Invoke(row);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void Toggle(int index)
    {
        if (Items[index] is not CollectorRow row) return;

        row.Held = !row.Held;
        SelectedIndex = index;
        Invalidate(GetItemRectangle(index));

        Toggled?.Invoke(row);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count || _label is null) return;
        if (Items[e.Index] is not CollectorRow row) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var full = e.Bounds;
        var selected = (e.State & DrawItemState.Selected) != 0;

        using (var brush = new SolidBrush(Theme.Panel)) g.FillRectangle(brush, full);

        var pad = LogicalToDeviceUnits(CollectorColumns.Pad);

        if (selected)
        {
            var lift = new RectangleF(full.X + pad, full.Y, full.Width - pad * 2, full.Height);
            Painting.FillRounded(g, lift, Theme.PanelHi, LogicalToDeviceUnits(6));
        }

        Box(g, full, row.Held, pad + LogicalToDeviceUnits(6));

        // A held item is not removed from the list - seeing the whole set is
        // the point - but it should stop competing for attention, and the line
        // through it belongs on every column or the row reads as half done.
        var strong = row.Held ? Theme.Faint : Theme.Text;
        var quiet = row.Held ? Theme.Faint : Theme.Muted;
        var label = row.Held ? _labelHeld! : _label!;
        var name = row.Held ? _held! : _name!;

        var at = CollectorLayout.For(this, full.Width, ShowJapanese);

        if (row.Item.ShortName is { } stash)
        {
            Draw(g, stash, label, strong, full.X + at.LabelX, at.LabelW, full);
        }
        else
        {
            // Nothing to line up with, so say so rather than leaving a hole
            // that reads as "this item has no label in the game".
            Draw(g, "—", _name!, Theme.Faint, full.X + at.LabelX, at.LabelW, full);
        }

        if (at.NameW > 0)
            Draw(g, row.Item.Name, name, quiet, full.X + at.NameX, at.NameW, full);

        if (ShowJapanese && at.JapaneseW > 0 && row.Item.JapaneseName is { } japanese)
            Draw(g, japanese, name, quiet, full.X + at.JapaneseX, at.JapaneseW, full);
    }

    private void Box(Graphics g, Rectangle row, bool held, int x)
    {
        var side = LogicalToDeviceUnits(15);
        var box = new RectangleF(row.X + x, row.Y + (row.Height - side) / 2f, side, side);

        // Barely rounded. At four it reads as a radio button, which promises
        // that ticking one clears the others.
        var radius = LogicalToDeviceUnits(2);

        if (held)
        {
            Painting.FillRounded(g, box, Theme.Accent, radius);
            Painting.Tick(g, box, Theme.AccentText, Math.Max(1.6f, side * 0.14f));
        }
        else
        {
            Painting.DrawRounded(g, box, Theme.Field, radius, Math.Max(1f, LogicalToDeviceUnits(1) * 1.2f));
        }
    }

    private static void Draw(Graphics g, string text, Font font, Color colour, int x, int w, Rectangle row) =>
        TextRenderer.DrawText(g, text, font, new Rectangle(x, row.Y, w, row.Height), colour,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
            | TextFormatFlags.NoPrefix);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _label?.Dispose();
            _labelHeld?.Dispose();
            _name?.Dispose();
            _held?.Dispose();
        }

        base.Dispose(disposing);
    }
}
