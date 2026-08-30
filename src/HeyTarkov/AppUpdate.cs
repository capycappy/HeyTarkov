using System.Net.Http;
using System.Text.Json;

namespace HeyTarkov;

public sealed record ReleaseInfo(string Version, string Url);

/// <summary>
/// Checks GitHub Releases for a newer build. Inert until a repository is set in
/// settings.json (`"UpdateRepository": "owner/name"`), so it does nothing at all
/// while the project is still local.
/// </summary>
public static class AppUpdate
{
    public static async Task<ReleaseInfo?> CheckAsync(
        string? repository, string currentVersion, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repository)) return null;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"HeyTarkov/{currentVersion}");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            var json = await http
                .GetStringAsync($"https://api.github.com/repos/{repository}/releases/latest", ct)
                .ConfigureAwait(false);

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var tag = root.TryGetProperty("tag_name", out var tagName) ? tagName.GetString() : null;
            var url = root.TryGetProperty("html_url", out var htmlUrl) ? htmlUrl.GetString() : null;

            if (tag is null || url is null) return null;

            var latest = tag.TrimStart('v', 'V');
            return IsNewer(latest, currentVersion) ? new ReleaseInfo(latest, url) : null;
        }
        catch (Exception)
        {
            // Being offline, rate-limited, or pointed at a repo with no releases
            // are all normal. Never interrupt the user over it.
            return null;
        }
    }

    public static bool IsNewer(string candidate, string current) =>
        Version.TryParse(Pad(candidate), out var a)
        && Version.TryParse(Pad(current), out var b)
        && a > b;

    /// <summary>Version.TryParse needs at least two components.</summary>
    private static string Pad(string value)
    {
        var text = value.Trim();
        return text.Contains('.') ? text : text + ".0";
    }
}
