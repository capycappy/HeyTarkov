// Copied from src/HeyTarkov so the generator and the app agree on the shape of
// the file. Keep the two in step: the app deserialises exactly what this writes.
namespace HeyTarkov.CatalogBuilder;

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

    /// <summary>
    /// An item the Collector task wants handed over. Added last on purpose -
    /// the value is written into tasks.json as a number, so an existing file
    /// keeps meaning what it meant.
    /// </summary>
    Item,
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

    /// <summary>The seasonal event this task belongs to, or empty.</summary>
    public string Event { get; set; } = "";

    /// <summary>
    /// The label the game prints over the icon in the stash - "BeardOil",
    /// "Plague mask". Null for anything that is not an item.
    ///
    /// This is what a person reads when checking what they already have, so it
    /// is what the checklist shows first. Neither wiki carries it; it comes
    /// from the game's own item data.
    /// </summary>
    public string? ShortName { get; set; }

    /// <summary>
    /// The Japanese name, where the Japanese wiki gives one. Shown beside the
    /// English name when that wiki is selected, and null when the wiki says in
    /// so many words that there is no Japanese name for the thing.
    ///
    /// Never spoken. The grammar stays on the English name, which is what both
    /// wikis title their pages with.
    /// </summary>
    public string? JapaneseName { get; set; }

    public string? JapaneseUrl { get; set; }
    public string? EnglishUrl { get; set; }

    public string? Url(WikiSource source) =>
        source == WikiSource.Japanese ? JapaneseUrl : EnglishUrl;

    public bool IsOn(WikiSource source) => Url(source) is not null;

    /// <summary>What the candidate list shows: the name plus its faction.</summary>
    public string Display => Faction is null ? Name : $"{Name} ({Faction})";

    /// <summary>
    /// What the user says. An extract is qualified by its map so that the same
    /// extract name on several maps can be told apart.
    /// </summary>
    public string SpokenName => Kind == EntryKind.Extract ? $"{Group} {Name}" : Name;

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
