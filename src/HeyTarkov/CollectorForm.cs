namespace HeyTarkov;

/// <summary>
/// The Collector checklist, in a window of its own.
///
/// The main window is a remote control - one button, pressed while speaking,
/// deliberately small. Forty-four rows with boxes to tick is a different kind
/// of screen and wants its own room, and keeping it separate means the main
/// window is untouched but for the one button that opens this.
/// </summary>
public sealed class CollectorForm : Form
{
    private readonly IReadOnlyList<WikiEntry> _items;
    private readonly CollectorRecord _record;
    private readonly WikiSource _wiki;
    private readonly BrowserChoice _browser;

    private readonly Label _progress = new();
    private readonly TextBox _filter = new();
    private readonly CheckBox _remaining = new();
    private readonly CollectorList _list = new();
    private readonly Label _hint = new();

    public CollectorForm(
        IReadOnlyList<WikiEntry> items, CollectorRecord record,
        WikiSource wiki, BrowserChoice browser)
    {
        _items = items;
        _record = record;
        _wiki = wiki;
        _browser = browser;

        Text = Strings.CollectorTitle;
        Font = new Font("Yu Gothic UI", 9.75f);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(460, 420);
        Size = new Size(560, 680);
        BackColor = Theme.Page;
        ShowIcon = false;
        ShowInTaskbar = false;
        KeyPreview = true;

        BuildLayout();
        Populate();
    }

    private void BuildLayout()
    {
        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14),
            BackColor = Color.Transparent,
        };

        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        page.Controls.Add(BuildHeader(), 0, 0);
        page.Controls.Add(BuildFilterRow(), 0, 1);
        page.Controls.Add(BuildListCard(), 0, 2);
        page.Controls.Add(BuildHint(), 0, 3);

        Controls.Add(page);
    }

    private Control BuildHeader()
    {
        _progress.AutoSize = true;
        _progress.BackColor = Color.Transparent;
        _progress.ForeColor = Theme.Text;
        _progress.Font = new Font("Yu Gothic UI", 15f, FontStyle.Bold);
        _progress.Margin = new Padding(2, 0, 0, 10);
        return _progress;
    }

    private Control BuildFilterRow()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 10),
            BackColor = Color.Transparent,
        };

        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var card = new SearchCard
        {
            Dock = DockStyle.Fill,
            Height = 34,
            Margin = new Padding(0, 0, 12, 0),
            Outlined = false,
        };

        _filter.BorderStyle = BorderStyle.None;
        _filter.BackColor = Theme.Panel;
        _filter.ForeColor = Theme.Text;
        _filter.Font = new Font("Yu Gothic UI", 10.5f);
        _filter.PlaceholderText = Strings.CollectorFilter;
        _filter.Dock = DockStyle.Fill;
        _filter.Margin = new Padding(30, 8, 12, 8);
        _filter.TextChanged += (_, _) => Populate();

        var holder = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        holder.Padding = new Padding(30, 8, 12, 6);
        holder.Controls.Add(_filter);
        card.Controls.Add(holder);

        // Sized rather than auto-sized: the label is the longest string in the
        // window and AutoSize measured it before the form had scaled, which cut
        // the last character off on a high-DPI monitor.
        _remaining.AutoSize = false;
        _remaining.Size = new Size(132, 24);
        _remaining.Text = Strings.CollectorRemainingOnly;
        _remaining.ForeColor = Theme.Muted;
        _remaining.BackColor = Color.Transparent;
        _remaining.Anchor = AnchorStyles.Left;
        _remaining.Margin = new Padding(0, 8, 2, 0);
        _remaining.CheckedChanged += (_, _) => Populate();

        row.Controls.Add(card, 0, 0);
        row.Controls.Add(_remaining, 1, 0);
        return row;
    }

    private Control BuildListCard()
    {
        var card = new Card { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };

        _list.Dock = DockStyle.Fill;
        _list.BackColor = Theme.Panel;
        _list.ForeColor = Theme.Text;
        _list.ShowJapanese = _wiki == WikiSource.Japanese;
        _list.Toggled += OnToggled;
        _list.Opened += OnOpened;

        var holder = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(2, 8, 2, 8),
            BackColor = Color.Transparent,
        };

        holder.Controls.Add(_list);
        card.Controls.Add(holder);
        return card;
    }

    private Control BuildHint()
    {
        _hint.AutoSize = true;
        _hint.BackColor = Color.Transparent;
        _hint.ForeColor = Theme.Faint;
        _hint.Font = new Font("Yu Gothic UI", 8.25f);
        _hint.Text = Strings.CollectorHint;
        _hint.Margin = new Padding(2, 0, 0, 0);
        return _hint;
    }

    /// <summary>
    /// Rebuilt from the record every time rather than mutated in place, so the
    /// filter and the list cannot drift apart.
    /// </summary>
    private void Populate()
    {
        var needle = _filter.Text.Trim();

        var shown = _items
            .Where(item => !_remaining.Checked || !_record.Has(item.Name))
            .Where(item => Matches(item, needle))
            .Select(item => new CollectorRow(item, _record.Has(item.Name)))
            .ToArray();

        _list.BeginUpdate();
        _list.Items.Clear();
        _list.Items.AddRange(shown);
        _list.EndUpdate();

        var held = _items.Count(item => _record.Has(item.Name));

        _progress.Text = held == _items.Count && _items.Count > 0
            ? Strings.CollectorDone
            : Strings.CollectorProgress(held, _items.Count);

        _progress.ForeColor = held == _items.Count && _items.Count > 0 ? Theme.Accent : Theme.Text;

        _hint.Text = _items.Count == 0 ? Strings.CollectorNoItems
            : shown.Length == 0 ? Strings.CollectorEmpty
            : Strings.CollectorHint;
    }

    /// <summary>
    /// Typed text is matched against everything the row shows - the stash
    /// label, the English name, the Japanese one - because which of them the
    /// user has in mind is not knowable from here.
    /// </summary>
    private static bool Matches(WikiEntry item, string needle)
    {
        if (needle.Length == 0) return true;

        return Contains(item.ShortName, needle)
               || Contains(item.Name, needle)
               || Contains(item.JapaneseName, needle);
    }

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null
        && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private void OnToggled(CollectorRow row)
    {
        // Written the moment it is ticked. A checklist with a save button is a
        // checklist that loses an evening's raids to a closed window.
        _record.Set(row.Item.Name, row.Held);

        // "Still needed only" is on: the row just ticked no longer belongs.
        if (_remaining.Checked) Populate();
        else RefreshProgress();
    }

    private void RefreshProgress()
    {
        var held = _items.Count(item => _record.Has(item.Name));

        _progress.Text = held == _items.Count && _items.Count > 0
            ? Strings.CollectorDone
            : Strings.CollectorProgress(held, _items.Count);

        _progress.ForeColor = held == _items.Count && _items.Count > 0 ? Theme.Accent : Theme.Text;
    }

    private void OnOpened(CollectorRow row)
    {
        var url = row.Item.Url(_wiki) ?? row.Item.Url(Other(_wiki));
        if (url is not null) BrowserLauncher.Open(url, _browser);
    }

    private static WikiSource Other(WikiSource source) =>
        source == WikiSource.Japanese ? WikiSource.English : WikiSource.Japanese;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}
