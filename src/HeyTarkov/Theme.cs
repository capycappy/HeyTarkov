namespace HeyTarkov;

/// <summary>
/// Accent colours that stay readable in both themes.
///
/// WinForms itself handles the dark rendering (Application.SetColorMode), so
/// this only covers the few places the app picks a colour deliberately - the
/// status text, the level verdict, the update notice. Fixed colours like
/// Firebrick or MediumBlue are unreadable on a dark background, so every one of
/// them goes through here.
/// </summary>
public static class Theme
{
    public static bool IsDark => Application.IsDarkModeEnabled;

    /// <summary>Normal body text.</summary>
    public static Color Text => IsDark ? Color.FromArgb(0xE6, 0xE6, 0xE6) : SystemColors.ControlText;

    /// <summary>Secondary text: status line, hints.</summary>
    public static Color Muted => IsDark ? Color.FromArgb(0xA0, 0xA0, 0xA0) : SystemColors.GrayText;

    /// <summary>Something is wrong and needs attention.</summary>
    public static Color Danger => IsDark ? Color.FromArgb(0xFF, 0x7B, 0x72) : Color.Firebrick;

    /// <summary>Usable, but not ideal.</summary>
    public static Color Warning => IsDark ? Color.FromArgb(0xE3, 0xB3, 0x41) : Color.DarkOrange;

    /// <summary>Confirmation: good level, list is up to date.</summary>
    public static Color Good => IsDark ? Color.FromArgb(0x6B, 0xC4, 0x6D) : Color.SeaGreen;

    /// <summary>Informational, clickable.</summary>
    public static Color Info => IsDark ? Color.FromArgb(0x79, 0xB8, 0xFF) : Color.MediumBlue;
}
