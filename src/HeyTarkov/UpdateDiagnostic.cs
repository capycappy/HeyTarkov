using System.Text;

namespace HeyTarkov;

/// <summary>
/// Reports exactly what the startup version check would do. Without a published
/// release there is nothing to see in the UI, so this makes the wiring
/// verifiable before the first release exists.
/// </summary>
public static class UpdateDiagnostic
{
    public static int Run()
    {
        var report = new StringBuilder();
        var reportPath = Path.Combine(TaskCatalog.DataDirectory, "update-check-report.txt");

        try
        {
            var settings = Settings.Load();
            var repository = string.IsNullOrWhiteSpace(settings.UpdateRepository)
                ? AppInfo.UpdateRepository
                : settings.UpdateRepository;

            report.AppendLine($"current version : {AppInfo.Version}");
            report.AppendLine($"built-in repo   : "
                              + $"{(AppInfo.UpdateRepository.Length == 0 ? "(none)" : AppInfo.UpdateRepository)}");
            report.AppendLine($"settings repo   : {settings.UpdateRepository ?? "(none)"}");
            report.AppendLine($"effective repo  : {(repository.Length == 0 ? "(none)" : repository)}");
            report.AppendLine();

            if (string.IsNullOrWhiteSpace(repository))
            {
                report.AppendLine("DISABLED: no repository configured, so the startup check does nothing.");
                report.AppendLine("Set AppInfo.UpdateRepository (shipped default) or");
                report.AppendLine("\"UpdateRepository\": \"owner/name\" in settings.json (per-machine override).");
                return 0;
            }

            var release = AppUpdate.CheckAsync(repository, AppInfo.Version)
                .GetAwaiter().GetResult();

            report.AppendLine(release is null
                ? "RESULT: no newer release (up to date, no releases published, or unreachable)"
                : $"RESULT: v{release.Version} available -> {release.Url}");

            report.AppendLine();
            report.AppendLine("--- version comparison ---");
            foreach (var candidate in new[] { "0.9.0", AppInfo.Version, "1.0.1", "1.1.0", "2.0.0" })
                report.AppendLine($"  v{candidate,-8} newer than v{AppInfo.Version}? "
                                  + AppUpdate.IsNewer(candidate, AppInfo.Version));

            return 0;
        }
        catch (Exception ex)
        {
            report.AppendLine($"FAILED: {ex}");
            return 1;
        }
        finally
        {
            File.WriteAllText(reportPath, report.ToString());
        }
    }
}
