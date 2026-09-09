using System.Text.RegularExpressions;

namespace HeyTarkov.CatalogBuilder;

/// <summary>
/// Maps and their extraction points.
///
/// The two wikis differ here in a way worth stating plainly: wikiwiki gives
/// every extract its own heading, so an extract links straight to it. The Fandom
/// wiki keeps all extracts in one table under a single "Extractions" section,
/// so the best it can offer is that section.
/// </summary>
public static partial class Maps
{
    /// <summary>
    /// wikiwiki page name paired with the Fandom title. Kept explicit because
    /// the two do not always transform into each other ("STREETS OF TARKOV" and
    /// "Streets of Tarkov" do, "THE LAB" and "The Lab" do, but relying on that
    /// silently breaks the day one of them is renamed).
    /// </summary>
    private static readonly (string Japanese, string English)[] Known =
    {
        ("GROUND ZERO", "Ground Zero"),
        ("CUSTOMS", "Customs"),
        ("FACTORY", "Factory"),
        ("WOODS", "Woods"),
        ("SHORELINE", "Shoreline"),
        ("INTERCHANGE", "Interchange"),
        ("RESERVE", "Reserve"),
        ("LIGHTHOUSE", "Lighthouse"),
        ("STREETS OF TARKOV", "Streets of Tarkov"),
        ("THE LAB", "The Lab"),
        ("THE LABYRINTH", "The Labyrinth"),
        ("ICEBREAKER", "Icebreaker"),
        ("TOWN", "Town"),
        ("SUBURBS", "Suburbs"),
        ("TERMINAL", "Terminal"),
    };

    /// <summary>h2 sections on wikiwiki whose h3 children are extraction points.</summary>
    private static readonly string[] ExtractSections = { "脱出地点", "exfil", "脱出口" };

    /// <summary>The section holding the extraction table on the Fandom wiki.</summary>
    private const string FandomExtractSection = "Extractions";

    /// <summary>Trailing "（PMC用）" / "（SCAV用）" marks which faction may use it.</summary>
    [GeneratedRegex(@"\s*[（(](PMC|SCAV)用[）)]\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex FactionSuffix();

    /// <summary>
    /// Editorial notes ride along in some headings - "(Ver1.00削除)" bracketed,
    /// "Ver1.00追加" not. They are not part of the name and cannot be spoken.
    /// </summary>
    [GeneratedRegex(@"\s*[（(][^）)]*[　-ヿ一-鿿][^）)]*[）)]\s*")]
    private static partial Regex BracketedJapaneseNote();

    /// <summary>A version marker, with or without a note attached to it.</summary>
    [GeneratedRegex(@"\s*Ver\s*[\d.]+\s*", RegexOptions.IgnoreCase)]
    private static partial Regex VersionNote();

    public static async Task<List<WikiEntry>> FetchAsync(Wikis wikis, CancellationToken ct = default)
    {
        var entries = new List<WikiEntry>();

        foreach (var (japanese, english) in Known)
        {
            Console.WriteLine($"  {japanese}");

            var japaneseUrl = wikis.JapaneseUrl(japanese);
            var englishUrl = Wikis.FandomWiki + Wikis.EncodeWikiTitle(english);

            var japanesePage = await wikis.GetAsync(japaneseUrl, ct).ConfigureAwait(false);
            var sections = await wikis.SectionsAsync(english, ct).ConfigureAwait(false);

            var onJapanese = japanesePage.Exists;
            var onEnglish = sections.Count > 0;

            if (!onJapanese && !onEnglish)
            {
                Console.Error.WriteLine("    neither wiki has this page; skipping");
                continue;
            }

            entries.Add(new WikiEntry
            {
                Kind = EntryKind.Map,
                Name = english,                       // "Ground Zero" reads better than "GROUND ZERO"
                JapaneseUrl = onJapanese ? japaneseUrl : null,
                EnglishUrl = onEnglish ? englishUrl : null,
            });

            if (japanesePage.Text is not { } html)
            {
                Console.WriteLine("    (japanese page unavailable, no extracts)");
                continue;
            }

            // The Fandom side has one anchor for the whole table, if that.
            var englishExtractUrl =
                onEnglish && sections.TryGetValue(FandomExtractSection, out var anchor)
                    ? $"{englishUrl}#{anchor}"
                    : null;

            var extracts = Extracts(html);

            foreach (var (name, faction, japaneseAnchor) in extracts)
            {
                entries.Add(new WikiEntry
                {
                    Kind = EntryKind.Extract,
                    Name = name,
                    Group = english,
                    Faction = faction,
                    JapaneseUrl = $"{japaneseUrl}#{japaneseAnchor}",
                    EnglishUrl = englishExtractUrl,
                });
            }

            Console.WriteLine($"    {extracts.Count} extracts"
                              + (englishExtractUrl is null ? " (english: no extraction section)" : ""));
        }

        return entries;
    }

    /// <summary>
    /// Strips the editorial noise off a heading. Anything from the first
    /// remaining Japanese character onwards goes too: an unbracketed note like
    /// "Ver1.00追加" is not part of the name, and Japanese left in an otherwise
    /// English name cannot be spoken by the English recognizer at all.
    /// </summary>
    private static string Clean(string name)
    {
        name = BracketedJapaneseNote().Replace(name, " ");

        // Anything from the first remaining Japanese character onwards is a
        // note, not part of the name.
        for (var i = 0; i < name.Length; i++)
        {
            if (!Naming.ContainsJapanese(name[i].ToString())) continue;
            name = name[..i];
            break;
        }

        name = VersionNote().Replace(name, " ");
        return Regex.Replace(name, @"\s+", " ").Trim();
    }

    /// <summary>h3 headings sitting under an h2 about exfils.</summary>
    private static List<(string Name, string? Faction, string Anchor)> Extracts(string html)
    {
        var found = new List<(string, string?, string)>();
        var inside = false;

        foreach (var heading in Wikis.Headings(html))
        {
            if (heading.Level == 2)
            {
                inside = ExtractSections.Any(k =>
                    heading.Text.Contains(k, StringComparison.OrdinalIgnoreCase));
                continue;
            }

            if (heading.Level != 3 || !inside) continue;

            var faction = FactionSuffix().Match(heading.Text);
            var name = faction.Success
                ? heading.Text[..faction.Index]
                : heading.Text;

            name = Clean(name);
            if (name.Length == 0) continue;

            found.Add((name, faction.Success ? faction.Groups[1].Value.ToUpperInvariant() : null,
                heading.Anchor));
        }

        return found;
    }
}
