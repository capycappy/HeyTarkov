using System.Text.Json;

namespace HeyTarkov.CatalogBuilder;

/// <summary>
/// The game's own locale files, as the Single Player Tarkov project mirrors
/// them. They are the source for two things nothing else has reliably: the
/// short label the game prints over an icon ("Dorm 314", "BeardOil"), and the
/// name the game shows in Japanese ("マークの刻まれた廃工場の鍵"). Being files
/// in a git repository rather than a service, they do not go down.
///
/// They are a snapshot, though, so they know nothing about anything added since
/// they were last refreshed. Callers fill the gaps elsewhere.
/// </summary>
public static class GameLocale
{
    private const string Folder =
        "https://raw.githubusercontent.com/sp-tarkov/server/master"
        + "/project/assets/database/locales/global/";

    /// <summary>Item id to the English short label.</summary>
    public static Task<Dictionary<string, string>?> ShortNamesAsync(CancellationToken ct) =>
        ReadAsync("en", " ShortName", ct);

    /// <summary>Item id to the full name the Japanese game shows.</summary>
    public static Task<Dictionary<string, string>?> JapaneseNamesAsync(CancellationToken ct) =>
        ReadAsync("jp", " Name", ct);

    /// <summary>
    /// Every "&lt;id&gt;&lt;suffix&gt;" entry in one language's file. Null when the file
    /// could not be fetched, which is not a reason to fail a build.
    /// </summary>
    private static async Task<Dictionary<string, string>?> ReadAsync(
        string language, string suffix, CancellationToken ct)
    {
        string json;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "HeyTarkovCatalogBuilder/1.0 (+https://github.com/capycappy/HeyTarkov)");

            json = await http.GetStringAsync(Folder + language + ".json", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  the mirrored {language} locale is unreachable: {ex.Message}");
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        using var document = JsonDocument.Parse(json);

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!property.Name.EndsWith(suffix, StringComparison.Ordinal)) continue;

            var value = property.Value.GetString();
            if (string.IsNullOrWhiteSpace(value)) continue;

            values[property.Name[..^suffix.Length]] = value.Trim();
        }

        return values;
    }
}
