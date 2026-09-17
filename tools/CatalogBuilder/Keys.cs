using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HeyTarkov.CatalogBuilder;

/// <summary>
/// The keys - two hundred-odd doors, safes and caches, one wiki page each.
///
/// Everything needed is on the English page and nowhere else: an infobox line
/// saying what the key opens and on which map, the id the game's own locale is
/// keyed by, and an opening sentence carrying the short label the game prints
/// over the icon ("Dorm 314", "W306 San").
///
/// The Japanese wiki carries the same pages under the same English names, and
/// indexes most of them on one page, so the links cost one request rather than
/// two hundred.
/// </summary>
public static partial class Keys
{
    /// <summary>The full page title, as the API wants it.</summary>
    private const string Category = "Category:Keys";

    /// <summary>The Japanese wiki's own index of keys.</summary>
    private const string JapaneseIndex = "鍵";

    /// <summary>MediaWiki takes fifty titles per request.</summary>
    private const int Batch = 50;

    [GeneratedRegex(@"(?im)^\s*\|\s*node\s*=\s*([0-9a-f]{24})\s*$")]
    private static partial Regex ItemId();

    /// <summary>The bolded title, then the label in brackets, then the verb -
    /// the same opening sentence every item page has.</summary>
    [GeneratedRegex(@"'''(?:\{\{PAGENAME\}\}|[^']+)'''\s*\(([^)]{1,24})\)\s*(?:is|are)\b")]
    private static partial Regex LeadLabel();

    /// <summary>The infobox line that says what the key opens, and where.</summary>
    [GeneratedRegex(@"(?im)^\s*\|\s*usage\s*=\s*(.*)$")]
    private static partial Regex UsageLine();

    [GeneratedRegex(@"href=""/eft/([^""#?]+)""")]
    private static partial Regex JapaneseLink();

    public static async Task<List<WikiEntry>> FetchAsync(
        Wikis wikis, IReadOnlyList<WikiEntry> maps, CancellationToken ct = default)
    {
        var titles = await wikis.CategoryMembersAsync(Category, ct).ConfigureAwait(false);

        if (titles.Count == 0)
        {
            Console.Error.WriteLine("  no keys found in the category; skipping keys");
            return new List<WikiEntry>();
        }

        Console.WriteLine($"  {titles.Count} keys");

        var pages = await PagesAsync(titles, wikis, ct).ConfigureAwait(false);
        var indexed = await JapaneseIndexAsync(wikis, ct).ConfigureAwait(false);
        var mapNames = maps.Where(m => m.Kind == EntryKind.Map).Select(m => m.Name).ToList();

        var entries = new List<WikiEntry>();

        foreach (var title in titles)
        {
            pages.TryGetValue(title, out var page);

            entries.Add(new WikiEntry
            {
                Kind = EntryKind.Key,
                Name = title,
                Group = MapOf(page, mapNames) ?? "",
                ShortName = page.Lead,
                EnglishUrl = Wikis.FandomWiki + Wikis.EncodeWikiTitle(title),
                JapaneseUrl = indexed.Contains(title) ? wikis.JapaneseUrl(title) : null,
            });
        }

        await AddJapaneseLinksAsync(entries, wikis, ct).ConfigureAwait(false);
        await AddShortNamesAsync(entries, pages, ct).ConfigureAwait(false);

        Console.WriteLine($"  {entries.Count(e => e.Group.Length > 0)} placed on a map, "
                          + $"{entries.Count(e => e.ShortName is not null)} with the game's short label");
        Console.WriteLine($"  {entries.Count(e => e.JapaneseUrl is not null)} on the Japanese wiki");

        return entries;
    }

    // ------------------------------------------------------------- the pages

    /// <summary>What one key's wiki page says about itself.</summary>
    private readonly record struct KeyPage(string? Id, string? Lead, string? Usage);

