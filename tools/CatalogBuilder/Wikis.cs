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

    /// <summary>
    /// The slowest this will let itself get. wikiwiki.jp starts refusing well
    /// inside the polite delay, and every refusal doubles the pace for the rest
    /// of the run - but a build that crawls forever is its own kind of broken.
    /// </summary>
    private static readonly TimeSpan SlowestPace = TimeSpan.FromSeconds(10);

    /// <summary>wikiwiki.jp runs a bot check that a burst can trip.</summary>
    private const string BotCheckMarker = "アクセス確認中";

    /// <summary>
    /// How many requests must go through untouched before the pace is allowed
    /// to creep back up. Backing off is cheap; staying backed off for the
    /// remaining thousand pages is not.
    /// </summary>
    private const int CalmBeforeSpeedingUp = 20;

    private readonly HttpClient _http;
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;
    private TimeSpan _pace = PoliteDelay;
    private int _calm;

    /// <summary>
    /// Every URL this could not get an answer about. Not the ones that came
    /// back missing - the ones where the wiki refused to say. A catalog built
    /// with any of these in it is missing links it should have had, so the
    /// build refuses to write one.
    /// </summary>
    public List<string> Unresolved { get; } = new();

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

    /// <summary>
    /// Fetches a page, and says which of three things happened - because "the
    /// page is not there" and "the wiki would not tell me" have to be told
    /// apart. Reading a refusal as an absence is how a rebuild quietly drops
    /// links that were fine the day before.
    /// </summary>
    public async Task<Page> GetAsync(string url, CancellationToken ct = default)
    {
        var wait = TimeSpan.FromSeconds(20);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await PaceAsync(ct).ConfigureAwait(false);

            HttpResponseMessage response;
            try
            {
                response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // A timeout or a dropped connection says nothing about whether
                // the page exists, so it is worth another try.
                Console.Error.WriteLine($"    {url}: {ex.Message}");
                await Task.Delay(wait, ct).ConfigureAwait(false);
                wait = Longer(wait);
                continue;
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    Calm();
                    return Page.Missing;
                }

                if (Overloaded(response.StatusCode))
                {
                    // One refusal means the whole run is going too fast, not
                    // just this request - so slow everything down, or the next
                    // few hundred requests each pay this same penalty.
                    Slower();

                    var told = response.Headers.RetryAfter?.Delta;
                    var delay = told > wait ? told.Value : wait;

                    Console.WriteLine(
                        $"    {(int)response.StatusCode} from the wiki, waiting {delay.TotalSeconds:0}s "
                        + $"(pace now {_pace.TotalSeconds:0.0}s)");

                    await Task.Delay(delay, ct).ConfigureAwait(false);
                    wait = Longer(wait);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    Console.Error.WriteLine($"    {url}: HTTP {(int)response.StatusCode}");
                    return Unresolvable(url);
                }

                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                if (!body.Contains(BotCheckMarker, StringComparison.Ordinal))
                {
                    Calm();
                    return Page.Found(body);
                }

                Slower();
                Console.WriteLine($"    bot check hit, waiting {wait.TotalSeconds:0}s");
                await Task.Delay(wait, ct).ConfigureAwait(false);
                wait = Longer(wait);
            }
        }

        Console.Error.WriteLine($"    {url}: the wiki never answered");
        return Unresolvable(url);
    }

    /// <summary>The codes that mean "ask again later" rather than "no".</summary>
    private static bool Overloaded(HttpStatusCode code) =>
        code is HttpStatusCode.TooManyRequests
             or HttpStatusCode.ServiceUnavailable
             or HttpStatusCode.BadGateway
             or HttpStatusCode.GatewayTimeout;

    private static TimeSpan Longer(TimeSpan wait) =>
        TimeSpan.FromSeconds(Math.Min(wait.TotalSeconds * 2, 120));

    private void Slower()
    {
        _pace = TimeSpan.FromSeconds(Math.Min(_pace.TotalSeconds * 2, SlowestPace.TotalSeconds));
        _calm = 0;
    }

    /// <summary>
    /// Eases back towards the polite delay after a quiet stretch. Without this
    /// one busy minute early on would slow every remaining request for the rest
    /// of the run - at the ten second ceiling that is hours added to a rebuild
    /// of a thousand pages.
    /// </summary>
    private void Calm()
    {
        if (_pace <= PoliteDelay) return;
        if (++_calm < CalmBeforeSpeedingUp) return;

        _pace = TimeSpan.FromSeconds(Math.Max(_pace.TotalSeconds / 2, PoliteDelay.TotalSeconds));
        _calm = 0;
        Console.WriteLine($"    quiet again, pace back to {_pace.TotalSeconds:0.0}s");
    }

    private Page Unresolvable(string url)
    {
        Unresolved.Add(url);
        return Page.Unknown;
    }

    private async Task PaceAsync(CancellationToken ct)
    {
        var since = DateTimeOffset.Now - _lastRequest;
        if (since < _pace) await Task.Delay(_pace - since, ct).ConfigureAwait(false);
        _lastRequest = DateTimeOffset.Now;
    }

    /// <summary>
    /// Escaped a segment at a time. A wikiwiki page under a trader has a slash
    /// in its title - "Prapor/KORD BREACH Uninvited Guests - Part 1" - and
    /// escaping the whole title turns that into %2F, which the site tolerates
    /// but which matches nothing else in the catalog.
    /// </summary>
    public string JapaneseUrl(string page) =>
        $"{JapaneseOrigin}/eft/{string.Join('/', page.Split('/').Select(Uri.EscapeDataString))}";

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

            // A failure here is already recorded in Unresolved, and the build
            // will refuse to write a catalog once anything is in there. Stopping
            // quietly is only safe because of that.
            if (await GetAsync(url, ct).ConfigureAwait(false) is not { Text: { } json }) break;

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

        if (await GetAsync(url, ct).ConfigureAwait(false) is not { Text: { } json })
            return sections;

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
