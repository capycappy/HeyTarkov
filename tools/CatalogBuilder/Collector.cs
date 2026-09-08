using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HeyTarkov.CatalogBuilder;

/// <summary>
/// The items Fence's Collector task wants handed over.
///
/// They are in the catalog for the same reason tasks are - saying one opens its
/// wiki page - but the point of having them is the checklist, which needs to
/// know the whole set to say what is still missing.
/// </summary>
public static partial class Collector
{
    private const string EnglishPage = "Collector";
    private const string JapanesePage = "Fence/Collector";

    /// <summary>The task's own page, which is not one of the items.</summary>
    private const string Group = "Collector";

    [GeneratedRegex(@"Hand over the found .*?item:\s*\[\[([^\]|]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Objective();

    /// <summary>
    /// An item link in the objectives list on the Japanese page.
    ///
    /// Anchored on the sentence that follows it rather than on where the
    /// section starts. The page carries a sidebar of map names and forum links
    /// that are shaped exactly like item links, and every one of them was
    /// coming through when this matched on the link alone.
    /// </summary>
    [GeneratedRegex("""<a[^>]+href="/eft/([^"]+)"[^>]*>([^<]*)</a>\s*をレイド内で手に入れる""")]
    private static partial Regex JapaneseObjective();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagPattern();

    /// <summary>
    /// The wiki writes a sentence where a Japanese name would go when the item
    /// has none. Taken literally it becomes the item's name in the checklist.
    /// </summary>
    [GeneratedRegex("日本語名称無し|英名称と同じ")]
    private static partial Regex NoJapaneseName();

    public static async Task<List<WikiEntry>> FetchAsync(Wikis wikis, CancellationToken ct = default)
    {
        var english = await EnglishItemsAsync(wikis, ct).ConfigureAwait(false);
        if (english.Count == 0)
        {
            Console.Error.WriteLine("  no items found on the English page; skipping Collector");
            return new List<WikiEntry>();
        }

        Console.WriteLine($"  {english.Count} items to hand over");

        var japanese = await JapaneseTitlesAsync(wikis, ct).ConfigureAwait(false);
        var paired = Pair(english, japanese);

        var entries = new List<WikiEntry>();

        foreach (var name in english)
        {
            var entry = new WikiEntry
            {
                Kind = EntryKind.Item,
                Name = name,
                Group = Group,
                EnglishUrl = Wikis.FandomWiki + Wikis.EncodeWikiTitle(name),
            };

            if (paired.TryGetValue(name, out var japaneseTitle))
            {
                entry.JapaneseUrl = wikis.JapaneseUrl(japaneseTitle);
                entry.JapaneseName = await JapaneseNameAsync(wikis, japaneseTitle, ct)
                    .ConfigureAwait(false);
            }

            entries.Add(entry);
        }

        await AddShortNamesAsync(entries, wikis, ct).ConfigureAwait(false);

        var named = entries.Count(e => e.JapaneseName is not null);
        var labelled = entries.Count(e => e.ShortName is not null);
        Console.WriteLine($"  {paired.Count} on the Japanese wiki, {named} with a Japanese name");
        Console.WriteLine($"  {labelled} with the game's short label");

        return entries;
    }

    // ---------------------------------------------------------- the item list

    /// <summary>
    /// The objectives section of the English page, which lists the items one
    /// per line as "Hand over the found in raid item: [[Name]]". Reading the
    /// list rather than the summary table is deliberate - the table is laid out
    /// for a human and has rearranged itself before.
    /// </summary>
    private static async Task<List<string>> EnglishItemsAsync(Wikis wikis, CancellationToken ct)
    {
        var url = $"{Wikis.FandomApi}?action=query&prop=revisions&rvprop=content&rvslots=main"
                  + $"&titles={Uri.EscapeDataString(EnglishPage)}&format=json&formatversion=2";

        if (await wikis.GetAsync(url, ct).ConfigureAwait(false) is not { Text: { } json })
            return new List<string>();

        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("query", out var query)
            || !query.TryGetProperty("pages", out var pages)
            || pages.GetArrayLength() == 0)
        {
            return new List<string>();
        }

        var page = pages[0];
        if (!page.TryGetProperty("revisions", out var revisions) || revisions.GetArrayLength() == 0)
            return new List<string>();

        var text = revisions[0].GetProperty("slots").GetProperty("main")
            .GetProperty("content").GetString() ?? "";

        var start = text.IndexOf("==Objectives==", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return new List<string>();

        var end = text.IndexOf("\n==", start + 3, StringComparison.Ordinal);
        var section = end < 0 ? text[start..] : text[start..end];

        var names = new List<string>();
        foreach (Match match in Objective().Matches(section))
        {
            var name = WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
            if (name.Length > 0 && !names.Contains(name)) names.Add(name);
        }

        return names;
    }

    /// <summary>
    /// The same list off the Japanese page, as the page titles it links to.
    /// Those titles are what its own URLs use, and they are not always spelled
    /// the way the English wiki spells them.
    /// </summary>
    private static async Task<List<string>> JapaneseTitlesAsync(Wikis wikis, CancellationToken ct)
    {
        var page = await wikis.GetAsync(wikis.JapaneseUrl(JapanesePage), ct).ConfigureAwait(false);
        if (page.Text is not { } html) return new List<string>();

        var titles = new List<string>();

        foreach (Match match in JapaneseObjective().Matches(html))
        {
            var title = WebUtility.UrlDecode(match.Groups[1].Value).Trim();
            if (title.Length > 0 && !titles.Contains(title)) titles.Add(title);
        }

        return titles;
    }

    // ------------------------------------------------------------- pairing

    /// <summary>
    /// English title to Japanese title. Almost all of them are the same string;
    /// the ones that are not are typos on one side or the other.
    ///
    /// Normalising will not do. "DesmondPilak CD" and "Desmond Pilak CD" close
    /// up when spaces go, but "Bottle of YMXC water" and "Bottle of YXMC water"
    /// have two letters swapped and no amount of tidying makes them equal. So
    /// the leftovers go through edit distance, and every pair made that way is
    /// printed - if a wiki fixes its spelling the pairing should change
    /// silently, but if it renames something it should be noticed.
    /// </summary>
    private static Dictionary<string, string> Pair(List<string> english, List<string> japanese)
    {
        var paired = new Dictionary<string, string>(StringComparer.Ordinal);
        var spare = new List<string>(japanese);

        foreach (var name in english)
        {
            var exact = spare.FirstOrDefault(j => string.Equals(j, name, StringComparison.Ordinal));
            if (exact is null) continue;

            paired[name] = exact;
            spare.Remove(exact);
        }

        foreach (var name in english.Where(n => !paired.ContainsKey(n)))
        {
            var best = spare
                .Select(j => (Title: j, Distance: Distance(Squash(name), Squash(j))))
                .OrderBy(match => match.Distance)
                .FirstOrDefault();

            // A quarter of the name may differ. Two transposed letters in
            // "YMXC" is a distance of 2 in a name of twenty; a different item
            // is nowhere near.
            if (best.Title is null || best.Distance > Math.Max(2, name.Length / 4)) continue;

            paired[name] = best.Title;
            spare.Remove(best.Title);

            Console.WriteLine($"  spelled differently: \"{name}\" <- \"{best.Title}\" "
                              + $"(distance {best.Distance})");
        }

        var unmatched = english.Where(n => !paired.ContainsKey(n)).ToList();

        foreach (var name in unmatched)
            Console.WriteLine($"  no Japanese page: {name}");

        foreach (var leftover in spare)
            Console.WriteLine($"  Japanese page nothing claimed: {leftover}");

        // A wiki renaming an item leaves one entry in each list, too far apart
        // for edit distance to join. "Glorious E mask" and "Glorious E
        // lightweight armored mask" are the same thing; nothing here can know
        // that, so say so rather than reporting an item gained and one lost.
        if (unmatched.Count > 0 && spare.Count > 0)
        {
            Console.WriteLine("  ^ these may be the same items under different names,"
                              + " rather than one wiki having gained or lost any");
        }

        return paired;
    }

    private static string Squash(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    // ------------------------------------------------------- japanese names

    [GeneratedRegex(@"(?is)<h1[^>]*>(.*?)</h1>")]
    private static partial Regex JapaneseHeading();

    /// <summary>
    /// wikiwiki titles an item page "Golden egg / ゴールデンエッグ". The half
    /// after the slash is the name to show; there is not always one.
    ///
    /// The leading "#" on FireKlean is not stray markup, however much it looks
    /// like it - the game's own short label for that item is "#FireKlean". It
    /// took a second source to find that out, and it stays.
    /// </summary>
    private static async Task<string?> JapaneseNameAsync(Wikis wikis, string title, CancellationToken ct)
    {
        var page = await wikis.GetAsync(wikis.JapaneseUrl(title), ct).ConfigureAwait(false);
        if (page.Text is not { } html) return null;

        var heading = JapaneseHeading().Match(html);
        if (!heading.Success) return null;

        var text = WebUtility.HtmlDecode(TagPattern().Replace(heading.Groups[1].Value, "")).Trim();

        var slash = text.IndexOf('/');
        if (slash < 0) return null;

        var name = text[(slash + 1)..].Trim().Trim('「', '」');

        return name.Length == 0 || NoJapaneseName().IsMatch(name) ? null : name;
    }

    // --------------------------------------------------------- short labels

    private const string TarkovDevApi = "https://api.tarkov.dev/graphql";

    /// <summary>
    /// The game's own English locale, as the Single Player Tarkov project
    /// mirrors it. Keyed by the item's BSG id: "&lt;id&gt; ShortName".
    /// </summary>
    private const string GameLocale =
        "https://raw.githubusercontent.com/sp-tarkov/server/master"
        + "/project/assets/database/locales/global/en.json";

    /// <summary>
    /// The label the game prints over the icon in the stash - "BeardOil",
    /// "Plague mask", "WZ". It is what a person reads when checking what they
    /// already have, so the checklist leads with it.
    ///
    /// Neither wiki carries it. Two sources do, and both are needed: tarkov.dev
    /// follows the live game but is a service that goes down, and the mirrored
    /// locale is a git repository that cannot - but it was last refreshed in
    /// March 2025 and knows nothing of the items added since. Between them they
    /// covered forty-four of forty-four; the locale alone covered thirty-three.
    ///
    /// Coming back with none of them is not fatal. A checklist without the
    /// labels is poorer but still works, and a rebuild should not fail because
    /// somebody else's server is having an afternoon.
    /// </summary>
    private static async Task AddShortNamesAsync(
        List<WikiEntry> entries, Wikis wikis, CancellationToken ct)
    {
        var labels = await FromTarkovDevAsync(entries, ct).ConfigureAwait(false);

        var wanting = entries.Where(e => !labels.ContainsKey(e.Name)).ToList();
        if (wanting.Count > 0)
            await FromGameLocaleAsync(wanting, labels, wikis, ct).ConfigureAwait(false);

        foreach (var entry in entries)
            if (labels.TryGetValue(entry.Name, out var label)) entry.ShortName = label;

        foreach (var entry in entries.Where(e => e.ShortName is null))
            Console.WriteLine($"  no short label: {entry.Name}");
    }

    private static async Task<Dictionary<string, string>> FromTarkovDevAsync(
        List<WikiEntry> entries, CancellationToken ct)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);

        var query = "{ items(names: "
                    + JsonSerializer.Serialize(entries.Select(e => e.Name).ToList())
                    + ") { name shortName } }";

        string json;
        try
        {
            using var http = Client();
            using var content = new StringContent(
                JsonSerializer.Serialize(new { query }),
                System.Text.Encoding.UTF8,
                "application/json");

            using var response = await http.PostAsync(TarkovDevApi, content, ct).ConfigureAwait(false);
            json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  tarkov.dev unreachable ({ex.Message}); using the mirrored locale");
            return labels;
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                Console.WriteLine($"  tarkov.dev said {errors}; using the mirrored locale");
                return labels;
            }

            if (!document.RootElement.TryGetProperty("data", out var data)
                || !data.TryGetProperty("items", out var items))
            {
                Console.WriteLine("  tarkov.dev answered with something unexpected");
                return labels;
            }

            foreach (var item in items.EnumerateArray())
            {
                var name = item.GetProperty("name").GetString();
                var label = item.GetProperty("shortName").GetString();
                if (name is null || string.IsNullOrWhiteSpace(label)) continue;

                labels[name] = label.Trim();
            }
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"  tarkov.dev answered with something unreadable: {ex.Message}");
        }

