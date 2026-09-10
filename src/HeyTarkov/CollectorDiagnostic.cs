using System.Text;

namespace HeyTarkov;

/// <summary>
/// What the app sees when it goes looking for the KAPPA items record.
///
/// Exists because "my ticks are gone" is impossible to answer from outside:
/// the record living somewhere, the catalog listing the items, and the two
/// agreeing on the names are three separate things, and only the app can say
/// which of them failed. Reading the file by hand answers none of it.
/// </summary>
public static class CollectorDiagnostic
{
    public static int Run()
    {
        var reportPath = Path.Combine(TaskCatalog.DataDirectory, "collector-report.txt");
        var report = new StringBuilder();

        try
        {
            var path = Path.Combine(TaskCatalog.DataDirectory, "collector.json");

            report.AppendLine($"record path : {path}");
            report.AppendLine($"exists      : {File.Exists(path)}");

            if (File.Exists(path))
            {
                var file = new FileInfo(path);
                report.AppendLine($"size        : {file.Length} bytes");
                report.AppendLine($"written     : {file.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
            }

            var stray = Directory.GetFiles(TaskCatalog.DataDirectory, "collector.json.tmp");
            if (stray.Length > 0)
                report.AppendLine($"LEFTOVER    : {string.Join(", ", stray)} - a save was interrupted");

            var record = CollectorRecord.Load();
            report.AppendLine($"read back   : {record.Checked.Count} names");
            report.AppendLine();

            var catalog = TaskCatalog.Load();
            var items = catalog.Entries.Where(e => e.Kind == EntryKind.Item).ToList();

            report.AppendLine($"catalog     : {catalog.Entries.Count} entries, {items.Count} of them items");

            var held = items.Where(i => record.Has(i.Name)).ToList();
            report.AppendLine($"matched     : {held.Count}/{items.Count}"
                              + "   <- what the button says");
            report.AppendLine();

            // A name in the record that the catalog no longer lists is the one
            // way ticks can seem to vanish while the file is untouched.
            var orphans = record.Checked
                .Where(n => !items.Any(i => string.Equals(i.Name, n, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (orphans.Count > 0)
            {
                report.AppendLine($"IN THE RECORD BUT NOT IN THE CATALOG ({orphans.Count}):");
                foreach (var name in orphans) report.AppendLine($"  {name}");
                report.AppendLine("  These are still saved. They are not shown because no item");
                report.AppendLine("  in this build carries that name any more.");
                report.AppendLine();
            }

            report.AppendLine("ticked:");
            foreach (var item in held) report.AppendLine($"  x {item.Name}");

            report.AppendLine();
            report.AppendLine("still needed:");
            foreach (var item in items.Where(i => !record.Has(i.Name)))
                report.AppendLine($"    {item.Name}");

            File.WriteAllText(reportPath, report.ToString());
            Console.WriteLine(report.ToString());
            return 0;
        }
        catch (Exception ex)
        {
            report.AppendLine($"FAILED: {ex}");
            File.WriteAllText(reportPath, report.ToString());
            Console.Error.WriteLine(report.ToString());
            return 1;
        }
    }
}
