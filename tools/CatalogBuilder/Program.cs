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
        var output = args.Length > 0
            ? args[0]
            : Path.Combine(FindRepositoryRoot(), "src", "HeyTarkov", "tasks.json");

        try
        {
            using var wikis = new Wikis();

            Console.WriteLine("tasks and seasonal events...");
            var tasks = await Tasks.FetchAsync(wikis);
            Console.WriteLine($"  {tasks.Count} tasks");

            Console.WriteLine();
            Console.WriteLine("maps and extracts...");
            var maps = await Maps.FetchAsync(wikis);

            Console.WriteLine();
            Console.WriteLine("collector items...");
            var items = await Collector.FetchAsync(wikis);

            var entries = tasks.Concat(maps).Concat(items)
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

    /// <summary>Walk up until the solution layout appears, so the tool can be
    /// run from anywhere.</summary>
    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "HeyTarkov"))) return dir.FullName;
            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
