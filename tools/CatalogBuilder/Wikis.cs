using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HeyTarkov.CatalogBuilder;

/// <summary>
/// Reads both wikis. Everything here runs on the maintainer's machine, by hand;
/// the application ships the generated file and never makes these requests.
/// </summary>
public sealed partial class Wikis : IDisposable
{
    public const string JapaneseOrigin = "https://wikiwiki.jp";
    public const string FandomApi = "https://escapefromtarkov.fandom.com/api.php";
    public const string FandomWiki = "https://escapefromtarkov.fandom.com/wiki/";

    /// <summary>Spacing between requests, so a rebuild never looks like a crawl.</summary>
    private static readonly TimeSpan PoliteDelay = TimeSpan.FromSeconds(1.2);

    /// <summary>wikiwiki.jp runs a bot check that a burst can trip.</summary>
    private const string BotCheckMarker = "アクセス確認中";

    private readonly HttpClient _http;
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;

    public Wikis()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };

        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(45) };

        // Identify honestly, as MediaWiki's User-Agent policy asks.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "HeyTarkovCatalogBuilder/1.0 (+https://github.com/capycappy/HeyTarkov)");
    }

    public void Dispose() => _http.Dispose();

    // ------------------------------------------------------------- transport

    public async Task<string?> GetAsync(string url, CancellationToken ct = default)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await PaceAsync(ct).ConfigureAwait(false);

            string body;
            try
            {
                body = await _http.GetStringAsync(url, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"    {url}: {ex.Message}");
                return null;
            }

            if (!body.Contains(BotCheckMarker, StringComparison.Ordinal)) return body;

            Console.WriteLine("    bot check hit, backing off...");
            await Task.Delay(TimeSpan.FromSeconds(12), ct).ConfigureAwait(false);
        }

        Console.Error.WriteLine($"    {url}: bot check kept firing");
        return null;
    }

    private async Task PaceAsync(CancellationToken ct)
    {
        var since = DateTimeOffset.Now - _lastRequest;
        if (since < PoliteDelay) await Task.Delay(PoliteDelay - since, ct).ConfigureAwait(false);
        _lastRequest = DateTimeOffset.Now;
    }

    public string JapaneseUrl(string page) => $"{JapaneseOrigin}/eft/{Uri.EscapeDataString(page)}";

    /// <summary>
    /// MediaWiki titles keep characters EscapeDataString would encode; leaving
    /// them avoids a redirect on every open.
    /// </summary>
    public static string EncodeWikiTitle(string title) =>
        Uri.EscapeDataString(title.Replace(' ', '_'))
            .Replace("%28", "(")
            .Replace("%29", ")")
            .Replace("%27", "'")
            .Replace("%2C", ",")
            .Replace("%21", "!")
            .Replace("%3A", ":");

    // --------------------------------------------------------------- fandom

    /// <summary>Page titles in a Fandom category, following continuations.</summary>
    public async Task<List<string>> CategoryMembersAsync(string category, CancellationToken ct = default)
    {
        var titles = new List<string>();
        string? continuation = null;

        do
        {
            var url = $"{FandomApi}?action=query&list=categorymembers"
                      + $"&cmtitle={Uri.EscapeDataString(category)}"
                      + "&cmlimit=500&cmnamespace=0&format=json"
                      + (continuation is null
                          ? ""
                          : $"&cmcontinue={Uri.EscapeDataString(continuation)}");

            var json = await GetAsync(url, ct).ConfigureAwait(false);
            if (json is null) break;

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.TryGetProperty("query", out var query)
                && query.TryGetProperty("categorymembers", out var members))
            {
                foreach (var member in members.EnumerateArray())
                {
                    var title = member.GetProperty("title").GetString();
                    if (!string.IsNullOrWhiteSpace(title)) titles.Add(title);
                }
            }

            continuation = root.TryGetProperty("continue", out var cont)
                           && cont.TryGetProperty("cmcontinue", out var value)
                ? value.GetString()
                : null;
        }
        while (continuation is not null);

        return titles;
    }

    /// <summary>Section anchors of a Fandom page, keyed by heading text.</summary>
    public async Task<Dictionary<string, string>> SectionsAsync(
        string title, CancellationToken ct = default)
    {
        var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var url = $"{FandomApi}?action=parse&page={Uri.EscapeDataString(title)}"
                  + "&prop=sections&format=json";

        var json = await GetAsync(url, ct).ConfigureAwait(false);
        if (json is null) return sections;

        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("parse", out var parse)
            || !parse.TryGetProperty("sections", out var list))
        {
            return sections;
        }

        foreach (var section in list.EnumerateArray())
        {
            var line = section.GetProperty("line").GetString();
            var anchor = section.GetProperty("anchor").GetString();
            if (line is null || anchor is null) continue;
            sections.TryAdd(line, anchor);
        }

        return sections;
    }

    // ------------------------------------------------------------- wikiwiki

    /// <summary>A heading on a wikiwiki page, with the anchor the wiki's own
    /// table of contents links to.</summary>
    public readonly record struct Heading(int Level, string Text, string Anchor);

    /// <summary>
    /// Headings are matched as paired tags. Scanning forward for the next anchor
    /// instead lets one heading swallow everything below it.
    /// </summary>
    [GeneratedRegex(@"<h([2-4])\b[^>]*>(.*?)</h\1>", RegexOptions.Singleline)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"<a class=""anchor_super"" name\s*=""([^""]+)""")]
    private static partial Regex AnchorPattern();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagPattern();

    public static List<Heading> Headings(string html)
    {
        var found = new List<Heading>();

        foreach (Match match in HeadingPattern().Matches(html))
        {
            var inner = match.Groups[2].Value;

            var anchor = AnchorPattern().Match(inner);
            if (!anchor.Success) continue;

            var text = WebUtility.HtmlDecode(TagPattern().Replace(inner, ""));
            text = Regex.Replace(text, @"\s+", " ").Trim();
            if (text.Length == 0) continue;

            found.Add(new Heading(int.Parse(match.Groups[1].Value), text, anchor.Groups[1].Value));
        }

        return found;
    }
}
