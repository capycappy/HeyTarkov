namespace HeyTarkov;

/// <summary>
/// The Collector checklist, inside the main window.
///
/// It was a window of its own until the person using it said what a second
/// window feels like: two things on the taskbar for one app. So the checklist
/// takes over the candidate list instead - the same panel, the same search box,
/// the same row of controls above it - and the KAPPA button switches between
/// them the way the key button switches between tasks and keys.
///
/// This holds only the list and its rules. Everything around it - the count,
/// the hint line, the filter box - belongs to the window and is fed from here,
/// so the checklist reads as part of the screen rather than as a guest on it.
/// </summary>
public sealed class CollectorPanel : Panel
{
    private readonly IReadOnlyList<WikiEntry> _items;
    private readonly CollectorRecord _record;
    private readonly WikiSource _wiki;
    private readonly BrowserChoice _browser;

    private readonly CollectorHeader _header = new();
    private readonly CollectorList _list = new();

    private CollectorSort _sort = CollectorSort.Label;
    private bool _descending;
    private bool _remainingOnly;
    private string _filter = "";

    /// <summary>
    /// What has been ticked since the checklist was opened, so that "still
    /// needed only" does not make a row vanish the instant it is ticked.
    ///
    /// A box is easy to hit by accident, and a row that disappears takes the
    /// chance to undo it with it. They stay, struck through, until the
    /// checklist is left; coming back to it starts the list clean.
    /// </summary>
    private readonly HashSet<string> _tickedHere = new(StringComparer.Ordinal);

    /// <summary>Raised when a tick changes, so the button that opens this can
    /// keep its own count right.</summary>
    public event Action? Changed;

    /// <summary>Raised whenever the list is rebuilt, so the window can copy the
    /// count and the hint into its own labels.</summary>
    public event Action? Updated;

    public CollectorPanel(
        IReadOnlyList<WikiEntry> items, CollectorRecord record,
        WikiSource wiki, BrowserChoice browser)
    {
        _items = items;
        _record = record;
        _wiki = wiki;
        _browser = browser;

        BackColor = Color.Transparent;

        _list.Dock = DockStyle.Fill;
        _list.BackColor = Theme.Panel;
        _list.ForeColor = Theme.Text;
        _list.ShowJapanese = wiki == WikiSource.Japanese;
        _list.Toggled += OnToggled;
        _list.Opened += OnOpened;

        _header.Dock = DockStyle.Top;
        _header.BackColor = Theme.Panel;
        _header.ShowJapanese = wiki == WikiSource.Japanese;
        _header.Picked += OnSortPicked;

        // The list goes in first: WinForms docks in reverse, so the header
        // added after it is the one that takes the top edge.
        Controls.Add(_list);
        Controls.Add(_header);

        Populate();
    }

    /// <summary>What the window's count label should read.</summary>
    public string Progress =>
        Held == _items.Count && _items.Count > 0
            ? Strings.CollectorDone
            : Strings.CollectorProgress(Held, _items.Count);

    /// <summary>Everything is held, which is worth colouring differently.</summary>
    public bool Complete => Held == _items.Count && _items.Count > 0;

    /// <summary>What the window's hint line should read.</summary>
    public string Hint =>
        _record.Unreadable ? Strings.CollectorUnreadable
        : _items.Count == 0 ? Strings.CollectorNoItems
        : _list.Items.Count == 0 ? Strings.CollectorEmpty
        : Strings.CollectorHint;

    /// <summary>The record could not be read, so there is nothing to wipe.</summary>
    public bool CanClear => !_record.Unreadable;

    public bool Unreadable => _record.Unreadable;

    [System.ComponentModel.DefaultValue(false)]
    public bool RemainingOnly
    {
        get => _remainingOnly;
        set
        {
            if (_remainingOnly == value) return;

            _remainingOnly = value;
            Populate();
        }
    }

    /// <summary>What is typed in the window's search box.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Filter
    {
        set
        {
            var text = value.Trim();
            if (_filter == text) return;

            _filter = text;
            Populate();
        }
    }

    /// <summary>
    /// Leaving the checklist and coming back starts it clean: the rows kept
    /// visible after being ticked are a safety net for the moment they were
    /// ticked, not a state worth carrying around.
    /// </summary>
    public void Forget()
    {
        if (_tickedHere.Count == 0) return;

        _tickedHere.Clear();
        Populate();
    }

    /// <summary>
    /// Asked about first. It throws away the only thing here that cannot be
    /// worked out again from the wikis.
    /// </summary>
    public void ClearAll()
    {
        if (Held == 0) return;

        var answer = MessageBox.Show(FindForm(), Strings.CollectorClearAsk(Held),
            Strings.CollectorClearTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (answer != DialogResult.OK) return;

        _record.Clear();
        _tickedHere.Clear();
        Changed?.Invoke();
        Populate();
    }

    private int Held => _items.Count(item => _record.Has(item.Name));

    /// <summary>
    /// Clicking the column already sorted turns it around; clicking another one
    /// starts it the way round that reads naturally - these columns are all
    /// text, so that is A to Z.
    /// </summary>
    private void OnSortPicked(CollectorSort sort)
    {
        if (sort == _sort) _descending = !_descending;
        else (_sort, _descending) = (sort, false);

        _header.Show(_sort, _descending);
        Populate();
    }

    /// <summary>
    /// Rebuilt from the record every time rather than mutated in place, so the
    /// filter and the list cannot drift apart.
    /// </summary>
    private void Populate()
    {
        var shown = Ordered(_items
                .Where(item => !_remainingOnly
                               || !_record.Has(item.Name)
                               || _tickedHere.Contains(item.Name))
                .Where(item => Matches(item, _filter)))
            .Select(item => new CollectorRow(item, _record.Has(item.Name)))
            .ToArray();

        _list.BeginUpdate();
        _list.Items.Clear();
        _list.Items.AddRange(shown);
        _list.EndUpdate();

        Updated?.Invoke();
    }

    /// <summary>
    /// Items with no label sort last rather than first, whichever way the
    /// column runs: an empty cell at the top is a poor first impression of a
    /// list whose point is the labels.
    /// </summary>
    private IEnumerable<WikiEntry> Ordered(IEnumerable<WikiEntry> items)
    {
        if (_sort == CollectorSort.Name)
        {
            return _descending
                ? items.OrderByDescending(e => e.Name, StringComparer.OrdinalIgnoreCase)
                : items.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase);
        }

        var (missing, key) = _sort == CollectorSort.Japanese
            ? ((Func<WikiEntry, bool>)(e => e.JapaneseName is null),
                (Func<WikiEntry, string>)(e => e.JapaneseName ?? e.Name))
            : (e => e.ShortName is null, e => e.ShortName ?? e.Name);

        var ordered = items.OrderBy(missing);

        return _descending
            ? ordered.ThenByDescending(key, StringComparer.OrdinalIgnoreCase)
            : ordered.ThenBy(key, StringComparer.OrdinalIgnoreCase);
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
        _tickedHere.Add(row.Item.Name);

        Changed?.Invoke();

        // The row stays where it is, struck through, however the filter is set:
        // it is the only way back if the box was hit by mistake.
        Updated?.Invoke();
        _list.Invalidate();
    }

    private void OnOpened(CollectorRow row)
    {
        var url = row.Item.Url(_wiki) ?? row.Item.Url(Other(_wiki));
        if (url is not null) BrowserLauncher.Open(url, _browser);
    }

    private static WikiSource Other(WikiSource source) =>
        source == WikiSource.Japanese ? WikiSource.English : WikiSource.Japanese;
}
