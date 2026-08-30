using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace HeyTarkov.CatalogBuilder;

/// <summary>
/// Regenerates src/HeyTarkov/tasks.json from wikiwiki.jp and the Fandom wiki.
///
/// Run by the maintainer, by hand, when the task list needs refreshing:
///
///     dotnet run --project tools/CatalogBuilder
///
/// The application itself never does this - it ships the generated file and
/// makes no wiki requests at runtime.
/// </summary>
internal static class Program
{
    private const string JapaneseOrigin = "https://wikiwiki.jp";

    /// <summary>Trader tasks, then story tasks - two index pages on wikiwiki.</summary>
    private static readonly string[] JapaneseIndexPages =
    {
        "/eft/%3A%E3%82%BF%E3%82%B9%E3%82%AF%E4%B8%80%E8%A6%A7",                          // :タスク一覧
        "/eft/%E3%82%B9%E3%83%88%E3%83%BC%E3%83%AA%E3%83%BC%E3%82%BF%E3%82%B9%E3%82%AF",  // ストーリータスク
    };

    private const string FandomApi = "https://escapefromtarkov.fandom.com/api.php";
    private const string FandomWiki = "https://escapefromtarkov.fandom.com/wiki/";

    private static readonly string[] FandomCategories =
    {
        "Category:Quests",
        "Category:Story chapters",
    };

