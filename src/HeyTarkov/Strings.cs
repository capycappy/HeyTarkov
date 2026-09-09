using System.Globalization;

namespace HeyTarkov;

public enum UiLanguage
{
    /// <summary>Whatever Windows is set to.</summary>
    System,

    Japanese,
    English,
}

/// <summary>
/// Every word the window shows, in both languages.
///
/// A pair per string rather than .resx satellite assemblies: there are two
/// languages and about ninety strings, the app ships as one file, and a table
/// that reads as a table is easier to keep honest than a pair of resource
/// files that have to be diffed against each other.
///
/// The language is chosen once at startup, before the window is built, and
/// changing it restarts the app - the same as the theme does, and for the same
/// reason: the text is assigned as the controls are created.
/// </summary>
public static class Strings
{
    /// <summary>True when the window speaks Japanese.</summary>
    public static bool Ja { get; private set; } = true;

    /// <summary>
    /// Resolve a choice against the machine. "System" means the Windows display
    /// language, which is what someone who has never opened the settings
    /// expects to see.
    /// </summary>
    public static void Use(UiLanguage choice) => Ja = Resolve(choice);

    /// <summary>
    /// Which of the two a choice actually means. Separate from Use because the
    /// answer decides more than the captions: picking English also switches
    /// what the app listens for and which wiki it opens.
    /// </summary>
    public static bool Resolve(UiLanguage choice) => choice switch
    {
        UiLanguage.Japanese => true,
        UiLanguage.English => false,
        _ => IsJapaneseWindows,
    };

    public static bool IsJapaneseWindows =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            .Equals("ja", StringComparison.OrdinalIgnoreCase);

    private static string T(string ja, string en) => Ja ? ja : en;

