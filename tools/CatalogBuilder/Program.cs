using System.Text.Encodings.Web;
using System.Text.Json;

namespace HeyTarkov.CatalogBuilder;

/// <summary>
/// Regenerates src/HeyTarkov/tasks.json from wikiwiki.jp and the Fandom wiki.
///
///     dotnet run --project tools/CatalogBuilder
///
/// Run by the maintainer, by hand, when the list needs refreshing. The
/// application itself never does this: it ships the generated file and makes no
/// wiki requests at runtime.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // "--keys" refreshes only the keys, in place. A whole run walks eleven
        // hundred pages and the Japanese wiki rate limits long before that is
        // done, so re-fetching everything to correct one part of the catalog
        // costs most of an hour and annoys somebody else's server.
        var keysOnly = args.Contains("--keys", StringComparer.OrdinalIgnoreCase);

        // "--taskmaps" fills in which maps each task names, in place. Fifty
        // pages a request, so it is a couple of minutes rather than an hour.
        var taskMapsOnly = args.Contains("--taskmaps", StringComparer.OrdinalIgnoreCase);
        var objectivesOnly = args.Contains("--objectives", StringComparer.OrdinalIgnoreCase);

        var output = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))
            ?? Path.Combine(FindRepositoryRoot(), "src", "HeyTarkov", "tasks.json");

        try
        {
            using var wikis = new Wikis();

            if (keysOnly) return await RefreshKeysAsync(wikis, output).ConfigureAwait(false);
            if (taskMapsOnly) return await RefreshTaskMapsAsync(wikis, output).ConfigureAwait(false);

            if (objectivesOnly)
            {
                if (await ReadCatalogAsync(output).ConfigureAwait(false) is not { } only) return 1;

                var onlyTasks = only.Entries.Where(e => e.Kind == EntryKind.Task).ToList();
                var onlyMaps = only.Entries.Where(e => e.Kind == EntryKind.Map).Select(m => m.Name).ToList();

                await Tasks.AddMapsFromObjectivesAsync(onlyTasks, onlyMaps, wikis).ConfigureAwait(false);

                only.UpdatedAt = DateTimeOffset.Now;
                await WriteAsync(only, output).ConfigureAwait(false);
                Report(only, output);
                return 0;
            }

            Console.WriteLine("tasks and seasonal events...");
            var tasks = await Tasks.FetchAsync(wikis);
            Console.WriteLine($"  {tasks.Count} tasks");

            Console.WriteLine();
            Console.WriteLine("maps and extracts...");
            var maps = await Maps.FetchAsync(wikis);

            Console.WriteLine();
            Console.WriteLine("collector items...");
            var items = await Collector.FetchAsync(wikis);

            Console.WriteLine();
            Console.WriteLine("where the tasks are done...");
            var mapNames = maps.Where(m => m.Kind == EntryKind.Map).Select(m => m.Name).ToList();
            await Tasks.AddMapsAsync(tasks, mapNames, wikis);
            await Tasks.AddMapsFromJapaneseAsync(tasks, mapNames, wikis);
            await Tasks.AddMapsFromObjectivesAsync(tasks, mapNames, wikis);

            Console.WriteLine();
            Console.WriteLine("keys...");
            var keys = await Keys.FetchAsync(wikis, maps);

            var entries = tasks.Concat(maps).Concat(items).Concat(keys)
                .OrderBy(e => e.Kind)
                .ThenBy(e => e.Group, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (entries.Count == 0)
            {
                Console.Error.WriteLine("Nothing was fetched; leaving the file alone.");
                return 1;
            }

            // A page the wiki refused to answer about is not a page that is
            // missing, and the difference matters: an entry only gets its
            // Japanese link if its page was found. Writing the file now would
            // ship a catalog with links silently absent from it, and the next
            // person to notice would be a user who could not find a task.
            //
            // The failures are almost always rate limiting, and the answer is
            // to run it again later rather than to accept the result.
            if (wikis.Unresolved.Count > 0)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine(
                    $"{wikis.Unresolved.Count} page(s) the wiki would not answer about:");

                foreach (var url in wikis.Unresolved.Take(20))
                    Console.Error.WriteLine($"  {url}");

                if (wikis.Unresolved.Count > 20)
                    Console.Error.WriteLine($"  ...and {wikis.Unresolved.Count - 20} more");

                Console.Error.WriteLine();
                Console.Error.WriteLine("Leaving the file alone. Run it again in a few minutes.");
                return 1;
            }

            var catalog = new WikiCatalog { UpdatedAt = DateTimeOffset.Now, Entries = entries };

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(catalog,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                }));

            Report(catalog, output);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    /// <summary>
    /// Fetches the keys and puts them into the catalog that is already there,
    /// leaving everything else untouched.
    ///
    /// The maps come from that file rather than from the wiki: keys carry the
    /// map they belong to, and the name has to be the same string the map
    /// itself uses, which is exactly what the file holds.
    /// </summary>
    private static async Task<int> RefreshKeysAsync(Wikis wikis, string output)
    {
        if (!File.Exists(output))
        {
            Console.Error.WriteLine($"{output} is not there; run the whole build first.");
            return 1;
        }

        var catalog = JsonSerializer.Deserialize<WikiCatalog>(
            await File.ReadAllTextAsync(output).ConfigureAwait(false));

        if (catalog is null || catalog.Entries.Count == 0)
        {
            Console.Error.WriteLine($"{output} holds no catalog; run the whole build first.");
            return 1;
        }

        Console.WriteLine($"keys only, into the existing {catalog.Entries.Count} entries...");

        var keys = await Keys.FetchAsync(wikis, catalog.Entries).ConfigureAwait(false);

        if (keys.Count == 0)
        {
            Console.Error.WriteLine("No keys came back; leaving the file alone.");
            return 1;
        }

        if (wikis.Unresolved.Count > 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(
                $"{wikis.Unresolved.Count} page(s) the wiki would not answer about; leaving the file alone.");

            foreach (var url in wikis.Unresolved.Take(20)) Console.Error.WriteLine($"  {url}");
            return 1;
        }

        catalog.Entries = catalog.Entries.Where(e => e.Kind != EntryKind.Key).Concat(keys)
            .OrderBy(e => e.Kind)
            .ThenBy(e => e.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        catalog.UpdatedAt = DateTimeOffset.Now;

        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(catalog,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            })).ConfigureAwait(false);

        Report(catalog, output);
        return 0;
    }

    /// <summary>
    /// Fills in the maps of the tasks already in the file, leaving everything
    /// else untouched.
    /// </summary>
    private static async Task<int> RefreshTaskMapsAsync(Wikis wikis, string output)
    {
        if (await ReadCatalogAsync(output).ConfigureAwait(false) is not { } catalog) return 1;

        var tasks = catalog.Entries.Where(e => e.Kind == EntryKind.Task).ToList();
        var maps = catalog.Entries.Where(e => e.Kind == EntryKind.Map).Select(m => m.Name).ToList();

        Console.WriteLine($"task maps only, over {tasks.Count} tasks...");

        await Tasks.AddMapsAsync(tasks, maps, wikis).ConfigureAwait(false);
        await Tasks.AddMapsFromJapaneseAsync(tasks, maps, wikis).ConfigureAwait(false);
        await Tasks.AddMapsFromObjectivesAsync(tasks, maps, wikis).ConfigureAwait(false);

        if (tasks.All(t => t.Maps.Count == 0))
        {
            Console.Error.WriteLine("No maps came back; leaving the file alone.");
            return 1;
        }

        catalog.UpdatedAt = DateTimeOffset.Now;
        await WriteAsync(catalog, output).ConfigureAwait(false);

        Report(catalog, output);
        return 0;
    }

    private static async Task<WikiCatalog?> ReadCatalogAsync(string output)
    {
        if (!File.Exists(output))
        {
            Console.Error.WriteLine($"{output} is not there; run the whole build first.");
            return null;
        }

        var catalog = JsonSerializer.Deserialize<WikiCatalog>(
            await File.ReadAllTextAsync(output).ConfigureAwait(false));

        if (catalog is null || catalog.Entries.Count == 0)
        {
            Console.Error.WriteLine($"{output} holds no catalog; run the whole build first.");
            return null;
        }

        return catalog;
    }

    private static Task WriteAsync(WikiCatalog catalog, string output) =>
        File.WriteAllTextAsync(output, JsonSerializer.Serialize(catalog,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }));

    private static void Report(WikiCatalog catalog, string output)
    {
        Console.WriteLine();
        Console.WriteLine($"wrote {output}");
        Console.WriteLine($"  {catalog.Entries.Count} entries");

        foreach (var kind in Enum.GetValues<EntryKind>())
        {
            var of = catalog.Entries.Where(e => e.Kind == kind).ToList();
            Console.WriteLine($"    {kind,-8} {of.Count,4}"
                              + $"   ja {of.Count(e => e.JapaneseUrl is not null),4}"
                              + $"   en {of.Count(e => e.EnglishUrl is not null),4}");
        }

        Console.WriteLine();
        Console.WriteLine("Rebuild the app so the new catalog is embedded, then run");
        Console.WriteLine("  HeyTarkov.exe --selftest");
        Console.WriteLine("to see whether any new names need katakana readings.");
    }

    /// <summary>
    /// Walk up until the solution layout appears.
    ///
    /// Starts from the current directory: build output lives outside the
    /// repository (Directory.Build.props), so walking up from the exe no longer
    /// reaches it. Finding nothing is an error rather than a guess - writing
    /// src\HeyTarkov\tasks.json under whatever directory this happened to be
    /// started from produces a catalog the app never embeds, after twenty
    /// minutes of wiki requests.
    /// </summary>
    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "src", "HeyTarkov"))) return dir.FullName;
            }
        }

        throw new InvalidOperationException(
            "Could not find the repository (a folder containing src\\HeyTarkov). "
            + "Run this from inside the repository, or pass the output path as the first argument.");
    }
}
