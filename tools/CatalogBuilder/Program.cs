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

        // "--tasks" fetches the task list again and puts it in place of the
        // tasks already in the file, leaving the maps, extracts, items and keys
        // alone. New tasks appear on the wikis every few weeks; walking every
        // page of everything else to pick them up costs most of an hour.
        var tasksOnly = args.Contains("--tasks", StringComparer.OrdinalIgnoreCase);

        // "--chain" fills in the tasks before and after each task, in place.
        var chainOnly = args.Contains("--chain", StringComparer.OrdinalIgnoreCase);

        var output = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))
            ?? Path.Combine(FindRepositoryRoot(), "src", "HeyTarkov", "tasks.json");

        try
        {
            using var wikis = new Wikis();

            if (keysOnly) return await RefreshKeysAsync(wikis, output).ConfigureAwait(false);
            if (taskMapsOnly) return await RefreshTaskMapsAsync(wikis, output).ConfigureAwait(false);
            if (tasksOnly) return await RefreshTasksAsync(wikis, output).ConfigureAwait(false);
            if (chainOnly) return await RefreshChainAsync(wikis, output).ConfigureAwait(false);

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
            Maps.AddEventVersions(maps);

            Console.WriteLine();
            Console.WriteLine("collector items...");
            var items = await Collector.FetchAsync(wikis);

            Console.WriteLine();
            Console.WriteLine("where the tasks are done...");
            var mapNames = maps.Where(m => m.Kind == EntryKind.Map).Select(m => m.Name).ToList();
            await Tasks.AddMapsAsync(tasks, mapNames, wikis);
            await Tasks.AddMapsFromJapaneseAsync(tasks, mapNames, wikis);
            await Tasks.AddMapsFromObjectivesAsync(tasks, mapNames, wikis);
            await Tasks.AddChainAsync(tasks, wikis);

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

    /// <summary>
    /// Fills in the tasks before and after each task already in the file,
    /// leaving everything else untouched.
    /// </summary>
    private static async Task<int> RefreshChainAsync(Wikis wikis, string output)
    {
        if (await ReadCatalogAsync(output).ConfigureAwait(false) is not { } catalog) return 1;

        var tasks = catalog.Entries.Where(e => e.Kind == EntryKind.Task).ToList();
        Console.WriteLine($"tasks before and after only, over {tasks.Count} tasks...");

        await Tasks.AddChainAsync(tasks, wikis).ConfigureAwait(false);

        if (wikis.Unresolved.Count > 0 || tasks.All(t => t.Previous.Count == 0 && t.Next.Count == 0))
        {
            Console.Error.WriteLine("Pages were missed; leaving the file alone.");
            return 1;
        }

        catalog.UpdatedAt = DateTimeOffset.Now;
        await WriteAsync(catalog, output).ConfigureAwait(false);

        Report(catalog, output);
        return 0;
    }

    /// <summary>
    /// The task list fetched again, in place of the one in the file. Every
    /// other kind of entry is kept as it is.
    ///
    /// Refuses to write a list noticeably shorter than the one it replaces, or
    /// one fetched while the wikis were refusing requests: either means pages
    /// were missed, and a catalog that silently lost tasks is worse than an old
    /// one.
    /// </summary>
    private static async Task<int> RefreshTasksAsync(Wikis wikis, string output)
    {
        if (await ReadCatalogAsync(output).ConfigureAwait(false) is not { } catalog) return 1;

        Maps.AddEventVersions(catalog.Entries);

        var before = catalog.Entries.Where(e => e.Kind == EntryKind.Task).ToList();
        var maps = catalog.Entries.Where(e => e.Kind == EntryKind.Map).Select(m => m.Name).ToList();

        Console.WriteLine($"tasks only, in place of the {before.Count} in the file...");

        var tasks = await Tasks.FetchAsync(wikis).ConfigureAwait(false);
        Console.WriteLine($"  {tasks.Count} tasks");

        await Tasks.AddMapsAsync(tasks, maps, wikis).ConfigureAwait(false);
        await Tasks.AddMapsFromJapaneseAsync(tasks, maps, wikis).ConfigureAwait(false);
        await Tasks.AddMapsFromObjectivesAsync(tasks, maps, wikis).ConfigureAwait(false);
        await Tasks.AddChainAsync(tasks, wikis).ConfigureAwait(false);

        CarryForward(before, tasks);

        var known = before.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        await Tasks.FindJapanesePagesAsync(tasks.Where(t => !known.Contains(t.Name)), wikis).ConfigureAwait(false);

        if (tasks.Count < before.Count * 0.98)
        {
            Console.Error.WriteLine(
                $"Only {tasks.Count} tasks came back against {before.Count} before; leaving the file alone.");
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

        var old = before.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var added = tasks.Where(t => !old.Contains(t.Name)).Select(t => t.Name).OrderBy(n => n).ToList();
        var gone = before.Select(t => t.Name).Except(tasks.Select(t => t.Name)).OrderBy(n => n).ToList();

        Console.WriteLine();
        Console.WriteLine($"  added {added.Count}: {string.Join(", ", added)}");
        Console.WriteLine($"  gone {gone.Count}: {string.Join(", ", gone)}");
        Console.WriteLine($"  without a trader: {tasks.Count(t => t.Group.Length == 0)}");

        catalog.Entries = catalog.Entries.Where(e => e.Kind != EntryKind.Task).Concat(tasks)
            .OrderBy(e => e.Kind)
            .ThenBy(e => e.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        catalog.UpdatedAt = DateTimeOffset.Now;
        await WriteAsync(catalog, output).ConfigureAwait(false);

        Report(catalog, output);
        return 0;
    }

    /// <summary>
    /// What the last fetch knew and this one could not find, kept.
    ///
    /// The wikis' index pages drop pages that still exist - all thirteen
    /// "Gunsmith Master" parts left the Japanese task list while their pages
    /// stayed up - and a refresh that believed the index would quietly take
    /// those links away. A link, or a map, is only ever added by a refresh,
    /// never removed by one; a page that is truly gone shows up as a dead link
    /// someone reports, which is the cheaper mistake.
    /// </summary>
    private static void CarryForward(IReadOnlyList<WikiEntry> before, IReadOnlyList<WikiEntry> after)
    {
        var known = new Dictionary<string, WikiEntry>(StringComparer.Ordinal);
        foreach (var entry in before) known.TryAdd(entry.Name, entry);

        var kept = 0;

        foreach (var entry in after)
        {
            if (!known.TryGetValue(entry.Name, out var was)) continue;

            if (entry.JapaneseUrl is null && was.JapaneseUrl is not null) { entry.JapaneseUrl = was.JapaneseUrl; kept++; }
            if (entry.EnglishUrl is null && was.EnglishUrl is not null) { entry.EnglishUrl = was.EnglishUrl; kept++; }
            if (entry.Maps.Count == 0 && was.Maps.Count > 0) { entry.Maps = was.Maps; kept++; }
            if (entry.Group.Length == 0 && was.Group.Length > 0) { entry.Group = was.Group; kept++; }
            if (entry.Previous.Count == 0 && was.Previous.Count > 0) { entry.Previous = was.Previous; kept++; }
            if (entry.Next.Count == 0 && was.Next.Count > 0) { entry.Next = was.Next; kept++; }

            // An event task stays one after the next event takes the top of
            // the Events page.
            if (entry.Event.Length == 0 && was.Event.Length > 0) { entry.Event = was.Event; kept++; }
        }

        Console.WriteLine($"  {kept} links or maps kept from the last catalog");
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
