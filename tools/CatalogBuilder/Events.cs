using System.Text.Json;
using System.Text.RegularExpressions;

namespace HeyTarkov.CatalogBuilder;

/// <summary>
/// The event that is on right now, and its tasks.
///
/// Seasons mark their tasks with a link to the Seasons page; time-limited
/// events do not. What they have instead is the wiki's Events page, which
/// opens with the newest event as its first heading - "Scourge Incursion
/// (16 October 2026)" - followed by "Quest [[Hidden Route]] has been added."
/// for each task it brought.
///
/// Only that first heading is read. The page goes back years and lists forty
/// events; tagging all of them would put an event badge on a quarter of the
/// catalog for events long finished. And a task is only taken as an event task
/// if its own page also carries {{Event content}}: some headings are game
/// updates that added ordinary, permanent tasks.
/// </summary>
public static partial class Events
{
    private const string EventsPage = "Events";
    private const string EventTemplate = "Template:Event content";

    [GeneratedRegex(@"^==\s*([^=].*?)\s*==\s*$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"\s*\([^)]*\)\s*$")]
    private static partial Regex DateSuffix();

    [GeneratedRegex(@"Quest \[\[([^\]|]+)(?:\|[^\]]*)?\]\] has been added")]
    private static partial Regex AddedQuest();

    /// <summary>The newest event's name, and the names of its tasks.</summary>
    public readonly record struct Current(string Name, IReadOnlySet<string> Tasks);

    /// <summary>The Events page's wikitext, or nothing if the wiki would not say.</summary>
    private static async Task<string?> PageAsync(Wikis wikis, CancellationToken ct)
    {
        var url = $"{Wikis.FandomApi}?action=parse&page={Uri.EscapeDataString(EventsPage)}"
                  + "&prop=wikitext&format=json&formatversion=2";

        if (await wikis.GetAsync(url, ct).ConfigureAwait(false) is not { Text: { } json }) return null;

        using var document = JsonDocument.Parse(json);

        return document.RootElement.TryGetProperty("parse", out var parse)
               && parse.TryGetProperty("wikitext", out var wikitext)
            ? wikitext.GetString()
            : null;
    }

    /// <summary>
    /// The events with tasks in the catalog, newest first. Those on the Events
    /// page come in its order - newest at the top - and the seasons, which it
    /// does not list, after them; a season runs for months, so it is older
    /// than whatever event is on.
    /// </summary>
    public static async Task<List<string>> OrderAsync(
        IEnumerable<WikiEntry> entries, Wikis wikis, CancellationToken ct = default)
    {
        var named = entries.Select(e => e.Event).Where(e => e.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var order = new List<string>();

        if (await PageAsync(wikis, ct).ConfigureAwait(false) is { } text)
        {
            foreach (var line in text.Split('\n'))
            {
                if (Heading().Match(line.TrimEnd('\r')) is not { Success: true } heading) continue;

                var name = DateSuffix().Replace(heading.Groups[1].Value, "").Trim();
                if (named.Contains(name) && !order.Contains(name)) order.Add(name);
            }
        }

        order.AddRange(named.Where(n => !order.Contains(n)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));

        Console.WriteLine($"  events, newest first: {string.Join(" / ", order)}");
        return order;
    }

    public static async Task<Current?> FetchNewestAsync(Wikis wikis, CancellationToken ct = default)
    {
        if (await PageAsync(wikis, ct).ConfigureAwait(false) is not { } text) return null;

        string? name = null;
        var listed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in text.Split('\n'))
        {
            if (Heading().Match(line.TrimEnd('\r')) is { Success: true } heading)
            {
                // The second heading is the end of the newest event.
                if (name is not null) break;

                name = DateSuffix().Replace(heading.Groups[1].Value, "").Trim();
                continue;
            }

            if (name is null) continue;

            foreach (Match added in AddedQuest().Matches(line))
                listed.Add(added.Groups[1].Value.Trim());
        }

        if (name is null || listed.Count == 0) return null;

        var marked = await MarkedAsync(wikis, ct).ConfigureAwait(false);
        var tasks = listed.Where(marked.Contains).ToHashSet(StringComparer.Ordinal);

        if (tasks.Count == 0) return null;

        Console.WriteLine($"  current event: {name}, {tasks.Count} tasks");
        return new Current(name, tasks);
    }

    /// <summary>Every page that carries the event notice.</summary>
    private static async Task<HashSet<string>> MarkedAsync(Wikis wikis, CancellationToken ct)
    {
        var marked = new HashSet<string>(StringComparer.Ordinal);
        string? next = null;

        do
        {
            var url = $"{Wikis.FandomApi}?action=query&list=embeddedin"
                      + $"&eititle={Uri.EscapeDataString(EventTemplate)}&eilimit=500"
                      + "&format=json&formatversion=2"
                      + (next is null ? "" : $"&eicontinue={Uri.EscapeDataString(next)}");

            if (await wikis.GetAsync(url, ct).ConfigureAwait(false) is not { Text: { } json }) break;

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.TryGetProperty("query", out var query) && query.TryGetProperty("embeddedin", out var pages))
            {
                foreach (var page in pages.EnumerateArray())
                    if (page.GetProperty("title").GetString() is { } title) marked.Add(title);
            }

            next = root.TryGetProperty("continue", out var more) && more.TryGetProperty("eicontinue", out var token)
                ? token.GetString()
                : null;
        }
        while (next is not null);

        return marked;
    }
}