    private static readonly HashSet<string> FandomExcluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "Story chapters", "Quests",
    };

    private static readonly Regex JapaneseLinkPattern = new(
        """href="(/eft/[^"]+)" title="([^"]+)" class="rel-wiki-page""",
        RegexOptions.Compiled);

    private static readonly Regex FandomQualifier = new(
        @"\s*\((?:story chapter|quest)\)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> JapaneseCategories = new(StringComparer.Ordinal)
    {
        "Prapor", "Therapist", "Fence", "Skier", "Peacekeeper", "Mechanic",
        "Ragman", "Jaeger", "Ref", "Lightkeeper", "BTR Driver", "Arena",
        "ストーリータスク",
    };

    private const string StoryCategoryJapanese = "ストーリータスク";
    private const string StoryTrader = "Story";

    /// <summary>Spacing between requests, so a rebuild never looks like a crawl.</summary>
    private static readonly TimeSpan PoliteDelay = TimeSpan.FromSeconds(1);

    private static async Task<int> Main(string[] args)
    {
        var output = args.Length > 0
            ? args[0]
            : Path.Combine("src", "HeyTarkov", "tasks.json");

        try
        {
            using var http = CreateClient();

            Console.WriteLine("wikiwiki.jp ...");
            var japanese = await FetchJapaneseAsync(http);
            Console.WriteLine($"  {japanese.Count} tasks");

            Console.WriteLine("escapefromtarkov.fandom.com ...");
            var english = await FetchEnglishAsync(http);
            Console.WriteLine($"  {english.Count} tasks");

            if (japanese.Count == 0 && english.Count == 0)
            {
                Console.Error.WriteLine("Neither wiki returned anything; leaving the file alone.");
                return 1;
            }

            var catalog = new CatalogFile
            {
                UpdatedAt = DateTimeOffset.Now,
                Tasks = Merge(japanese, english),
            };

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(catalog,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                }));

            Console.WriteLine();
            Console.WriteLine($"wrote {output}");
            Console.WriteLine($"  {catalog.Tasks.Count} tasks");
            Console.WriteLine($"  {catalog.Tasks.Count(t => t.JapaneseUrl is not null)} on wikiwiki.jp");
            Console.WriteLine($"  {catalog.Tasks.Count(t => t.EnglishUrl is not null)} on the Fandom wiki");
            Console.WriteLine();
            Console.WriteLine("Rebuild the app so the new catalog is embedded, then run");
            Console.WriteLine("  HeyTarkov.exe --selftest");
            Console.WriteLine("to see whether any new task names need katakana readings.");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };

        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(45) };

        // Identify honestly: a descriptive agent with a contact link, as
        // MediaWiki's User-Agent policy asks for.
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "HeyTarkovCatalogBuilder/1.0 (+https://github.com/capycappy/HeyTarkov)");

        return http;
    }

    private static List<TaskEntry> Merge(
        List<(string Trader, string Name, string Url)> japanese,
        List<(string Name, string Url)> english)
    {
        var byName = new Dictionary<string, TaskEntry>(StringComparer.Ordinal);

        foreach (var (trader, name, url) in japanese)
        {
            var key = Normalize(name);
            if (key.Length == 0) continue;

            if (!byName.TryGetValue(key, out var entry))
            {
                entry = new TaskEntry { Name = name, Trader = trader };
                byName[key] = entry;
            }

            entry.JapaneseUrl ??= url;
        }

        foreach (var (name, url) in english)
        {
            var key = Normalize(name);
            if (key.Length == 0) continue;

            if (!byName.TryGetValue(key, out var entry))
            {
                entry = new TaskEntry { Name = name, Trader = "" };
                byName[key] = entry;
            }

            entry.EnglishUrl ??= url;
        }

        return byName.Values
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Must match HeyTarkov.SpokenForms.Normalize closely enough to pair the
    /// same names across the two wikis.
    /// </summary>
    private static string Normalize(string text)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var currentIsDigit = false;

        void Flush()
        {
            if (current.Length == 0) return;
            tokens.Add(current.ToString().ToLowerInvariant());
            current.Clear();
        }

        foreach (var raw in text)
        {
            var c = raw switch
            {
                '’' or '‘' => '\'',
                '—' or '–' => '-',
                _ => raw,
            };

            if (char.IsWhiteSpace(c) || c is '-' or '_' or '/' or '\\')
            {
                Flush();
                currentIsDigit = false;
                continue;
            }

            if (char.IsDigit(c))
            {
                if (current.Length > 0 && !currentIsDigit) Flush();
                currentIsDigit = true;
                current.Append(c);
                continue;
            }

            if (char.IsLetter(c) || c == '\'')
            {
                if (current.Length > 0 && currentIsDigit) Flush();
                currentIsDigit = false;
                current.Append(c);
            }
        }

        Flush();
        return string.Join(' ', tokens);
    }

    private static async Task<List<(string Trader, string Name, string Url)>> FetchJapaneseAsync(
        HttpClient http)
    {
        var found = new List<(string, string, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var first = true;

        foreach (var page in JapaneseIndexPages)
        {
            if (!first) await Task.Delay(PoliteDelay);
            first = false;

            string html;
            try
            {
                html = await http.GetStringAsync(JapaneseOrigin + page);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  {page}: {ex.Message}");
                continue;
            }

            foreach (Match match in JapaneseLinkPattern.Matches(html))
            {
                var href = match.Groups[1].Value;
                var title = WebUtility.HtmlDecode(match.Groups[2].Value);

                var slash = title.IndexOf('/');
                if (slash <= 0 || slash == title.Length - 1) continue;

                var category = title[..slash];
                var name = title[(slash + 1)..];

                if (!JapaneseCategories.Contains(category)) continue;
                if (ContainsJapanese(name)) continue;
                if (!seen.Add(title)) continue;

                var trader = category == StoryCategoryJapanese ? StoryTrader : category;
                found.Add((trader, name, JapaneseOrigin + href));
            }
        }

        return found;
    }

    private static async Task<List<(string Name, string Url)>> FetchEnglishAsync(HttpClient http)
    {
        var found = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var category in FandomCategories)
        {
            string? continuation = null;

            do
            {
                if (found.Count > 0) await Task.Delay(PoliteDelay);

                var url = $"{FandomApi}?action=query&list=categorymembers"
                          + $"&cmtitle={Uri.EscapeDataString(category)}"
                          + "&cmlimit=500&cmnamespace=0&format=json"
                          + (continuation is null
                              ? ""
                              : $"&cmcontinue={Uri.EscapeDataString(continuation)}");

                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(await http.GetStringAsync(url));
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  {category}: {ex.Message}");
                    break;
                }

                using (document)
                {
                    var root = document.RootElement;

                    if (root.TryGetProperty("query", out var query)
                        && query.TryGetProperty("categorymembers", out var members))
                    {
                        foreach (var member in members.EnumerateArray())
                        {
                            var title = member.GetProperty("title").GetString();
                            if (string.IsNullOrWhiteSpace(title)) continue;
                            if (FandomExcluded.Contains(title)) continue;
                            if (!seen.Add(title)) continue;

                            var name = FandomQualifier.Replace(title, "").Trim();
                            if (name.Length == 0) continue;

                            found.Add((name, FandomWiki + EncodeWikiTitle(title)));
                        }
                    }

                    continuation = root.TryGetProperty("continue", out var cont)
                                   && cont.TryGetProperty("cmcontinue", out var value)
                        ? value.GetString()
                        : null;
                }
            }
            while (continuation is not null);
        }

        return found;
    }

    /// <summary>MediaWiki titles keep characters EscapeDataString would encode;
    /// leaving them avoids a redirect on every open.</summary>
    private static string EncodeWikiTitle(string title) =>
        Uri.EscapeDataString(title.Replace(' ', '_'))
            .Replace("%28", "(")
            .Replace("%29", ")")
            .Replace("%27", "'")
            .Replace("%2C", ",")
            .Replace("%21", "!")
            .Replace("%3A", ":");

    private static bool ContainsJapanese(string s)
    {
        foreach (var c in s)
        {
            if (c is >= '　' and <= 'ヿ') return true;
            if (c is >= '一' and <= '鿿') return true;
            if (c is >= '＀' and <= '￯') return true;
        }

        return false;
    }
}

internal sealed class TaskEntry
{
    public string Trader { get; set; } = "";
    public string Name { get; set; } = "";
    public string? JapaneseUrl { get; set; }
    public string? EnglishUrl { get; set; }
}

internal sealed class CatalogFile
{
    public DateTimeOffset UpdatedAt { get; set; }
    public List<TaskEntry> Tasks { get; set; } = new();
}
