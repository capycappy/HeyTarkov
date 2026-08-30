using System.Reflection;

namespace HeyTarkov;

/// <summary>Version, in one place, so the title bar and the update check agree.</summary>
public static class AppInfo
{
    public const string Name = "Hey Tarkov";

    /// <summary>
    /// "owner/name" on GitHub, checked for a newer release at startup. Baked in
    /// so a released build checks without the user configuring anything;
    /// settings.json can still override it for a fork. Empty disables the check.
    /// </summary>
    public const string UpdateRepository = "capycappy/HeyTarkov";

    /// <summary>"1.0.0" - the three-part version, without build metadata.</summary>
    public static string Version { get; } = ReadVersion();

    public static string TitleBar => $"{Name} v{Version}";

    private static string ReadVersion()
    {
        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Strip the "+<commit>" the SDK appends.
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
