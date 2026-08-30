using System.Text.Json;

namespace HeyTarkov;

public enum ThemeMode
{
    /// <summary>Follow the Windows light/dark setting.</summary>
    System,

    Light,
    Dark,
}

/// <summary>
/// The choices worth remembering between launches. Devices and browsers are
/// stored by name rather than index or path, because both shift as hardware and
/// applications are installed.
/// </summary>
public sealed class Settings
{
    public string? InputDeviceName { get; set; }
    public string? BrowserName { get; set; }
    public bool JapaneseMode { get; set; } = true;
    public WikiSource Wiki { get; set; } = WikiSource.Japanese;
    public bool AutoOpen { get; set; } = true;
    public ThemeMode Theme { get; set; } = ThemeMode.System;

    /// <summary>
    /// "owner/name" on GitHub. Empty disables the update check entirely, which
    /// is the right default while the project is not published.
    /// </summary>
    public string? UpdateRepository { get; set; }

    private static string Path =>
        System.IO.Path.Combine(TaskCatalog.DataDirectory, "settings.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(Path))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path), Options)
                       ?? new Settings();
        }
        catch (Exception)
        {
            // A corrupt settings file should never stop the app from starting.
        }

        return new Settings();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(Path, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception)
        {
            // Losing a preference is not worth interrupting the user over.
        }
    }
}
