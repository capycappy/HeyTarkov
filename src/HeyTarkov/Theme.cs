namespace HeyTarkov;

/// <summary>
/// Every colour the app picks for itself, in both themes.
///
/// WinForms handles the system controls (Application.SetColorMode); this covers
/// the surfaces the app paints - cards, the level dial, candidate rows - plus
/// the few status colours. Fixed colours like Firebrick are unreadable on a dark
/// background, so every one of them goes through here.
/// </summary>
public static class Theme
{
    public static bool IsDark => Application.IsDarkModeEnabled;

    private static Color Pick(int dark, int light) =>
        Color.FromArgb(unchecked((int)0xFF000000) | (IsDark ? dark : light));

    // ------------------------------------------------------------- surfaces

    /// <summary>The window itself, behind every card.</summary>
    public static Color Page => Pick(0x131519, 0xF3F4F6);

    /// <summary>A card: the settings group, the search field, the candidate list.</summary>
    public static Color Panel => Pick(0x1B1E24, 0xFFFFFF);

    /// <summary>One step up from a card: the selected row, a pressed control.</summary>
    public static Color PanelHi => Pick(0x232830, 0xE8EBF0);

    /// <summary>Hairline borders and dividers.</summary>
    public static Color Edge => Pick(0x2C313A, 0xD5DAE1);

    /// <summary>
    /// The outline of something you type or choose in. A card can be found by
    /// its fill alone; a field has to look like a field, and once the combo box
    /// stopped borrowing the system's own bright border it needed its own.
    /// </summary>
    public static Color Field => Pick(0x3E4653, 0xB9C0CB);

    // ----------------------------------------------------------------- ink

    /// <summary>Normal body text.</summary>
    public static Color Text => Pick(0xE7EAEF, 0x1B1F26);

    /// <summary>Secondary text: status line, hints, the trader column.</summary>
    public static Color Muted => Pick(0x8A93A1, 0x5B6470);

    /// <summary>Tertiary text: field labels, the katakana reading.</summary>
    public static Color Faint => Pick(0x656E7C, 0x8A93A1);

    // -------------------------------------------------------------- accent

    /// <summary>
    /// The amber the app icon already uses. The light variant is darkened so it
    /// still carries white text and reads as a control rather than a highlighter.
    /// </summary>
    public static Color Accent => Pick(0xE0A94A, 0xA9741A);

    /// <summary>Ink on top of <see cref="Accent"/>.</summary>
    public static Color AccentText => Pick(0x1A1408, 0xFFFFFF);

    /// <summary>Input close to clipping - the top of the level dial.</summary>
    public static Color Clip => Pick(0xC4553F, 0xB03A22);

    // ------------------------------------------------------------ by entry

    /// <summary>
    /// Tasks, maps and extracts get their own colour: "Ground Zero" the map and
    /// an extract on it are different answers to the same words, and the colour
    /// separates them faster than reading the badge does.
    /// </summary>
    /// <summary>
    /// A seasonal event task. It is still a task, but it is the one thing in
    /// the list that stops existing when the season ends, and saying the event
    /// name lists nothing else - so it is worth telling apart at a glance.
    /// </summary>
    public static Color Event => Pick(0xC792EA, 0x6B3FA6);

    /// <summary>The event wins: a KORD BREACH task is an event task first.</summary>
    public static Color Of(WikiEntry entry) =>
        entry.Event.Length > 0 ? Event : Of(entry.Kind);

    public static string BadgeOf(WikiEntry entry) =>
        entry.Event.Length > 0 ? "EVENT" : BadgeOf(entry.Kind);

    public static Color Of(EntryKind kind) => kind switch
    {
        EntryKind.Map => Pick(0x6FA8DC, 0x2C6BA8),
        EntryKind.Extract => Pick(0x77C08A, 0x2F7D4A),
        EntryKind.Item => Pick(0xD98C6A, 0xA85426),
        _ => Accent,
    };

    public static string BadgeOf(EntryKind kind) => kind switch
    {
        EntryKind.Map => "MAP",
        EntryKind.Extract => "EXIT",
        EntryKind.Item => "ITEM",
        _ => "TASK",
    };

    // -------------------------------------------------------------- status

    /// <summary>Something is wrong and needs attention.</summary>
    public static Color Danger => IsDark ? Color.FromArgb(0xFF, 0x7B, 0x72) : Color.Firebrick;

    /// <summary>Usable, but not ideal.</summary>
    public static Color Warning => IsDark ? Color.FromArgb(0xE3, 0xB3, 0x41) : Color.DarkOrange;

    /// <summary>Confirmation: good level, list is up to date.</summary>
    public static Color Good => IsDark ? Color.FromArgb(0x6B, 0xC4, 0x6D) : Color.SeaGreen;

    /// <summary>Informational, clickable.</summary>
    public static Color Info => IsDark ? Color.FromArgb(0x79, 0xB8, 0xFF) : Color.MediumBlue;
}
