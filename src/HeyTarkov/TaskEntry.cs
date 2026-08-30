namespace HeyTarkov;

public enum WikiSource
{
    /// <summary>wikiwiki.jp/eft</summary>
    Japanese,

    /// <summary>escapefromtarkov.fandom.com</summary>
    English,
}

/// <summary>
/// One task, with the page for it on each wiki. The name is English on both, so
/// a task found on either wiki is spoken the same way; only the link differs.
/// Either URL may be missing - the two wikis do not carry the same set.
/// </summary>
public sealed class TaskEntry
{
    public string Trader { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>Absolute URL on wikiwiki.jp, or null if that wiki lacks the page.</summary>
    public string? JapaneseUrl { get; set; }

    /// <summary>Absolute URL on the Fandom wiki, or null if it lacks the page.</summary>
    public string? EnglishUrl { get; set; }

    public string? Url(WikiSource source) =>
        source == WikiSource.Japanese ? JapaneseUrl : EnglishUrl;

    public bool IsOn(WikiSource source) => Url(source) is not null;

    public override string ToString() => $"{Name}  ({Trader})";
}

public sealed class TaskCatalogFile
{
    public DateTimeOffset UpdatedAt { get; set; }
    public List<TaskEntry> Tasks { get; set; } = new();

    public int CountOn(WikiSource source) => Tasks.Count(t => t.IsOn(source));

    /// <summary>
    /// Only the tasks the chosen wiki actually has a page for. Selecting a wiki
    /// selects the task set: offering a task whose only page is on the other
    /// wiki just pushes the real matches down the list.
    /// </summary>
    public List<TaskEntry> On(WikiSource source) => Tasks.Where(t => t.IsOn(source)).ToList();
}
