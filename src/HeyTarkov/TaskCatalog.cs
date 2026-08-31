using System.Reflection;
using System.Text.Json;

namespace HeyTarkov;

/// <summary>
/// Loads the task list that ships with the build.
///
/// The app never contacts either wiki. The catalog is generated ahead of time
/// by tools/CatalogBuilder and embedded here, so a released build is a fixed
/// snapshot: it opens wiki pages in the browser, exactly like a bookmark would,
/// and makes no automated requests of its own. Updating the list means shipping
/// a new version.
/// </summary>
public static class TaskCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DataDirectory
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HeyTarkov");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>
    /// The catalog embedded in this build. Never null in a working build - a
    /// missing or unreadable resource is a packaging error, not a runtime state
    /// the UI should try to recover from.
    /// </summary>
    public static WikiCatalog Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("tasks.json", StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                "tasks.json がビルドに含まれていません。");

        using var stream = assembly.GetManifestResourceStream(name)!;

        var catalog = JsonSerializer.Deserialize<WikiCatalog>(stream, JsonOptions)
                      ?? throw new InvalidOperationException("tasks.json を読み取れませんでした。");

        if (catalog.Entries.Count == 0)
            throw new InvalidOperationException("tasks.json に項目が入っていません。");

        return catalog;
    }
}
