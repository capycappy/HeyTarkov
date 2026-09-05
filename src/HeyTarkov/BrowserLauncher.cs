using System.Diagnostics;
using Microsoft.Win32;

namespace HeyTarkov;

/// <summary>One way to open a page. An empty path means the system default.</summary>
public sealed record BrowserChoice(string Name, string? ExecutablePath)
{
    public bool IsDefault => ExecutablePath is null;

    public override string ToString() => IsDefault ? Strings.DefaultBrowser : Name;
}

/// <summary>
/// Opens a URL in the browser the user picked. Nothing here touches the game:
/// it is an ordinary process start, the same thing a desktop shortcut does.
/// </summary>
public static class BrowserLauncher
{
    private const string StartMenuInternet = @"SOFTWARE\Clients\StartMenuInternet";

    /// <summary>
    /// The name is fixed rather than translated: it is what gets written to
    /// settings.json for a non-default browser, and a display language must not
    /// change what a setting means. IsDefault is what the UI shows instead.
    /// </summary>
    public static readonly BrowserChoice Default = new("default", null);

    /// <summary>
    /// Every browser registered with Windows, plus the system default. Reads the
    /// same registry Windows itself uses for "Open with", so anything installed
    /// normally shows up - Chrome, Edge, Firefox, Brave, Vivaldi and the rest.
    /// </summary>
    public static List<BrowserChoice> Installed()
    {
        var browsers = new List<BrowserChoice> { Default };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var clients = root.OpenSubKey(StartMenuInternet);
            if (clients is null) continue;

            foreach (var name in clients.GetSubKeyNames())
            {
                using var client = clients.OpenSubKey(name);
                if (client is null) continue;

                var path = ReadCommandPath(client);
                if (path is null || !seen.Add(path)) continue;

                browsers.Add(new BrowserChoice(ReadDisplayName(client, name), path));
            }
        }

        return browsers;
    }

    private static string? ReadCommandPath(RegistryKey client)
    {
        using var command = client.OpenSubKey(@"shell\open\command");
        if (command?.GetValue(null) is not string raw) return null;

        var path = raw.Trim().Trim('"');
        return File.Exists(path) ? path : null;
    }

    private static string ReadDisplayName(RegistryKey client, string fallback)
    {
        using var capabilities = client.OpenSubKey("Capabilities");
        if (capabilities?.GetValue("ApplicationName") is string named && named.Length > 0)
            return named;

        if (client.GetValue(null) is string title && title.Length > 0) return title;

        return fallback;
    }

    public static void Open(string url, BrowserChoice? browser)
    {
        if (browser is { IsDefault: false, ExecutablePath: { } path } && File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path, url) { UseShellExecute = false })?.Dispose();
            return;
        }

        // ShellExecute on the URL itself hands it to whatever the user has set
        // as their default browser.
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
    }
}
