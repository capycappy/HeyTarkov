using System.Text.Json;
using System.Text.RegularExpressions;

namespace HeyTarkov.CatalogBuilder;

/// <summary>
/// Which tasks belong to a seasonal event, and which event.
///
/// A seasonal task says so in its own wikitext, by linking to the season's
/// heading: "[[Seasons#Season 1: KORD BREACH|Seasonal mode]]". That link is the
/// only machine-readable marker either wiki carries - the Fandom category is
/// just Quests, and wikiwiki's page title prefix exists on some pages and not
/// others - so the event list is read from it rather than written down here.
/// A new season needs no change to this file.
///
/// Finding them without reading 870 pages: everything that links to Seasons is
/// one query, and the handful of those that are quests is a second. Only those
/// are read in full.
/// </summary>
public static partial class Seasons
{
    private const string SeasonsPage = "Seasons";
    private const string QuestCategory = "Category:Quests";

    /// <summary>The API takes 50 titles at a time; 45 leaves room to spare.</summary>
    private const int BatchSize = 45;

    [GeneratedRegex(@"\[\[Seasons#Season\s*\d+\s*:\s*([^|\]]+)")]
    private static partial Regex SeasonLink();

    /// <summary>Task name, normalized, to the event it belongs to.</summary>
    public static async Task<Dictionary<string, string>> FetchAsync(
        Wikis wikis, CancellationToken ct = default)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        var linking = await BacklinksAsync(wikis, ct).ConfigureAwait(false);
        if (linking.Count == 0) return found;

        var quests = await QuestsAmongAsync(wikis, linking, ct).ConfigureAwait(false);
        Console.WriteLine($"  {linking.Count} pages link to {SeasonsPage}; {quests.Count} are tasks");

        foreach (var batch in Batches(quests))
        {
            var url = $"{Wikis.FandomApi}?action=query&prop=revisions&rvprop=content&rvslots=main"
                      + $"&titles={Uri.EscapeDataString(string.Join('|', batch))}&format=json";

            var json = await wikis.GetAsync(url, ct).ConfigureAwait(false);
            if (json is null) continue;

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("query", out var query)
                || !query.TryGetProperty("pages", out var pages))
            {
                continue;
            }

            foreach (var page in pages.EnumerateObject())
            {
                var title = page.Value.GetProperty("title").GetString();
                if (title is null) continue;

                if (!page.Value.TryGetProperty("revisions", out var revisions)
                    || revisions.GetArrayLength() == 0)
                {
                    continue;
                }

                var text = revisions[0].GetProperty("slots").GetProperty("main")
                    .GetProperty("*").GetString() ?? "";

                var marker = SeasonLink().Match(text);
                if (!marker.Success) continue;

                var name = marker.Groups[1].Value.Trim();
                if (name.Length == 0) continue;

                found[Naming.Normalize(title)] = name;
            }
        }

        foreach (var group in found.GroupBy(p => p.Value).OrderBy(g => g.Key, StringComparer.Ordinal))
            Console.WriteLine($"    {group.Key}: {group.Count()} tasks");

        return found;
    }

    /// <summary>Every page that links to the Seasons page.</summary>
    private static async Task<List<string>> BacklinksAsync(Wikis wikis, CancellationToken ct)
    {
        var titles = new List<string>();
        string? continuation = null;

        do
        {
            var url = $"{Wikis.FandomApi}?action=query&list=backlinks"
                      + $"&bltitle={Uri.EscapeDataString(SeasonsPage)}"
                      + "&blnamespace=0&bllimit=500&format=json"
                      + (continuation is null ? "" : $"&blcontinue={Uri.EscapeDataString(continuation)}");

            var json = await wikis.GetAsync(url, ct).ConfigureAwait(false);
            if (json is null) break;

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.TryGetProperty("query", out var query)
                && query.TryGetProperty("backlinks", out var links))
            {
                foreach (var link in links.EnumerateArray())
                {
                    var title = link.GetProperty("title").GetString();
                    if (!string.IsNullOrWhiteSpace(title)) titles.Add(title);
                }
            }

            continuation = root.TryGetProperty("continue", out var cont)
                           && cont.TryGetProperty("blcontinue", out var value)
                ? value.GetString()
                : null;
        }
        while (continuation is not null);

        return titles;
    }

    /// <summary>Of those pages, the ones that are actually tasks.</summary>
    private static async Task<List<string>> QuestsAmongAsync(
        Wikis wikis, List<string> titles, CancellationToken ct)
    {
        var quests = new List<string>();

        foreach (var batch in Batches(titles))
        {
            var url = $"{Wikis.FandomApi}?action=query&prop=categories"
                      + $"&titles={Uri.EscapeDataString(string.Join('|', batch))}"
                      + "&cllimit=500&format=json";

            var json = await wikis.GetAsync(url, ct).ConfigureAwait(false);
            if (json is null) continue;

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("query", out var query)
                || !query.TryGetProperty("pages", out var pages))
            {
                continue;
            }

            foreach (var page in pages.EnumerateObject())
            {
                if (!page.Value.TryGetProperty("categories", out var categories)) continue;

                var isQuest = categories.EnumerateArray().Any(c =>
                    c.GetProperty("title").GetString() == QuestCategory);

                var title = page.Value.GetProperty("title").GetString();
                if (isQuest && title is not null) quests.Add(title);
            }
        }

        return quests;
    }

    private static IEnumerable<List<string>> Batches(List<string> titles)
    {
        for (var i = 0; i < titles.Count; i += BatchSize)
            yield return titles.GetRange(i, Math.Min(BatchSize, titles.Count - i));
    }
}
