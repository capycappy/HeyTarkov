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

        return byKey.Values.ToList();
    }

    private static async Task<List<(string Trader, string Name, string Url)>> JapaneseAsync(
        Wikis wikis, CancellationToken ct)
    {
        var found = new List<(string, string, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var page in JapaneseIndexPages)
        {
            var html = await wikis.GetAsync(wikis.JapaneseUrl(page), ct).ConfigureAwait(false);
            if (html is null) continue;

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
                found.Add((trader, name, Wikis.JapaneseOrigin + href));
            }
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