    private static async Task<Dictionary<string, KeyPage>> PagesAsync(
        List<string> titles, Wikis wikis, CancellationToken ct)
    {
        var found = new Dictionary<string, KeyPage>(StringComparer.Ordinal);

        foreach (var batch in titles.Chunk(Batch))
        {
            var url = $"{Wikis.FandomApi}?action=query&prop=revisions&rvprop=content&rvslots=main"
                      + $"&titles={Uri.EscapeDataString(string.Join('|', batch))}"
                      + "&format=json&formatversion=2";

            if (await wikis.GetAsync(url, ct).ConfigureAwait(false) is not { Text: { } json }) continue;

            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("query", out var query)
                || !query.TryGetProperty("pages", out var pages))
            {
                continue;
            }

            foreach (var page in pages.EnumerateArray())
            {
                if (page.TryGetProperty("missing", out _)) continue;
                if (!page.TryGetProperty("revisions", out var revisions)
                    || revisions.GetArrayLength() == 0)
                {
                    continue;
                }

                var title = page.GetProperty("title").GetString();
                var text = revisions[0].GetProperty("slots").GetProperty("main")
                    .GetProperty("content").GetString();

                if (title is null || text is null) continue;

                var id = ItemId().Match(text);
                var lead = LeadLabel().Match(text);
                var usage = UsageLine().Match(text);

                found[title] = new KeyPage(
                    id.Success ? id.Groups[1].Value : null,
                    lead.Success ? lead.Groups[1].Value.Trim() : null,
                    usage.Success ? usage.Groups[1].Value : null);
            }
        }

        return found;
    }

    /// <summary>
    /// Which map the key belongs to, read from the sentence that says what it
    /// opens - "Unlocks room 314 (Marked room) in the three story dorms on
    /// [[Customs]]".
    ///
    /// Matched against the maps already in the catalog rather than a list
    /// written here, so the name on a key row is the same string as the name of
    /// the map itself.
    ///
    /// The earliest map named wins: the sentence leads with where the door is,
    /// and anything else it mentions comes later and in passing.
    /// </summary>
    private static string? MapOf(KeyPage page, IReadOnlyList<string> maps)
    {
        if (page.Usage is not { } usage) return null;

        var best = int.MaxValue;
        string? found = null;

        foreach (var map in maps)
        {
            var at = usage.IndexOf(map, StringComparison.OrdinalIgnoreCase);
            if (at < 0 || at >= best) continue;

            best = at;
            found = map;
        }

        return found;
    }

    // -------------------------------------------------------- japanese links

    /// <summary>
    /// The Japanese wiki's index of keys, which links most of them. One request
    /// instead of one per key.
    /// </summary>
    private static async Task<HashSet<string>> JapaneseIndexAsync(Wikis wikis, CancellationToken ct)
    {
        var titles = new HashSet<string>(StringComparer.Ordinal);

        var page = await wikis.GetAsync(wikis.JapaneseUrl(JapaneseIndex), ct).ConfigureAwait(false);
        if (page.Text is not { } html) return titles;

        foreach (Match match in JapaneseLink().Matches(html))
            titles.Add(WebUtility.UrlDecode(match.Groups[1].Value).Trim());

        Console.WriteLine($"  {titles.Count} pages linked from the Japanese index");
        return titles;
    }

    /// <summary>
    /// The index does not link every key, so the rest are asked for one at a
    /// time. A page the wiki would not answer about is left without a link and
    /// counted as unresolved by the transport, which is what stops a busy
    /// afternoon from quietly shipping a catalog with links missing.
    /// </summary>
    private static async Task AddJapaneseLinksAsync(
        List<WikiEntry> entries, Wikis wikis, CancellationToken ct)
    {
        var rest = entries.Where(e => e.JapaneseUrl is null).ToList();
        if (rest.Count == 0) return;

        Console.WriteLine($"  asking about {rest.Count} the index does not link");

        foreach (var entry in rest)
        {
            var url = wikis.JapaneseUrl(entry.Name);
            if ((await wikis.GetAsync(url, ct).ConfigureAwait(false)).Exists) entry.JapaneseUrl = url;
        }
    }

    // ----------------------------------------------------------- short labels

    /// <summary>
    /// The label the game prints over the icon. The game's own locale answers
    /// first and the wiki's opening sentence - already read above - covers what
    /// the locale is too old to know.
    /// </summary>
    private static async Task AddShortNamesAsync(
        List<WikiEntry> entries, Dictionary<string, KeyPage> pages, CancellationToken ct)
    {
        var locale = await GameLocale.ShortNamesAsync(ct).ConfigureAwait(false);
        if (locale is null) return;

        var taken = 0;

        foreach (var entry in entries)
        {
            if (!pages.TryGetValue(entry.Name, out var page) || page.Id is null) continue;
            if (!locale.TryGetValue(page.Id, out var label)) continue;

            entry.ShortName = label;
            taken++;
        }

        Console.WriteLine($"  {taken} short labels from the mirrored game locale");
    }
}
