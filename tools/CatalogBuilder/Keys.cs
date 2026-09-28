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
    /// <summary>
    /// The full page titles, as the API wants them. Keycards are a category of
    /// their own on the wiki, but not to a player: a card that opens the
    /// laboratory's doors is looked up for the same reason a key is.
    /// </summary>
    private static readonly string[] Categories = { "Category:Keys", "Category:Keycards" };

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

    /// <summary>
    /// The section that says where the lock is. Written for keys whose infobox
    /// says only "this key has no usage" - the room is always open, or the lock
    /// was taken out of the game - but which still sit on a map: every Health
    /// Resort room key is on Shoreline, every RB- key on Reserve.
    /// </summary>
    [GeneratedRegex(@"(?is)==\s*Lock Location\s*==(.*?)(?=?
==|$)")]
    private static partial Regex LockLocation();

    [GeneratedRegex(@"href=""/eft/([^""#?]+)""")]
    private static partial Regex JapaneseLink();

    /// <summary>Kana or kanji anywhere in the text.</summary>
    [GeneratedRegex(@"[぀-ヿ一-鿿]")]
    private static partial Regex JapaneseScript();

    public static async Task<List<WikiEntry>> FetchAsync(
        Wikis wikis, IReadOnlyList<WikiEntry> maps, CancellationToken ct = default)
    {
        var titles = new List<string>();
        var already = new HashSet<string>(StringComparer.Ordinal);

        foreach (var category in Categories)
        {
            foreach (var title in await wikis.CategoryMembersAsync(category, ct).ConfigureAwait(false))
                if (already.Add(title)) titles.Add(title);
        }

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
        await AddJapaneseNamesAsync(entries, pages, ct).ConfigureAwait(false);
        await AddNamesFromJapaneseWikiAsync(entries, wikis, ct).ConfigureAwait(false);

        Console.WriteLine($"  {entries.Count(e => e.Group.Length > 0)} placed on a map, "
                          + $"{entries.Count(e => e.ShortName is not null)} with the game's short label");
        Console.WriteLine($"  {entries.Count(e => e.JapaneseUrl is not null)} on the Japanese wiki, "
                          + $"{entries.Count(e => e.JapaneseName is not null)} with the name the Japanese game shows");

        return entries;
    }

    // ------------------------------------------------------------- the pages

    /// <summary>What one key's wiki page says about itself.</summary>
    private readonly record struct KeyPage(string? Id, string? Lead, string? Usage, string? Lock);

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
                var where = LockLocation().Match(text);

                found[title] = new KeyPage(
                    id.Success ? id.Groups[1].Value : null,
                    lead.Success ? lead.Groups[1].Value.Trim() : null,
                    usage.Success ? usage.Groups[1].Value : null,
                    where.Success ? where.Groups[1].Value : null);
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
    private static string? MapOf(KeyPage page, IReadOnlyList<string> maps) =>
        MapIn(page.Usage, maps) ?? MapIn(page.Lock, maps);

    private static string? MapIn(string? usage, IReadOnlyList<string> maps)
    {
        if (usage is null) return null;

        // Only the links. The prose around them is full of words that contain
        // a map's name without being one: "the laboratory" holds "The Lab",
        // and a key to a door on Icebreaker was filed under the wrong place
        // because of it.
        //
        // The last link wins, because the sentence is written outwards - what
        // the key opens, then where that is: "the door to the [[The Labyrinth]]
        // transit on [[Shoreline]]" is a door standing on Shoreline. Reading
        // the first link instead put that key inside the place it leads to.
        string? found = null;

        foreach (Match link in WikiLink().Matches(usage))
        {
            var target = link.Groups[1].Value.Trim();

            foreach (var map in maps)
                if (string.Equals(map, target, StringComparison.OrdinalIgnoreCase)) found = map;
        }

        return found;
    }

    /// <summary>A wiki link, without the text it is shown as.</summary>
    [GeneratedRegex(@"\[\[([^\]\|#]+)")]
    private static partial Regex WikiLink();

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

    // --------------------------------------------------------- japanese names

    /// <summary>
    /// The name the game shows when it is played in Japanese - "マークの刻まれた
    /// 廃工場の鍵" - which is what a player reads off the screen and says.
    ///
    /// Taken from the game's own Japanese file by the item id rather than from
    /// the wiki: the wiki's page titles are the English names, and its headings
    /// are written by hand. A name the game has left in English is not kept,
    /// because it adds nothing the English name does not already cover.
    /// </summary>
    private static async Task AddJapaneseNamesAsync(
        List<WikiEntry> entries, Dictionary<string, KeyPage> pages, CancellationToken ct)
    {
        var names = await GameLocale.JapaneseNamesAsync(ct).ConfigureAwait(false);
        if (names is null) return;

        foreach (var entry in entries)
        {
            if (!pages.TryGetValue(entry.Name, out var page) || page.Id is null) continue;
            if (!names.TryGetValue(page.Id, out var name)) continue;

            if (JapaneseScript().IsMatch(name)) entry.JapaneseName = name;
        }
    }

    /// <summary>
    /// The Japanese wiki's own heading, for the keys the game's Japanese file
    /// does not cover - it is titled "Company director's room key/会社役員室の
    /// 鍵", the English name and the Japanese one either side of a slash.
    ///
    /// Only ever a fallback. A heading is written by hand and need not match
    /// the game word for word, so it is used where the alternative is no
    /// Japanese name at all, and never in place of the game's own.
    /// </summary>
    private static async Task AddNamesFromJapaneseWikiAsync(
        List<WikiEntry> entries, Wikis wikis, CancellationToken ct)
    {
        var rest = entries
            .Where(e => e.JapaneseName is null && e.JapaneseUrl is not null)
            .ToList();

        if (rest.Count == 0) return;

        Console.WriteLine($"  asking the Japanese wiki about {rest.Count} without a Japanese name");

        var taken = 0;

        foreach (var entry in rest)
        {
            var page = await wikis.GetAsync(entry.JapaneseUrl!, ct).ConfigureAwait(false);
            if (page.Text is not { } html) continue;

            var heading = PageHeading().Match(html);
            if (!heading.Success) continue;

            var title = WebUtility.HtmlDecode(Tags().Replace(heading.Groups[1].Value, "")).Trim();

            // Everything after the English name. A heading without a slash is
            // just the English name again and says nothing new.
            var slash = title.IndexOf('/');
            if (slash < 0) continue;

            var name = title[(slash + 1)..].Trim();

            if (name.Length == 0 || !JapaneseScript().IsMatch(name)) continue;

            // Some headings say, in Japanese, that there is no Japanese name.
            // Taking that literally would put "日本語名称無し" on the key row
            // and into the grammar.
            if (NoName().IsMatch(name)) continue;

            entry.JapaneseName = name;
            taken++;
        }

        Console.WriteLine($"  {taken} from the Japanese wiki's heading");
    }

    [GeneratedRegex(@"<h1[^>]*>(.*?)</h1>", RegexOptions.Singleline)]
    private static partial Regex PageHeading();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    /// <summary>A heading that says the item has no Japanese name.</summary>
    [GeneratedRegex(@"名称?(無し|なし)|翻訳(無し|なし)")]
    private static partial Regex NoName();

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
