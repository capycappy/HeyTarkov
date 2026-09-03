namespace HeyTarkov;

public enum WikiSource
{
    /// <summary>wikiwiki.jp/eft</summary>
    Japanese,

    /// <summary>escapefromtarkov.fandom.com</summary>
    English,
}

public enum EntryKind
{
    /// <summary>A trader task or story chapter.</summary>
    Task,

    /// <summary>A raid location.</summary>
    Map,

    /// <summary>An extraction point on a map.</summary>
    Extract,
}

/// <summary>
/// One thing that can be spoken and opened: a task, a map, or an extract.
///
/// The name is English on both wikis, so anything found on either is spoken the
/// same way; only the link differs. Either URL may be missing - the two wikis do
/// not carry the same set, and the English wiki has no per-extract anchors at
/// all (its extracts live in one table, so an extract there points at the
/// section rather than the row).
/// </summary>
public sealed class WikiEntry
{
    public EntryKind Kind { get; set; } = EntryKind.Task;

    public string Name { get; set; } = "";

    /// <summary>Trader for a task, map name for an extract, empty for a map.</summary>
    public string Group { get; set; } = "";

    /// <summary>"PMC" or "SCAV" when an extract is faction-specific.</summary>
    public string? Faction { get; set; }

    /// <summary>
    /// The seasonal event this task belongs to, or empty. Saying the event name
    /// is how the whole line is listed at once, so it is part of what the task
    /// can be called rather than a label hung off it.
    /// </summary>
    public string Event { get; set; } = "";

    public string? JapaneseUrl { get; set; }
    public string? EnglishUrl { get; set; }

    public string? Url(WikiSource source) =>
        source == WikiSource.Japanese ? JapaneseUrl : EnglishUrl;

    public bool IsOn(WikiSource source) => Url(source) is not null;

    /// <summary>What the candidate list shows: the name plus its faction.</summary>
    public string Display => Faction is null ? Name : $"{Name} ({Faction})";

    /// <summary>
    /// The ways this can be said. An extract is offered both with and without
    /// its map: the map qualifies it when the same extract name appears on
    /// several maps, but saying the whole thing every time is a chore.
    /// </summary>
    public IEnumerable<string> SpokenVariants()
    {
        if (Kind == EntryKind.Extract && Group.Length > 0) yield return $"{Group} {Name}";

        // "KORD BREACH Uninvited Guests - Part 1". Every prefix of this is
        // searchable, so saying just the event name lists the whole line.
        if (Event.Length > 0) yield return $"{Event} {Name}";

        yield return Name;
    }

    public override string ToString() => $"{Display}  ({Kind})";
}

public sealed class WikiCatalog
{
    public DateTimeOffset UpdatedAt { get; set; }
    public List<WikiEntry> Entries { get; set; } = new();

    public int CountOn(WikiSource source) => Entries.Count(e => e.IsOn(source));

    /// <summary>
    /// Only the entries the chosen wiki actually has a page for. Selecting a
    /// wiki selects the set: offering something whose only page is on the other
    /// wiki just pushes the reachable matches down the list.
    /// </summary>
    public List<WikiEntry> On(WikiSource source) => Entries.Where(e => e.IsOn(source)).ToList();

    public int CountOf(EntryKind kind) => Entries.Count(e => e.Kind == kind);
}