        if (labels.Count > 0) Console.WriteLine($"  {labels.Count} short labels from tarkov.dev");
        return labels;
    }

    /// <summary>
    /// The wiki knows each item's BSG id, and the mirrored locale is keyed by
    /// it. One batched request covers every item's infobox.
    /// </summary>
    private static async Task FromGameLocaleAsync(
        List<WikiEntry> wanting, Dictionary<string, string> labels,
        Wikis wikis, CancellationToken ct)
    {
        var ids = await ItemIdsAsync(wanting.Select(e => e.Name).ToList(), wikis, ct)
            .ConfigureAwait(false);

        if (ids.Count == 0) return;

        string locale;
        try
        {
            using var http = Client();
            locale = await http.GetStringAsync(GameLocale, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  the mirrored locale is unreachable too: {ex.Message}");
            return;
        }

        using var document = JsonDocument.Parse(locale);
        var root = document.RootElement;
        var before = labels.Count;

        foreach (var entry in wanting)
        {
            if (!ids.TryGetValue(entry.Name, out var id)) continue;
            if (!root.TryGetProperty($"{id} ShortName", out var value)) continue;

            var label = value.GetString();
            if (!string.IsNullOrWhiteSpace(label)) labels[entry.Name] = label.Trim();
        }

        Console.WriteLine($"  {labels.Count - before} more from the mirrored game locale");
    }

    [GeneratedRegex(@"(?im)^\s*\|\s*node\s*=\s*([0-9a-f]{24})\s*$")]
    private static partial Regex ItemId();

    private static async Task<Dictionary<string, string>> ItemIdsAsync(
        List<string> names, Wikis wikis, CancellationToken ct)
    {
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);

        // MediaWiki takes fifty titles at a time; there are never that many.
        var url = $"{Wikis.FandomApi}?action=query&prop=revisions&rvprop=content&rvslots=main"
                  + $"&titles={Uri.EscapeDataString(string.Join('|', names))}"
                  + "&format=json&formatversion=2";

        if (await wikis.GetAsync(url, ct).ConfigureAwait(false) is not { Text: { } json })
            return ids;

        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("query", out var query)
            || !query.TryGetProperty("pages", out var pages))
        {
            return ids;
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
            if (id.Success) ids[title] = id.Groups[1].Value;
        }

        return ids;
    }

    private static HttpClient Client()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "HeyTarkovCatalogBuilder/1.0 (+https://github.com/capycappy/HeyTarkov)");
        return http;
    }
}
