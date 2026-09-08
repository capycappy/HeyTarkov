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
                if (!seen.Add(title)) continue;

                var name = FandomQualifier().Replace(title, "").Trim();
                if (name.Length == 0) continue;

                found.Add((name, Wikis.FandomWiki + Wikis.EncodeWikiTitle(title)));
            }
        }

        return found;
    }
}