    /// <summary>
    /// Four digits and up get a thousands separator in the English text, which
    /// is what an English reader expects. Invariant rather than current: the
    /// sentence is English wherever Windows happens to be set.
    /// </summary>
    private static string N(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------ the panel

    public static string SpeakLabel => T("話す言語", "Speak");
    public static string WikiLabel => "Wiki";
    public static string BrowserLabel => T("ブラウザ", "Browser");
    public static string InputLabel => T("入力", "Input");

    public static string Rescan => T("再検出", "Rescan");
    public static string CheckLevel => T("レベル確認", "Check level");
    public static string StopLevelCheck => T("確認を停止", "Stop check");

    // The label above each of these says what it is, so the item itself only
    // has to name the language. Four fields share one row.
    public static string SpeakJapanese => T("日本語", "Japanese");
    public static string SpeakEnglish => T("英語", "English");

    public static string WikiJapaneseItem => T("日本語", "Japanese");
    public static string WikiEnglishItem => T("英語", "English");

    /// <summary>Written out, for the sentences that mention a wiki.</summary>
    public static string WikiName(WikiSource wiki) => wiki == WikiSource.Japanese
        ? T("日本語 Wiki", "Japanese wiki")
        : T("英語 Wiki", "English wiki");

    /// <summary>
    /// The one not selected, by name. "The other wiki" means nothing to someone
    /// who has never been told there are two.
    /// </summary>
    public static string OtherWikiName(WikiSource wiki) =>
        WikiName(wiki == WikiSource.Japanese ? WikiSource.English : WikiSource.Japanese);

    // -------------------------------------------------------------- the row

    public static string SearchPlaceholder =>
        T("キーボードで探す（英語表記: wet job part 4）", "Type to search (English: wet job part 4)");

    public static string HeardPlaceholder =>
        T("聞き取り結果はここに出ます", "What you say appears here");

    public static string MicReady => T("押して話す ／ 打つ", "Hold to talk, or type");
    public static string MicListening => T("聞き取り中… 離すと検索します", "Listening… release to search");
    public static string MicPreparing => T("準備中…", "Preparing…");
    public static string MicKeepHolding => T("準備中… 押したままお待ちください", "Preparing… keep holding");
    public static string MicUnavailable => T("使用できません", "Unavailable");
    public static string MicNoEngine => T("音声認識が未インストール", "No speech engine");
    public static string MicInitFailed => T("初期化に失敗", "Failed to start");
    public static string MicCheckingLevel => T("レベル確認中", "Checking level");
    public static string MicHoldToListen => T("押している間だけ聞き取ります", "Hold to listen");

    // ------------------------------------------------------------ the list

    public static string EnterToOpen => T("Enter で開く", "Enter to open");
    public static string Candidates(int count) =>
        T($"候補 {count} 件", count == 1 ? "1 match" : $"{count} matches");

    public static string KindMap => T("マップ ", "Map ");
    public static string KindExit => T("出口 ", "Exit ");
    public static string KindItem => T("アイテム ", "Item ");

    // ------------------------------------------------------------ collector

    public static string CollectorTitle => T("コレクター", "Collector");

    /// <summary>The button on the main window. Short: it shares a row.</summary>
    public static string CollectorOpen => T("コレクター", "Collector");

    public static string CollectorProgress(int held, int all) =>
        T($"{held} / {all} 所持", $"{held} of {all} held");

    public static string CollectorDone => T("すべて集まりました", "All collected");

    public static string CollectorFilter => T("絞り込み", "Filter");

    public static string CollectorColumnLabel => T("ゲーム内表記", "In game");

    public static string CollectorColumnName => T("アイテム名", "Item");

    public static string CollectorRemainingOnly => T("未所持だけ", "Still needed only");

    public static string CollectorHint =>
        T("クリックでチェック　ダブルクリックで Wiki を開く",
          "Click the box to tick, double-click a row to open the wiki");

    public static string CollectorEmpty =>
        T("該当なし", "Nothing matches");

    public static string CollectorNoItems =>
        T("このビルドにはコレクターの一覧が入っていません",
          "This build carries no Collector list");
    public static string KindEvent(string name) => T($"イベント {name} ", $"Event {name} ");

    public static string OpenSelected =>
        T("選択したページをブラウザで開く", "Open selected page in browser");

    public static string AutoOpen =>
        T("確信度が高いときは自動で開く", "Open automatically on a confident match");

    // ---------------------------------------------------------- the footer

    public static string ThemeLabel => T("表示", "Theme");
    public static string ThemeSystem => T("システムに従う", "Follow system");
    public static string ThemeLight => T("ライト", "Light");
    public static string ThemeDark => T("ダーク", "Dark");

    /// <summary>Each language is offered in its own name, not translated.</summary>
    public static string UiLanguageLabel => T("表示言語", "Language");
    public static string UiSystem => T("システムに従う", "Follow system");
    public const string UiJapanese = "日本語";
    public const string UiEnglish = "English";

    public static string Restarting =>
        T("表示を切り替えるため再起動します…", "Restarting to apply the change…");

    public static string SavedForNextLaunch =>
        T("設定を保存しました。次回起動時に反映されます。", "Saved. It applies the next time the app starts.");

    // ---------------------------------------------------------- the status

    public static string Starting => T("起動中…", "Starting…");
    public static string PreparingSpeech => T("音声認識を準備中…", "Preparing speech recognition…");
    public static string MicInUse => T("マイク使用中", "Microphone in use");
    public static string Recognizing => T("認識中…", "Recognizing…");
    public static string SpeechDetected => T("…（音声を検出）", "… (speech detected)");

    public static string FromRecording => T("（録音から再認識）", " (re-recognized from the recording)");
    public static string MicStopped(string via) => T($"マイク停止{via}", $"Microphone closed{via}");
    public static string MicStoppedTryAgain => T("マイク停止。もう一度どうぞ。", "Microphone closed. Try again.");

    public static string NotQuiteMatched(string via) => T(
        $"一致しきりませんでした。近い候補を出しています。{via}",
        $"Not an exact match; showing the closest candidates.{via}");

    public static string Heard(string text, double confidence) => T(
        $"聞き取り: 「{text}」  ({confidence:P0})",
        $"Heard: “{text}”  ({confidence:P0})");

    public static string Rejected(string text, double confidence) => T(
        $"確信度不足: 「{text}」  ({confidence:P0}) — 候補から選んでください",
        $"Not confident: “{text}”  ({confidence:P0}) — pick one below");

    public static string TooShort(double seconds) => T(
        $"録音が短すぎます（{seconds:0.0}秒）— もう少し長く押してください",
        $"Too short ({seconds:0.0}s) — hold the button a little longer");

    public static string Silent(double seconds, string level) => T(
        $"無音でした（{seconds:0.0}秒 / ピーク {level}）— 入力デバイスを確認",
        $"Silence ({seconds:0.0}s, peak {level}) — check the input device");

    public static string NoMatch(double seconds, string level) => T(
        $"音は入っていますが一致しませんでした（{seconds:0.0}秒 / ピーク {level}）",
        $"Audio came through but nothing matched ({seconds:0.0}s, peak {level})");

    public static string Listening(string device) => T(
        $"「{device}」を聞いています。話してみてください。",
        $"Listening to “{device}”. Say something.");

    public static string LevelReadout(string now, string peak, string verdict) => T(
        $"入力 {now}　ピーク {peak}　— {verdict}",
        $"Input {now}   peak {peak}   — {verdict}");

    public static string SearchedAgain(string text, WikiSource wiki) => T(
        $"「{text}」を {WikiName(wiki)} で探し直しました",
        $"Searched the {WikiName(wiki)} again for “{text}”");

    public static string NotOnWiki(string text, WikiSource wiki) => T(
        $"「{text}」は {WikiName(wiki)} にありません",
        $"“{text}” is not on the {WikiName(wiki)}");

    public static string NoPageOnWiki(string name, WikiSource wiki) => T(
        $"「{name}」のページは {WikiName(wiki)} にありません",
        $"There is no page for “{name}” on the {WikiName(wiki)}");

    public static string Opened(string name) => T($"開きました: {name}", $"Opened: {name}");

    public static string Coverage(WikiSource wiki, int onWiki, int other) => other == 0
        ? T($"{WikiName(wiki)} のタスク {onWiki} 件を対象にしています",
            $"Searching {N(onWiki)} entries on the {WikiName(wiki)}")
        : T($"{WikiName(wiki)} のタスク {onWiki} 件を対象にしています（もう一方の Wiki にしかない {other} 件は対象外）",
            $"Searching {N(onWiki)} entries on the {WikiName(wiki)}"
            + $" — {N(other)} more exist only on the {OtherWikiName(wiki)}");

    public static string Vocabulary(int count, string note) =>
        T($"認識語彙 {count} 件{note}", $"{N(count)} phrases{note}");

    public static string SpellOnlyNote(int count) => T(
        $"　※{count} 件は読み未登録（スペル読みでのみ認識）",
        $"   ({N(count)} without readings — spelled out only)");

    public static string SpelledLoaded(int count) =>
        T($"　/　スペル読み {count} 件", $"   /   spelled {N(count)}");

    public static string SpelledFailed(string message) => T(
        $"　/　スペル読みの読み込みに失敗: {message}",
        $"   /   spelled readings failed to load: {message}");

    // ----------------------------------------------------------- and wrong

    public static string CatalogFailed(string message) =>
        T($"タスク一覧を読み込めませんでした: {message}", $"Could not load the entry list: {message}");

    public static string CannotOpenDevice(string message) =>
        T($"このデバイスを開けませんでした: {message}", $"Could not open that device: {message}");

    public static string CannotOpenMic(string message) =>
        T($"マイクを開けませんでした: {message}", $"Could not open the microphone: {message}");

    public static string CannotOpenBrowser(string message) =>
        T($"ブラウザを開けませんでした: {message}", $"Could not open the browser: {message}");

    public static string NoRecognizer(RecognitionLanguage language, string installed) => T(
        (language == RecognitionLanguage.Japanese
            ? "日本語の音声認識が未インストールです。"
            : "英語の音声認識が未インストールです。") + $"利用可能な認識エンジン: {installed}",
        (language == RecognitionLanguage.Japanese
            ? "Japanese speech recognition is not installed. "
            : "English speech recognition is not installed. ") + $"Installed engines: {installed}");

    public static string RecognizerFailed(string message) => T(
        $"音声認識の初期化に失敗しました: {message}",
        $"Speech recognition failed to start: {message}");

    public static string UpdateAvailable(string version) => T(
        $"新しいバージョン v{version} があります（クリックで開く）",
        $"Version v{version} is available (click to open)");

    // ------------------------------------------------------------- devices

    public static string DefaultDevice(string name) =>
        T($"既定のデバイス（{name}）", $"Default device ({name})");

    public static string NoInputDevice => T("入力デバイスなし", "No input device");
    public static string UnknownDevice => T("不明", "Unknown");
    public static string DefaultBrowser => T("既定のブラウザ", "Default browser");
    public static string NoneInstalled => T("(なし)", "(none)");

    public static string VerdictSilent =>
        T("無音 — このデバイスには何も入っていません", "Silent — nothing is reaching this device");
    public static string VerdictTooQuiet =>
        T("小さすぎます — 入力ゲインを上げるか、デバイスを変えてください",
          "Too quiet — raise the input gain, or pick another device");
    public static string VerdictGood => T("十分な音量です", "Good level");
    public static string VerdictTooLoud =>
        T("大きすぎます — 歪む可能性があります", "Too loud — it may distort");

    // ------------------------------------------------------------ catalogue

    public static string CatalogMissing =>
        T("tasks.json がビルドに含まれていません。", "tasks.json is not embedded in this build.");
    public static string CatalogUnreadable =>
        T("tasks.json を読み取れませんでした。", "tasks.json could not be read.");
    public static string CatalogEmpty =>
        T("tasks.json に項目が入っていません。", "tasks.json contains no entries.");

    // --------------------------------------------------- maintainer's note

    public static string[] MissingReadingsHeader => Ja
        ?
        [
            "japanese-lexicon.json に読みが無い単語。",
            "追加すればこれらのタスクを単語読みで言えるようになる。",
            "（未追加でもスペル読みでは認識できる）",
        ]
        :
        [
            "Words with no reading in japanese-lexicon.json.",
            "Adding them makes these entries speakable as words.",
            "(they are still reachable by spelling them out)",
        ];
}
