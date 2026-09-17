using System.Text.Json;

namespace HeyTarkov.CatalogBuilder;

/// <summary>
/// The game's own English locale file, as the Single Player Tarkov project
/// mirrors it. It is the only source for the short label the game prints over
/// an icon - "Dorm 314", "BeardOil" - that cannot go down, being a file in a
/// git repository rather than a service.
///
/// It is a snapshot, though, so it knows nothing about anything added since it
/// was last refreshed. Callers fill the gaps from the wiki.
/// </summary>
public static class GameLocale
{
    private const string Url =
        "https://raw.githubusercontent.com/sp-tarkov/server/master"
        + "/project/assets/database/locales/global/en.json";

    /// <summary>
    /// Item id to short label, for every id the file carries. Null when the
    /// file could not be fetched, which is not a reason to fail a build.
    /// </summary>
    public static async Task<Dictionary<string, string>?> ShortNamesAsync(CancellationToken ct)
    {
        string json;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "HeyTarkovCatalogBuilder/1.0 (+https://github.com/capycappy/HeyTarkov)");

            json = await http.GetStringAsync(Url, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  the mirrored locale is unreachable: {ex.Message}");
            return null;
        }

        var labels = new Dictionary<string, string>(StringComparer.Ordinal);

        using var document = JsonDocument.Parse(json);

        foreach (var property in document.RootElement.EnumerateObject())
        {
            const string suffix = " ShortName";
            if (!property.Name.EndsWith(suffix, StringComparison.Ordinal)) continue;

            var label = property.Value.GetString();
            if (string.IsNullOrWhiteSpace(label)) continue;

            labels[property.Name[..^suffix.Length]] = label.Trim();
        }

        return labels;
    }
}
