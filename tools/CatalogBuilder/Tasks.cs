using System.Net;
using System.Text.RegularExpressions;

namespace HeyTarkov.CatalogBuilder;

/// <summary>Trader tasks and story chapters, from both wikis.</summary>
public static partial class Tasks
{
    /// <summary>Trader tasks, then story tasks - two index pages on wikiwiki.</summary>
    private static readonly string[] JapaneseIndexPages =
    {
        ":タスク一覧",
        "ストーリータスク",
    };

    private static readonly string[] FandomCategories =
    {
        "Category:Quests",
        "Category:Story chapters",
    };

    /// <summary>Pages in those categories that are not tasks.</summary>
    private static readonly HashSet<string> FandomExcluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "Story chapters", "Quests",
    };

    private static readonly HashSet<string> JapaneseCategories = new(StringComparer.Ordinal)
    {
        "Prapor", "Therapist", "Fence", "Skier", "Peacekeeper", "Mechanic",
        "Ragman", "Jaeger", "Ref", "Lightkeeper", "BTR Driver", "Arena",
        "ストーリータスク",
    };

    private const string StoryCategoryJapanese = "ストーリータスク";
    private const string StoryTrader = "Story";

    /// <summary>
    /// Pages that exist on wikiwiki but are not linked from any index we read.
    /// Seasonal event tasks land here: the page is written before anybody wires
    /// it into the task list, and until they do it is unreachable by crawling.
    /// Each is checked to exist before it is used, so a stale line drops out
    /// rather than producing a dead entry.
    /// </summary>
    private static readonly string[] JapaneseExtras =
    {
        // Empty on purpose. Event tasks used to be listed here by hand; they
        // are found by FindJapaneseEventPagesAsync now. This stays for the
        // orphan that is not part of an event, which will turn up eventually.
    };

    /// <summary>
    /// Prefixes wikiwiki puts on event task pages to group them together. They
    /// are page naming rather than part of the task's name - the game calls it
    /// "Uninvited Guests - Part 1" - and stripping them is what lets the entry
    /// merge with the same task on the English wiki instead of becoming a
    /// second one nobody can open.
    /// </summary>
    private static readonly string[] EventPrefixes =
    {
        "KORD BREACH ",
    };

    private static string WithoutEventPrefix(string name)
    {
        foreach (var prefix in EventPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return name[prefix.Length..].Trim();
        }

        return name;
    }

    [GeneratedRegex("""href="(/eft/[^"]+)" title="([^"]+)" class="rel-wiki-page""")]
    private static partial Regex JapaneseLink();

    /// <summary>Fandom disambiguates some titles; the suffix is not spoken.</summary>
    [GeneratedRegex(@"\s*\((?:story chapter|quest)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex FandomQualifier();

    public static async Task<List<WikiEntry>> FetchAsync(Wikis wikis, CancellationToken ct = default)
    {
        var byKey = new Dictionary<string, WikiEntry>(StringComparer.Ordinal);

        foreach (var (trader, name, url) in await JapaneseAsync(wikis, ct).ConfigureAwait(false))
        {
            var key = Naming.Normalize(name);
            if (key.Length == 0) continue;

            if (!byKey.TryGetValue(key, out var entry))
            {
                entry = new WikiEntry { Kind = EntryKind.Task, Name = name, Group = trader };
                byKey[key] = entry;
            }

            entry.JapaneseUrl ??= url;
        }

        var events = await Seasons.FetchAsync(wikis, ct).ConfigureAwait(false);

        foreach (var (name, url) in await EnglishAsync(wikis, ct).ConfigureAwait(false))
        {
            var key = Naming.Normalize(name);
            if (key.Length == 0) continue;

            if (!byKey.TryGetValue(key, out var entry))
            {
                entry = new WikiEntry { Kind = EntryKind.Task, Name = name };
                byKey[key] = entry;
            }

            entry.EnglishUrl ??= url;
        }

        // The event on now, from the Events page: a time-limited event does not
        // mark its tasks the way a season does.
        if (await Events.FetchNewestAsync(wikis, ct).ConfigureAwait(false) is { } current)
        {
            var named = current.Tasks.Select(Naming.Normalize).ToHashSet(StringComparer.Ordinal);

            foreach (var (key, entry) in byKey)
                if (entry.Event.Length == 0 && named.Contains(key)) entry.Event = current.Name;
        }

        foreach (var (key, entry) in byKey)
        {
            if (!events.TryGetValue(key, out var season)) continue;

            entry.Event = season.Event;

            // The trader only ever came from the Japanese wiki, and most of
            // these have no Japanese page. Fandom's infobox knows.
            if (entry.Group.Length == 0) entry.Group = season.Giver;
        }

        await FindJapaneseEventPagesAsync(wikis, byKey.Values, ct).ConfigureAwait(false);

        return byKey.Values.ToList();
    }

    /// <summary>
    /// Japanese pages for tasks the Japanese index does not link yet.
    ///
    /// The index is edited by hand and lags behind: the six tasks of a new
    /// event had pages of their own for days before it listed any of them. A
    /// task's page is always "Trader/Name", and both are known from the English
    /// wiki, so the page is asked for directly. Only for the tasks given - new
    /// ones - rather than every task without a link: most of those are arena
    /// tasks the Japanese wiki does not cover, and asking about three hundred
    /// of them each time would mean three hundred refusals.
    /// </summary>
    public static async Task FindJapanesePagesAsync(
        IEnumerable<WikiEntry> tasks, Wikis wikis, CancellationToken ct = default)
    {
        var found = 0;

        foreach (var task in tasks)
        {
            if (task.JapaneseUrl is not null || task.Group.Length == 0) continue;

            var category = task.Group == StoryTrader ? StoryCategoryJapanese : task.Group;
            if (!JapaneseCategories.Contains(category)) continue;

            var url = wikis.JapaneseUrl($"{category}/{task.Name}");
            if (!(await wikis.GetAsync(url, ct).ConfigureAwait(false)).Exists) continue;

            task.JapaneseUrl = url;
            found++;
        }

        Console.WriteLine($"  {found} Japanese pages found by asking for them directly");
    }

    /// <summary>MediaWiki takes fifty titles per request.</summary>
    private const int Batch = 50;

    [GeneratedRegex(@"(?im)^\s*\|\s*location\s*=\s*(.*)$")]
    private static partial Regex LocationLine();

    [GeneratedRegex(@"(?im)^\s*\|\s*given by\s*=\s*(.*)$")]
    private static partial Regex GivenByLine();

    [GeneratedRegex(@"\[\[([^\]\|#]+)")]
    private static partial Regex WikiLink();

    /// <summary>
    /// Where each task is done, from the infobox line the English page carries:
    /// "|location =[[Woods]], [[Ground Zero]], [[Interchange]], [[Customs]]".
    ///
    /// A task can name several maps, and many name none - they are finished at
    /// a trader, or anywhere at all - so this is a list that is often empty
    /// rather than a single value with a blank in it.
    ///
    /// Read from the English wiki because fifty pages come back in one request
    /// there; the Japanese pages say the same thing one page at a time, which
    /// is nine hundred requests into a wiki that rate limits.
    /// </summary>
    public static async Task AddMapsAsync(
        IReadOnlyList<WikiEntry> tasks, IReadOnlyList<string> maps, Wikis wikis,
        CancellationToken ct = default)
    {
        var byTitle = new Dictionary<string, List<WikiEntry>>(StringComparer.Ordinal);

        foreach (var task in tasks)
        {
            if (TitleOf(task.EnglishUrl) is not { } title) continue;

            if (!byTitle.TryGetValue(title, out var list)) byTitle[title] = list = new List<WikiEntry>();
            list.Add(task);
        }

        if (byTitle.Count == 0) return;

        Console.WriteLine($"  reading {byTitle.Count} English pages for the maps they name");

        var placed = 0;

        foreach (var batch in byTitle.Keys.Chunk(Batch))
        {
            var url = $"{Wikis.FandomApi}?action=query&prop=revisions&rvprop=content&rvslots=main"
                      + $"&titles={Uri.EscapeDataString(string.Join('|', batch))}"
                      + "&format=json&formatversion=2";

            if (await wikis.GetAsync(url, ct).ConfigureAwait(false) is not { Text: { } json }) continue;

            using var document = System.Text.Json.JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("query", out var query)
                || !query.TryGetProperty("pages", out var pages))
            {
                continue;
            }

            foreach (var page in pages.EnumerateArray())
            {
                if (page.TryGetProperty("missing", out _)) continue;
                if (!page.TryGetProperty("revisions", out var revisions) || revisions.GetArrayLength() == 0)
                    continue;

                var title = page.GetProperty("title").GetString();
                var text = revisions[0].GetProperty("slots").GetProperty("main")
                    .GetProperty("content").GetString();

                if (title is null || text is null || !byTitle.TryGetValue(title, out var entries)) continue;

                // Who hands it out, for the tasks the Japanese index never
                // listed - those exist only on the English wiki, and until now
                // they sat in the catalog with no trader at all.
                if (GivenByLine().Match(text) is { Success: true } giver)
                {
                    var trader = WikiLink().Match(giver.Groups[1].Value) is { Success: true } who
                        ? who.Groups[1].Value.Trim()
                        : giver.Groups[1].Value.Trim();

                    if (trader.Length > 0)
                    {
                        foreach (var entry in entries)
                            if (entry.Group.Length == 0) entry.Group = trader;
                    }
                }

                var line = LocationLine().Match(text);
                if (!line.Success) continue;

                var named = new List<string>();

                foreach (Match link in WikiLink().Matches(line.Groups[1].Value))
                {
                    var target = link.Groups[1].Value.Trim();

                    foreach (var map in maps)
                    {
                        if (string.Equals(map, target, StringComparison.OrdinalIgnoreCase)
                            && !named.Contains(map))
                        {
                            named.Add(map);
                        }
                    }
                }

                if (named.Count == 0) continue;

                foreach (var entry in entries) entry.Maps = named;
                placed += entries.Count;
            }
        }

        Console.WriteLine($"  {placed} tasks placed on a map, "
                          + $"{tasks.Count(t => t.Group.Length > 0)} of {tasks.Count} with a trader");
    }

    [GeneratedRegex(@"(?im)^\s*\|\s*previous[ \t]*=[ \t]*(.*)$")]
    private static partial Regex PreviousLine();

    [GeneratedRegex(@"(?im)^\s*\|\s*leads to[ \t]*=[ \t]*(.*)$")]
    private static partial Regex LeadsToLine();

    /// <summary>
    /// Which tasks come before and after each one, from the infobox lines the
    /// English page carries: "|previous =[[Hidden Route]]" and
    /// "|leads to =[[Extending the Operation]]".
    ///
    /// Only links to tasks in the catalog are kept. The lines also name loyalty
    /// levels and items now and then, and those are not somewhere to go next.
    ///
    /// Blanks are matched on their own line: "\s*" after the "=" would run
    /// on into the next line and read "|leads to" as the previous task.
    ///
    /// Each side is completed from the other: a page that forgets its
    /// "leads to" is still named as "previous" by the task after it, and the
    /// two pages are not always edited together.
    /// </summary>
    public static async Task AddChainAsync(IReadOnlyList<WikiEntry> tasks, Wikis wikis, CancellationToken ct = default)
    {
        var byTitle = new Dictionary<string, WikiEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var task in tasks)
        {
            if (TitleOf(task.EnglishUrl) is { } title) byTitle.TryAdd(title, task);
            byTitle.TryAdd(task.Name, task);
        }

        WikiEntry? TaskAt(string link)
        {
            var title = link.Replace('_', ' ').Trim();
            return byTitle.TryGetValue(title, out var found) ? found : null;
        }

        var previous = tasks.ToDictionary(t => t, _ => new List<WikiEntry>());
        var next = tasks.ToDictionary(t => t, _ => new List<WikiEntry>());

        static void Link(List<WikiEntry> list, WikiEntry task)
        {
            if (!list.Contains(task)) list.Add(task);
        }

        var titles = tasks.Select(t => TitleOf(t.EnglishUrl)).OfType<string>().Distinct().ToList();
        Console.WriteLine($"  reading {titles.Count} English pages for the tasks before and after");

        foreach (var batch in titles.Chunk(Batch))
        {
            var url = $"{Wikis.FandomApi}?action=query&prop=revisions&rvprop=content&rvslots=main"
                      + $"&titles={Uri.EscapeDataString(string.Join('|', batch))}"
                      + "&format=json&formatversion=2";

            if (await wikis.GetAsync(url, ct).ConfigureAwait(false) is not { Text: { } json }) continue;

            using var document = System.Text.Json.JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("query", out var query)
                || !query.TryGetProperty("pages", out var pages))
            {
                continue;
            }

            foreach (var page in pages.EnumerateArray())
            {
                if (page.TryGetProperty("missing", out _)) continue;
                if (!page.TryGetProperty("revisions", out var revisions) || revisions.GetArrayLength() == 0)
                    continue;

                var title = page.GetProperty("title").GetString();
                var text = revisions[0].GetProperty("slots").GetProperty("main")
                    .GetProperty("content").GetString();

                if (title is null || text is null || TaskAt(title) is not { } task) continue;

                if (PreviousLine().Match(text) is { Success: true } before)
                {
                    foreach (Match link in WikiLink().Matches(before.Groups[1].Value))
                    {
                        if (TaskAt(link.Groups[1].Value) is not { } other || other == task) continue;
                        Link(previous[task], other);
                        Link(next[other], task);
                    }
                }

                if (LeadsToLine().Match(text) is { Success: true } after)
                {
                    foreach (Match link in WikiLink().Matches(after.Groups[1].Value))
                    {
                        if (TaskAt(link.Groups[1].Value) is not { } other || other == task) continue;
                        Link(next[task], other);
                        Link(previous[other], task);
                    }
                }
            }
        }

        foreach (var task in tasks)
        {
            task.Previous = previous[task].Select(t => t.Name).ToList();
            task.Next = next[task].Select(t => t.Name).ToList();
        }

        Console.WriteLine($"  {tasks.Count(t => t.Previous.Count > 0 || t.Next.Count > 0)} of {tasks.Count} tasks "
                          + "linked to a task before or after");
    }

    /// <summary>The objectives, which are the last place a map is named.</summary>
    [GeneratedRegex(@"(?is)==\s*Objectives\s*==(.*?)(?=\r?\n==)")]
    private static partial Regex ObjectivesSection();

    /// <summary>
    /// The maps named in the objectives themselves - "Eliminate Scavs on
    /// [[Woods]]" - for the tasks neither infobox nor Japanese table places.
    ///
    /// Last of the three, and deliberately so: a map named in passing is a
    /// weaker claim than a map named in the box that exists to say where the
    /// task is done. It covers eight tasks; the rest genuinely have no map.
    /// </summary>
    public static async Task AddMapsFromObjectivesAsync(
        IReadOnlyList<WikiEntry> tasks, IReadOnlyList<string> maps, Wikis wikis,
        CancellationToken ct = default)
    {
        var rest = tasks.Where(t => t.Maps.Count == 0).ToList();
        if (rest.Count == 0) return;

        var byTitle = new Dictionary<string, List<WikiEntry>>(StringComparer.Ordinal);

        foreach (var task in rest)
        {
            if (TitleOf(task.EnglishUrl) is not { } title) continue;

            if (!byTitle.TryGetValue(title, out var list)) byTitle[title] = list = new List<WikiEntry>();
            list.Add(task);
        }

        if (byTitle.Count == 0) return;

        var placed = 0;

        foreach (var batch in byTitle.Keys.Chunk(Batch))
        {
            var url = $"{Wikis.FandomApi}?action=query&prop=revisions&rvprop=content&rvslots=main"
                      + $"&titles={Uri.EscapeDataString(string.Join('|', batch))}"
                      + "&format=json&formatversion=2";

            if (await wikis.GetAsync(url, ct).ConfigureAwait(false) is not { Text: { } json }) continue;

            using var document = System.Text.Json.JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("query", out var query)
                || !query.TryGetProperty("pages", out var pages))
            {
                continue;
            }

            foreach (var page in pages.EnumerateArray())
            {
                if (page.TryGetProperty("missing", out _)) continue;
                if (!page.TryGetProperty("revisions", out var revisions) || revisions.GetArrayLength() == 0)
                    continue;

                var title = page.GetProperty("title").GetString();
                var text = revisions[0].GetProperty("slots").GetProperty("main")
                    .GetProperty("content").GetString();

                if (title is null || text is null || !byTitle.TryGetValue(title, out var entries)) continue;

                var objectives = ObjectivesSection().Match(text);

                if (Environment.GetEnvironmentVariable("CATALOG_DEBUG") is not null)
                    Console.WriteLine($"    [{title}] objectives={objectives.Success} len={text.Length}");

                if (!objectives.Success) continue;

                var named = new List<string>();

                foreach (Match link in WikiLink().Matches(objectives.Groups[1].Value))
                {
                    var target = link.Groups[1].Value.Trim();

                    foreach (var map in maps)
                    {
                        if (string.Equals(map, target, StringComparison.OrdinalIgnoreCase)
                            && !named.Contains(map))
                        {
                            named.Add(map);
                        }
                    }
                }

                if (named.Count == 0) continue;

                foreach (var entry in entries) entry.Maps = named;
                placed += entries.Count;
            }
        }

        Console.WriteLine($"  {placed} more from the maps their objectives name");
    }

    /// <summary>The "マップ" row of the data table the Japanese page opens with,
    /// and the pages it links to.</summary>
    [GeneratedRegex(@"<td>\s*マップ\s*</td>\s*<td>(.*?)</td>", RegexOptions.Singleline)]
    private static partial Regex JapaneseMapRow();

    [GeneratedRegex(@"href=""/eft/([^""#?]+)""")]
    private static partial Regex JapaneseLinkTarget();

    /// <summary>
    /// The maps the Japanese page names, for the tasks the English one does
    /// not. Its infobox leaves the location blank more often than the Japanese
    /// table leaves マップ blank - "Hunting Trip" says nothing in English and
    /// WOODS in Japanese - and some tasks have no English page at all.
    ///
    /// One request per page, so it is only asked about tasks still without a
    /// map: a few hundred rather than nine hundred.
    /// </summary>
    public static async Task AddMapsFromJapaneseAsync(
        IReadOnlyList<WikiEntry> tasks, IReadOnlyList<string> maps, Wikis wikis,
        CancellationToken ct = default)
    {
        var rest = tasks.Where(t => t.Maps.Count == 0 && t.JapaneseUrl is not null).ToList();
        if (rest.Count == 0) return;

        Console.WriteLine($"  asking the Japanese wiki about {rest.Count} without a map");

        var placed = 0;

        foreach (var task in rest)
        {
            var page = await wikis.GetAsync(task.JapaneseUrl!, ct).ConfigureAwait(false);
            if (page.Text is not { } html) continue;

            var row = JapaneseMapRow().Match(html);
            if (!row.Success) continue;

            var named = new List<string>();

            foreach (Match link in JapaneseLinkTarget().Matches(row.Groups[1].Value))
            {
                var target = WebUtility.UrlDecode(link.Groups[1].Value).Trim();

                foreach (var map in maps)
                {
                    if (string.Equals(map, target, StringComparison.OrdinalIgnoreCase)
                        && !named.Contains(map))
                    {
                        named.Add(map);
                    }
                }
            }

            if (named.Count == 0) continue;

            task.Maps = named;
            placed++;
        }

        Console.WriteLine($"  {placed} more placed on a map from the Japanese wiki");
    }

    /// <summary>The page title inside a Fandom link, as the API wants it.</summary>
    private static string? TitleOf(string? url)
    {
        if (url is null || !url.StartsWith(Wikis.FandomWiki, StringComparison.Ordinal)) return null;

        var title = WebUtility.UrlDecode(url[Wikis.FandomWiki.Length..]).Replace('_', ' ').Trim();
        return title.Length == 0 ? null : title;
    }

    /// <summary>
    /// wikiwiki files an event task under its trader with the event in the
    /// title - "Mechanic/KORD BREACH Break the Chain" - and links it from
    /// nowhere at all, so no amount of crawling finds it. Once the event and
    /// the trader are known the page name is, too, so it is simply asked for.
    ///
    /// Only for event tasks that have no Japanese page yet: fifteen of the
    /// nineteen in KORD BREACH, at one request each. The other four are not
    /// written yet and answer 404.
    /// </summary>
    private static async Task FindJapaneseEventPagesAsync(
        Wikis wikis, IEnumerable<WikiEntry> entries, CancellationToken ct)
    {
        var found = 0;

        foreach (var entry in entries)
        {
            if (entry.Event.Length == 0 || entry.JapaneseUrl is not null) continue;
            if (!JapaneseCategories.Contains(entry.Group)) continue;

            var page = $"{entry.Group}/{entry.Event} {entry.Name}";
            var url = wikis.JapaneseUrl(page);

            if (!(await wikis.GetAsync(url, ct).ConfigureAwait(false)).Exists) continue;

            entry.JapaneseUrl = url;
            found++;
            Console.WriteLine($"  event page: {page}");
        }

        Console.WriteLine($"  {found} event tasks also have a Japanese page");
    }

    private static async Task<List<(string Trader, string Name, string Url)>> JapaneseAsync(
        Wikis wikis, CancellationToken ct)
    {
        var found = new List<(string, string, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var page in JapaneseIndexPages)
        {
            if (await wikis.GetAsync(wikis.JapaneseUrl(page), ct).ConfigureAwait(false)
                is not { Text: { } html }) continue;

            foreach (Match match in JapaneseLink().Matches(html))
            {
                var href = match.Groups[1].Value;
                var title = WebUtility.HtmlDecode(match.Groups[2].Value);

                var slash = title.IndexOf('/');
                if (slash <= 0 || slash == title.Length - 1) continue;

                var category = title[..slash];
                var name = title[(slash + 1)..];

                if (!JapaneseCategories.Contains(category)) continue;
                if (Naming.ContainsJapanese(name)) continue;
                if (!seen.Add(title)) continue;

                var trader = category == StoryCategoryJapanese ? StoryTrader : category;
                found.Add((trader, WithoutEventPrefix(name), Wikis.JapaneseOrigin + href));
            }
        }

        foreach (var title in JapaneseExtras)
        {
            if (!seen.Add(title)) continue;

            var slash = title.IndexOf('/');
            if (slash <= 0 || slash == title.Length - 1) continue;

            var category = title[..slash];
            var name = title[(slash + 1)..];
            if (!JapaneseCategories.Contains(category)) continue;

            var url = wikis.JapaneseUrl(title);
            if (!(await wikis.GetAsync(url, ct).ConfigureAwait(false)).Exists)
            {
                Console.Error.WriteLine($"  extra page is gone, skipping: {title}");
                continue;
            }

            Console.WriteLine($"  extra: {title}");
            var trader = category == StoryCategoryJapanese ? StoryTrader : category;
            found.Add((trader, WithoutEventPrefix(name), url));
        }

        return found;
    }

    private static async Task<List<(string Name, string Url)>> EnglishAsync(
        Wikis wikis, CancellationToken ct)
    {
        var found = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var category in FandomCategories)
        {
            foreach (var title in await wikis.CategoryMembersAsync(category, ct).ConfigureAwait(false))
            {
                if (FandomExcluded.Contains(title)) continue;

                // A category holds other categories too - "Category:Community
                // Goal Quests" turned up among the quests - and those are not tasks.
                if (title.StartsWith("Category:", StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Add(title)) continue;

                var name = FandomQualifier().Replace(title, "").Trim();
                if (name.Length == 0) continue;

                found.Add((name, Wikis.FandomWiki + Wikis.EncodeWikiTitle(title)));
            }
        }

        return found;
    }
}
