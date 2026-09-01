using System.Runtime.InteropServices;

namespace HeyTarkov;

public sealed class MainForm : Form
{
    private const double AutoOpenConfidence = 0.55;

    private const string MicReady = "押して話す ／ 打つ";
    private const string MicListening = "聞き取り中… 離すと検索します";

    private readonly ComboBox _languageBox = new();
    private readonly ComboBox _wikiBox = new();
    private readonly ComboBox _browserBox = new();
    private readonly ComboBox _deviceBox = new();
    private readonly PillButton _rescanButton = new();
    private readonly PillButton _levelTestButton = new();
    private readonly Label _grammarLabel = new();
    private readonly Label _noticeLabel = new();

    /// <summary>The hold button and the input meter in one control.</summary>
    private readonly LevelDial _dial = new();

    private readonly Label _micHint = new();
    private readonly Label _heardLabel = new();
    private readonly TextBox _typedBox = new();
    private readonly CandidateList _candidates = new();
    private readonly Label _countLabel = new();
    private readonly PillButton _openButton = new();
    private readonly CheckBox _autoOpen = new();
    private readonly ComboBox _themeBox = new();
    private readonly Label _statusLabel = new();

    private Settings _settings = new();
    private SpeechService? _speech;
    private WikiCatalog? _catalog;

    /// <summary>Grammar for the language the microphone is currently using.</summary>
    private TaskIndex? _speechIndex;

    /// <summary>Always English: the text box is typed, not spoken.</summary>
    private TaskIndex? _typedIndex;

    private JapaneseLexicon? _lexicon;
    private JapaneseForms? _hintForms;
    private MicrophoneCapture? _levelTest;
    private CancellationTokenSource? _levelTestDrain;
    private ReleaseInfo? _pendingRelease;
    private double _levelTestPeakDb = AudioLevel.FloorDb;
    private bool _holding;
    private bool _loading;
    private int _buildGeneration;

    public MainForm()
    {
        Text = AppInfo.TitleBar;

        // Every size below is written for 96 DPI. Saying so explicitly is what
        // makes the window survive a move to a monitor that scales differently:
        // WinForms then multiplies the whole layout - control bounds,
        // MinimumSize, and TableLayoutPanel's absolute rows - by the DPI ratio,
        // at startup and again on every DPI change. Left unset it resizes the
        // window but not its contents, and the layout comes apart.
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;

        MinimumSize = new Size(600, 620);
        Size = new Size(680, 740);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Yu Gothic UI", 9.75f);
        BackColor = Theme.Page;

        ApplyIcon();
        BuildLayout();
        ActiveControl = _typedBox;
        Load += async (_, _) => await InitializeAsync();
    }

    /// <summary>
    /// The window and taskbar icons come from Form.Icon, which is separate from
    /// the exe's own icon: without this the title bar keeps WinForms' default.
    /// The multi-size .ico is loaded whole so Windows can pick the right frame
    /// for the title bar, the taskbar and Alt+Tab.
    /// </summary>
    private void ApplyIcon()
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var name = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("app.ico", StringComparison.Ordinal));

            if (name is null) return;

            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is not null) Icon = new Icon(stream);
        }
        catch (Exception)
        {
            // An icon is cosmetic; never let it stop the app from opening.
        }
    }

    private RecognitionLanguage SelectedLanguage =>
        _languageBox.SelectedIndex == 1 ? RecognitionLanguage.English : RecognitionLanguage.Japanese;

    private WikiSource SelectedWiki =>
        _wikiBox.SelectedIndex == 1 ? WikiSource.English : WikiSource.Japanese;

    private AudioDevice? SelectedDevice => _deviceBox.SelectedItem as AudioDevice;

    private BrowserChoice SelectedBrowser =>
        _browserBox.SelectedItem as BrowserChoice ?? BrowserLauncher.Default;

    // ---------------------------------------------------------------- layout

    /// <summary>
    /// Speaking and typing are two doors to the same thing, so they share a row:
    /// the dial to hold, the box to type in. Everything set once - language,
    /// wiki, browser, microphone - is boxed off at the top and then ignored,
    /// which leaves the rest of the window to the candidates.
    /// </summary>
    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            BackColor = Theme.Page,
            ColumnCount = 1,
            RowCount = 8,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildSettingsCard());
        root.Controls.Add(BuildInputRow());
        root.Controls.Add(BuildHeard());
        root.Controls.Add(BuildResultsCard());
        root.Controls.Add(BuildNotice());
        root.Controls.Add(BuildOpenButton());
        root.Controls.Add(BuildBottomRow());
        root.Controls.Add(BuildFooter());

        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // settings
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // dial + search
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // heard
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // candidates
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // notice
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // open
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // auto-open / theme
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // status / vocabulary

        Controls.Add(root);
    }

    private static Label Micro(string text) => new()
    {
        Text = text,
        AutoSize = true,
        BackColor = Color.Transparent,
        ForeColor = Theme.Faint,
        Font = new Font("Yu Gothic UI", 8.25f),
        Margin = new Padding(1, 0, 0, 1),
    };

    private static Panel Divider() => new()
    {
        Dock = DockStyle.Fill,
        Height = 1,
        BackColor = Theme.Edge,
        Margin = new Padding(0, 8, 0, 8),
    };

    /// <summary>
    /// Only the style: WinForms renders combo boxes for the active theme
    /// itself, and overriding the colours costs a pale border and a blue
    /// selection block that neither theme asked for.
    /// </summary>
    private static void StyleCombo(ComboBox box) => box.DropDownStyle = ComboBoxStyle.DropDownList;

    /// <summary>Set once and then ignored, so it is boxed off and quiet.</summary>
    private Card BuildSettingsCard()
    {
        StyleCombo(_languageBox);
        _languageBox.Items.AddRange(new object[] { "日本語で言う", "英語で言う" });
        _languageBox.SelectedIndex = 0;
        _languageBox.SelectedIndexChanged += async (_, _) => await OnLanguageChangedAsync();

        StyleCombo(_wikiBox);
        _wikiBox.Items.AddRange(new object[] { "日本語 Wiki", "英語 Wiki" });
        _wikiBox.SelectedIndex = 0;
        _wikiBox.SelectedIndexChanged += async (_, _) => await OnWikiChangedAsync();

        StyleCombo(_browserBox);
        _browserBox.SelectedIndexChanged += (_, _) => OnBrowserChanged();

        StyleCombo(_deviceBox);
        _deviceBox.SelectedIndexChanged += (_, _) => OnDeviceChanged();

        var choices = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            ColumnCount = 3,
            RowCount = 2,
            Margin = new Padding(0),
        };
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        choices.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        choices.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        choices.Controls.Add(Micro("言語"), 0, 0);
        choices.Controls.Add(Micro("Wiki"), 1, 0);
        choices.Controls.Add(Micro("ブラウザ"), 2, 0);

        // Anchored, not docked: Dock.Fill stretches a ComboBox to the row height
        // and clips its bottom edge. Left+Right stretches the width only.
        foreach (var (box, column) in new[] { (_languageBox, 0), (_wikiBox, 1), (_browserBox, 2) })
        {
            box.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            box.Margin = new Padding(0, 0, column == 2 ? 0 : 12, 0);
            choices.Controls.Add(box, column, 1);
        }

        _rescanButton.Text = "再検出";
        _levelTestButton.Text = "レベル確認";

        // Identical fixed sizes: the level button's caption toggles between
        // "レベル確認" and "確認を停止", and an auto-sized button would shift the
        // row every time it changed.
        foreach (var button in new[] { _rescanButton, _levelTestButton })
        {
            button.Ghost = true;
            button.OnCard = true;
            button.Radius = 5;
            button.AutoSize = false;
            button.Size = new Size(84, 26);
            button.Anchor = AnchorStyles.Left;
            button.Margin = new Padding(8, 0, 0, 0);
        }
        _rescanButton.Click += (_, _) => LoadDevices(_settings.InputDeviceName);
        _levelTestButton.Click += (_, _) => ToggleLevelTest();

        var device = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0),
        };
        device.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        device.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        device.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        device.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var inputLabel = Micro("入力");
        inputLabel.Anchor = AnchorStyles.Left;
        inputLabel.Margin = new Padding(1, 0, 10, 0);
        device.Controls.Add(inputLabel, 0, 0);

        _deviceBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _deviceBox.Margin = new Padding(0);
        device.Controls.Add(_deviceBox, 1, 0);
        device.Controls.Add(_rescanButton, 2, 0);
        device.Controls.Add(_levelTestButton, 3, 0);

        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16, 12, 16, 14),
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        stack.Controls.Add(choices);
        stack.Controls.Add(Divider());
        stack.Controls.Add(device);
        foreach (var _ in Enumerable.Range(0, 3)) stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var card = new Card { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 14) };
        card.Controls.Add(stack);
        return card;
    }

    /// <summary>The dial and the search box: hold the one, or type in the other.</summary>
    private TableLayoutPanel BuildInputRow()
    {
        _dial.Size = new Size(64, 64);
        _dial.Enabled = false;
        _dial.Anchor = AnchorStyles.Left;
        _dial.Margin = new Padding(0, 0, 14, 0);
        _dial.HoldStarted += (_, _) => BeginHold();
        _dial.HoldEnded += (_, _) => EndHold();

        _typedBox.BorderStyle = BorderStyle.None;
        _typedBox.BackColor = Theme.Panel;
        _typedBox.ForeColor = Theme.Text;
        _typedBox.Font = new Font("Yu Gothic UI", 10.5f);
        _typedBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _typedBox.Margin = new Padding(0);
        _typedBox.PlaceholderText = "キーボードで探す（英語表記: wet job part 4）";
        _typedBox.TextChanged += (_, _) => ShowCandidatesFor(_typedBox.Text);
        _typedBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            OpenSelected();
        };

        _micHint.Text = "準備中…";
        _micHint.AutoSize = true;
        _micHint.BackColor = Color.Transparent;
        _micHint.ForeColor = Theme.Faint;
        _micHint.Font = new Font("Yu Gothic UI", 8.25f);
        _micHint.Anchor = AnchorStyles.Right;
        _micHint.Margin = new Padding(10, 0, 0, 0);

        var inner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(34, 0, 12, 0),
            Margin = new Padding(0),
        };
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        inner.Controls.Add(_typedBox, 0, 0);
        inner.Controls.Add(_micHint, 1, 0);

        var box = new SearchCard
        {
            Radius = 8,
            Height = 38,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0),
        };
        box.Controls.Add(inner);

        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        row.Controls.Add(_dial, 0, 0);
        row.Controls.Add(box, 1, 0);
        return row;
    }

    /// <summary>What the recognizer heard, large enough to read at a glance.</summary>
    private Label BuildHeard()
    {
        _heardLabel.Dock = DockStyle.Fill;
        _heardLabel.Height = 34;
        _heardLabel.TextAlign = ContentAlignment.MiddleLeft;
        _heardLabel.Font = new Font("Yu Gothic UI", 12.5f);
        _heardLabel.ForeColor = Theme.Faint;
        _heardLabel.BackColor = Color.Transparent;
        _heardLabel.Margin = new Padding(2, 10, 0, 6);
        _heardLabel.Text = "聞き取り結果はここに出ます";
        return _heardLabel;
    }

    private Card BuildResultsCard()
    {
        _candidates.Dock = DockStyle.Fill;
        _candidates.BackColor = Theme.Panel;
        _candidates.ForeColor = Theme.Text;
        _candidates.Margin = new Padding(0);
        _candidates.DoubleClick += (_, _) => OpenSelected();
        _candidates.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            OpenSelected();
        };

        _countLabel.Text = "";
        _countLabel.AutoSize = true;
        _countLabel.BackColor = Color.Transparent;
        _countLabel.ForeColor = Theme.Faint;
        _countLabel.Font = new Font("Yu Gothic UI", 8.25f);
        _countLabel.Margin = new Padding(2, 0, 0, 6);

        var hint = Micro("Enter で開く");
        hint.Anchor = AnchorStyles.Right;
        hint.Margin = new Padding(0, 0, 2, 6);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.Controls.Add(_countLabel, 0, 0);
        header.Controls.Add(hint, 1, 0);

        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(14, 12, 14, 12),
            Margin = new Padding(0),
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        stack.Controls.Add(header, 0, 0);
        stack.Controls.Add(_candidates, 0, 1);

        var card = new Card { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12) };
        card.Controls.Add(stack);
        return card;
    }

    private Label BuildNotice()
    {
        _noticeLabel.AutoSize = true;
        _noticeLabel.Visible = false;
        _noticeLabel.BackColor = Color.Transparent;
        _noticeLabel.Cursor = Cursors.Hand;
        _noticeLabel.Margin = new Padding(2, 0, 0, 8);
        _noticeLabel.Click += (_, _) => OnNoticeClicked();
        return _noticeLabel;
    }

    private PillButton BuildOpenButton()
    {
        _openButton.Text = "選択したページをブラウザで開く";
        _openButton.Dock = DockStyle.Fill;
        _openButton.Height = 38;
        _openButton.Font = new Font("Yu Gothic UI", 10.5f, FontStyle.Bold);
        _openButton.Margin = new Padding(0, 0, 0, 12);
        _openButton.Click += (_, _) => OpenSelected();
        return _openButton;
    }

    private TableLayoutPanel BuildBottomRow()
    {
        _autoOpen.Text = "確信度が高いときは自動で開く";
        _autoOpen.Checked = true;
        _autoOpen.AutoSize = true;
        _autoOpen.BackColor = Color.Transparent;
        _autoOpen.ForeColor = Theme.Muted;
        _autoOpen.Anchor = AnchorStyles.Left;
        _autoOpen.Margin = new Padding(0, 0, 0, 0);
        _autoOpen.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.AutoOpen = _autoOpen.Checked;
            _settings.Save();
        };

        StyleCombo(_themeBox);
        _themeBox.Width = 118;
        _themeBox.Anchor = AnchorStyles.Right;
        _themeBox.Margin = new Padding(8, 0, 0, 0);
        _themeBox.Items.AddRange(new object[] { "システムに従う", "ライト", "ダーク" });
        _themeBox.SelectedIndex = 0;
        _themeBox.SelectedIndexChanged += (_, _) => OnThemeChanged();

        var themeLabel = Micro("表示");
        themeLabel.Anchor = AnchorStyles.Right;
        themeLabel.Margin = new Padding(0, 0, 0, 0);

        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 10),
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        row.Controls.Add(_autoOpen, 0, 0);
        row.Controls.Add(themeLabel, 1, 0);
        row.Controls.Add(_themeBox, 2, 0);
        return row;
    }

    /// <summary>
    /// What the app is doing, and what it can hear. One line rather than two:
    /// a second row of grey text is the sort of noise this redesign is trying
    /// to remove, and it was the row that got clipped first on a small window.
    /// </summary>
    private TableLayoutPanel BuildFooter()
    {
        _statusLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _statusLabel.AutoSize = false;
        _statusLabel.Height = 18;
        _statusLabel.AutoEllipsis = true;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.BackColor = Color.Transparent;
        _statusLabel.ForeColor = Theme.Muted;
        _statusLabel.Font = new Font("Yu Gothic UI", 8.75f);
        _statusLabel.Margin = new Padding(2, 0, 12, 0);
        _statusLabel.Text = "起動中…";

        _grammarLabel.Anchor = AnchorStyles.Right;
        _grammarLabel.AutoSize = true;
        _grammarLabel.BackColor = Color.Transparent;
        _grammarLabel.ForeColor = Theme.Faint;
        _grammarLabel.Font = new Font("Yu Gothic UI", 8.25f);
        _grammarLabel.Margin = new Padding(0, 0, 2, 0);

        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        row.Controls.Add(_statusLabel, 0, 0);
        row.Controls.Add(_grammarLabel, 1, 0);
        return row;
    }

    /// <summary>
    /// Whether the dial can be held, and why not when it cannot. The reason used
    /// to be the button's own caption; the dial has no room for a sentence.
    /// </summary>
    private void SetMicState(bool ready, string hint, bool warn = false)
    {
        _dial.Enabled = ready;
        if (!ready) _dial.Reset();
        _micHint.Text = hint;
        _micHint.ForeColor = warn ? Theme.Warning : Theme.Faint;
    }

    // --------------------------------------------------------- window frame

    private const int DwmCaptionColour = 35;
    private const int DwmTextColour = 36;
    private const int DwmBorderColour = 34;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyCaptionColour();
    }

    /// <summary>
    /// Paints the title bar in the app's own colour instead of the system grey.
    /// Windows 11 only; older builds return an error code we ignore, and the
    /// window simply keeps the default caption.
    /// </summary>
    private void ApplyCaptionColour()
    {
        if (!IsHandleCreated) return;

        static int Bgr(Color c) => c.R | (c.G << 8) | (c.B << 16);

        try
        {
            var caption = Bgr(Theme.Page);
            var text = Bgr(Theme.Muted);
            var border = Bgr(Theme.Edge);

            DwmSetWindowAttribute(Handle, DwmCaptionColour, ref caption, sizeof(int));
            DwmSetWindowAttribute(Handle, DwmTextColour, ref text, sizeof(int));
            DwmSetWindowAttribute(Handle, DwmBorderColour, ref border, sizeof(int));
        }
        catch (Exception)
        {
            // Cosmetic; never let it stop the window from opening.
        }
    }

    // ------------------------------------------------------------- lifecycle

    private async Task InitializeAsync()
    {
        _settings = Settings.Load();

        _loading = true;
        _languageBox.SelectedIndex = _settings.JapaneseMode ? 0 : 1;
        _wikiBox.SelectedIndex = _settings.Wiki == WikiSource.English ? 1 : 0;
        _autoOpen.Checked = _settings.AutoOpen;
        _themeBox.SelectedIndex = _settings.Theme switch
        {
            ThemeMode.Light => 1,
            ThemeMode.Dark => 2,
            _ => 0,
        };
        LoadBrowsers(_settings.BrowserName);
        LoadDevices(_settings.InputDeviceName);
        _loading = false;

        _lexicon = JapaneseLexicon.Load();
        _hintForms = new JapaneseForms(_lexicon);

        LoadCatalog();
        await RebuildForLanguageAsync();

        // The only thing this app asks the network for: whether a newer build
        // exists. The task list is fixed at build time.
        _ = CheckForAppUpdateAsync();
    }

    /// <summary>
    /// The catalog is embedded in the build, so this cannot fail at runtime
    /// unless the packaging is broken - in which case the app has nothing to do
    /// and should say so plainly.
    /// </summary>
    private void LoadCatalog()
    {
        try
        {
            _catalog = TaskCatalog.Load();
            _typedIndex = null;
        }
        catch (Exception ex)
        {
            SetStatus($"タスク一覧を読み込めませんでした: {ex.Message}");
        }
    }

    /// <summary>
    /// Startup check for a newer release. Silent unless there is one: being
    /// offline, rate-limited, or running an unreleased build are all normal.
    /// settings.json wins over the built-in repository so a fork can retarget it.
    /// </summary>
    private async Task CheckForAppUpdateAsync()
    {
        var repository = string.IsNullOrWhiteSpace(_settings.UpdateRepository)
            ? AppInfo.UpdateRepository
            : _settings.UpdateRepository;

        var release = await AppUpdate.CheckAsync(repository, AppInfo.Version);
        if (release is null) return;

        BeginInvoke(() =>
        {
            _pendingRelease = release;
            ShowNotice(
                $"新しいバージョン v{release.Version} があります（クリックで開く）",
                Theme.Info);
        });
    }

    private void ShowNotice(string text, Color color)
    {
        // A pending release is the more important message; task-list news must
        // not scroll it away.
        if (_pendingRelease is not null && color != Theme.Info) return;

        _noticeLabel.Text = text;
        _noticeLabel.ForeColor = color;
        _noticeLabel.Visible = true;
    }

    private void OnNoticeClicked()
    {
        if (_pendingRelease is null) return;
        BrowserLauncher.Open(_pendingRelease.Url, SelectedBrowser);
    }

    // -------------------------------------------------------------- settings

    private void LoadBrowsers(string? preferredName)
    {
        var browsers = BrowserLauncher.Installed();

        _browserBox.BeginUpdate();
        _browserBox.Items.Clear();
        foreach (var browser in browsers) _browserBox.Items.Add(browser);
        _browserBox.EndUpdate();

        var index = preferredName is null
            ? 0
            : browsers.FindIndex(b => !b.IsDefault && b.Name == preferredName);

        _browserBox.SelectedIndex = index >= 0 ? index : 0;
    }

    private void OnBrowserChanged()
    {
        if (_loading) return;
        _settings.BrowserName = SelectedBrowser.IsDefault ? null : SelectedBrowser.Name;
        _settings.Save();
    }

    /// <summary>
    /// WinForms fixes the control rendering for the whole process when the
    /// process starts, so switching theme means restarting. Doing it for the
    /// user is less annoying than telling them to.
    /// </summary>
    private void OnThemeChanged()
    {
        if (_loading) return;

        var chosen = _themeBox.SelectedIndex switch
        {
            1 => ThemeMode.Light,
            2 => ThemeMode.Dark,
            _ => ThemeMode.System,
        };

        if (chosen == _settings.Theme) return;

        _settings.Theme = chosen;
        _settings.Save();

        SetStatus("表示テーマを切り替えるため再起動します…");
        _speech?.Dispose();
        _speech = null;

        try
        {
            Application.Restart();
        }
        catch (Exception)
        {
            // The setting is saved either way; it applies on the next launch.
            SetStatus("表示テーマを保存しました。次回起動時に反映されます。");
        }
    }

    private async Task OnWikiChangedAsync()
    {
        if (_loading) return;

        _settings.Wiki = SelectedWiki;
        _settings.Save();

        // The wiki decides which tasks exist, so both the grammar and the typed
        // search have to be rebuilt against the new set.
        _typedIndex = null;
        _candidates.Items.Clear();

        await RebuildForLanguageAsync();
        ShowCandidatesFor(_typedBox.Text);
    }

    /// <summary>Re-enumerate capture devices, keeping the current pick if it is
    /// still present. Called at startup and whenever hardware is plugged in.</summary>
    private void LoadDevices(string? preferredName)
    {
        StopLevelTest();

        var devices = AudioInput.Devices();

        _deviceBox.BeginUpdate();
        _deviceBox.Items.Clear();
        foreach (var device in devices) _deviceBox.Items.Add(device);
        _deviceBox.EndUpdate();

        var index = preferredName is null
            ? 0
            : devices.FindIndex(d => !d.IsDefault && d.Name == preferredName);

        _deviceBox.SelectedIndex = index >= 0 ? index : 0;
    }

    private void OnDeviceChanged()
    {
        StopLevelTest();

        var device = SelectedDevice;
        if (device is null) return;

        if (_speech is not null) _speech.DeviceIndex = device.Index;

        if (_loading) return;
        _settings.InputDeviceName = device.IsDefault ? null : device.Name;
        _settings.Save();
    }

    // ----------------------------------------------------------- level meter

    /// <summary>
    /// Capture from the selected device without involving the recognizer, so
    /// "is this input even live?" can be answered on its own.
    /// </summary>
    private void ToggleLevelTest()
    {
        if (_levelTest is not null) { StopLevelTest(); return; }
        if (_holding) return;

        var device = SelectedDevice;
        if (device is null) return;

        try
        {
            _levelTestPeakDb = AudioLevel.FloorDb;
            _levelTest = new MicrophoneCapture(device.Index);
            _levelTest.Level += (_, level) => BeginInvoke(() => ShowLevel(level));

            // Nothing else is reading the stream during a level test.
            _levelTestDrain = new CancellationTokenSource();
            var stream = _levelTest.Stream;
            var token = _levelTestDrain.Token;
            _ = Task.Run(() =>
            {
                var scratch = new byte[4096];
                while (!token.IsCancellationRequested && stream.Read(scratch, 0, scratch.Length) > 0)
                {
                }
            }, token);

            _levelTestButton.Text = "確認を停止";
            SetMicState(false, "レベル確認中");
            SetStatus($"「{device}」を聞いています。話してみてください。");
        }
        catch (Exception ex)
        {
            StopLevelTest();
            _heardLabel.ForeColor = Theme.Danger;
            _heardLabel.Text = $"このデバイスを開けませんでした: {ex.Message}";
        }
    }

    /// <summary>
    /// dBFS, not a linear percentage: normal speech peaks around a tenth of full
    /// scale, which reads as "10%" and looks broken when it is in fact fine.
    /// 0 dBFS is the clipping point, so healthy speech sits near -20.
    /// </summary>
    private void ShowLevel(AudioLevel level)
    {
        _dial.Value = AudioLevel.ToMeter(level.PeakDb);

        if (level.PeakDb > _levelTestPeakDb) _levelTestPeakDb = level.PeakDb;

        _heardLabel.ForeColor = _levelTestPeakDb switch
        {
            < AudioLevel.SilenceDb => Theme.Danger,
            < -35 => Theme.Warning,
            < -3 => Theme.Good,
            _ => Theme.Warning,
        };

        var now = level.PeakDb <= AudioLevel.FloorDb ? "-∞" : $"{level.PeakDb:0} dBFS";
        var held = _levelTestPeakDb <= AudioLevel.FloorDb ? "-∞" : $"{_levelTestPeakDb:0} dBFS";

        _heardLabel.Text = $"入力 {now}　ピーク {held}　— {AudioLevel.Verdict(_levelTestPeakDb)}";
    }

    private void StopLevelTest()
    {
        if (_levelTest is null) return;

        _levelTestDrain?.Cancel();
        _levelTestDrain?.Dispose();
        _levelTestDrain = null;

        _levelTest.Dispose();
        _levelTest = null;

        _dial.Reset();
        _levelTestButton.Text = "レベル確認";
        SetMicState(_speech is not null, _speech is not null ? MicReady : "使用できません",
            warn: _speech is null);
    }

    // ------------------------------------------------------------ recognizer

    private async Task OnLanguageChangedAsync()
    {
        if (_loading) return;

        _settings.JapaneseMode = SelectedLanguage == RecognitionLanguage.Japanese;
        _settings.Save();

        await RebuildForLanguageAsync();
    }

    private sealed record SpeechSetup(
        TaskIndex? Index, string Note, SpeechService? Speech, string? Error);

    /// <summary>
    /// Building a grammar takes a second or two, so it happens off the UI
    /// thread; the window stays responsive with the microphone disabled.
    /// </summary>
    private async Task RebuildForLanguageAsync()
    {
        var generation = ++_buildGeneration;

        SetMicState(false, "準備中…");
        SetStatus("音声認識を準備中…");

        _speech?.Dispose();
        _speech = null;

        var language = SelectedLanguage;
        var wiki = SelectedWiki;
        var tasks = _catalog?.On(wiki);
        var lexicon = _lexicon;

        var setup = await Task.Run(() => BuildSpeech(tasks, lexicon, language));

        // A newer switch already started; drop this result.
        if (generation != _buildGeneration)
        {
            setup.Speech?.Dispose();
            return;
        }

        Apply(setup, language);

        if (_speech is not null && _speechIndex is not null)
            await LoadSpelledAsync(_speech, _speechIndex, generation);
    }

    private static SpeechSetup BuildSpeech(
        List<WikiEntry>? tasks, JapaneseLexicon? lexicon, RecognitionLanguage language)
    {
        if (tasks is null) return new SpeechSetup(null, "", null, null);

        var note = "";
        TaskIndex index;

        if (language == RecognitionLanguage.Japanese)
        {
            var scheme = new JapaneseScheme(new JapaneseForms(lexicon ?? JapaneseLexicon.Load()));
            index = new TaskIndex(tasks, scheme);

            // Tasks whose words have no katakana reading yet are still usable by
            // spelling them out. Say so, and write the missing words somewhere
            // they can be picked up and added to the lexicon.
            if (index.SpellOnlyTasks.Count > 0)
            {
                note = $"　※{index.SpellOnlyTasks.Count} 件は読み未登録"
                       + "（スペル読みでのみ認識）";
                ReportMissingReadings(scheme, index);
            }
        }
        else
        {
            index = new TaskIndex(tasks, new EnglishScheme());
        }

        var recognizerInfo = SpeechService.FindRecognizer(language);
        if (recognizerInfo is null)
            return new SpeechSetup(index, note, null, "recognizer-missing");

        try
        {
            var speech = new SpeechService(recognizerInfo);
            speech.LoadVocabulary(index.GrammarPhrases);
            return new SpeechSetup(index, note, speech, null);
        }
        catch (Exception ex)
        {
            return new SpeechSetup(index, note, null, ex.Message);
        }
    }

    private void Apply(SpeechSetup setup, RecognitionLanguage language)
    {
        if (setup.Index is not null)
        {
            _speechIndex = setup.Index;
            if (_catalog is not null)
                _typedIndex ??= new TaskIndex(_catalog.On(SelectedWiki), new EnglishScheme());

            _grammarLabel.ForeColor = Theme.Muted;
            _grammarLabel.Text = $"認識語彙 {setup.Index.TaskCount} 件{setup.Note}";
            ReportCoverage();
        }

        if (setup.Error == "recognizer-missing")
        {
            SetMicState(false, "音声認識が未インストール", warn: true);
            SetStatus(language == RecognitionLanguage.Japanese
                ? "日本語の音声認識が未インストールです。"
                  + $"利用可能な認識エンジン: {SpeechService.InstalledRecognizerSummary()}"
                : "英語の音声認識が未インストールです。"
                  + $"利用可能な認識エンジン: {SpeechService.InstalledRecognizerSummary()}");
            return;
        }

        if (setup.Speech is null)
        {
            SetMicState(false, "初期化に失敗", warn: true);
            if (setup.Error is not null) SetStatus($"音声認識の初期化に失敗しました: {setup.Error}");
            return;
        }

        _speech = setup.Speech;
        _speech.DeviceIndex = SelectedDevice?.Index ?? -1;
        _speech.AudioLevel += (_, level) =>
            BeginInvoke(() => _dial.Value = AudioLevel.ToMeter(level.PeakDb));
        _speech.Finished += (_, outcome) =>
            BeginInvoke(() => OnRecognitionFinished(outcome));
        _speech.Hypothesis += (_, text) =>
            BeginInvoke(() => ShowLive(text));
        _speech.SpeechDetected += (_, _) =>
            BeginInvoke(() => { if (_holding) _heardLabel.Text = "…（音声を検出）"; });

        SetMicState(true, MicReady);
    }

    /// <summary>
    /// The spelled-out phrases are loaded after the microphone is already usable
    /// with the normal readings.
    /// </summary>
    private async Task LoadSpelledAsync(SpeechService speech, TaskIndex index, int generation)
    {
        var phrases = index.DeferredGrammarPhrases;
        if (phrases.Count == 0) return;

        try
        {
            await Task.Run(() => speech.AddVocabulary("spelled", phrases));
            if (generation != _buildGeneration) return;

            _grammarLabel.Text += $"　/　スペル読み {phrases.Count} 件";
        }
        catch (Exception ex)
        {
            if (generation != _buildGeneration) return;
            _grammarLabel.ForeColor = Theme.Danger;
            _grammarLabel.Text += $"　/　スペル読みの読み込みに失敗: {ex.Message}";
        }
    }

    /// <summary>
    /// Writes the words that have no katakana reading, with the tasks they
    /// block, next to the catalog. Adding them to japanese-lexicon.json is what
    /// turns those tasks from spell-only into speakable.
    /// </summary>
    private static void ReportMissingReadings(JapaneseScheme scheme, TaskIndex index)
    {
        try
        {
            var path = Path.Combine(TaskCatalog.DataDirectory, "missing-readings.txt");
            var lines = new List<string>
            {
                "japanese-lexicon.json に読みが無い単語。",
                "追加すればこれらのタスクを単語読みで言えるようになる。",
                "（未追加でもスペル読みでは認識できる）",
                "",
                "--- words ---",
            };

            lines.AddRange(scheme.UnknownWords);
            lines.Add("");
            lines.Add("--- tasks reachable only by spelling ---");
            lines.AddRange(index.SpellOnlyTasks
                .Select(t => t.Name)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase));

            File.WriteAllLines(path, lines);
        }
        catch (Exception)
        {
            // Diagnostics only; never let it interfere with startup.
        }
    }

    private void ReportCoverage()
    {
        if (_catalog is null) return;

        var wiki = SelectedWiki;
        var onWiki = _catalog.CountOn(wiki);
        var other = _catalog.Entries.Count - onWiki;
        var label = wiki == WikiSource.Japanese ? "日本語 Wiki" : "英語 Wiki";

        SetStatus(other == 0
            ? $"{label} のタスク {onWiki} 件を対象にしています"
            : $"{label} のタスク {onWiki} 件を対象にしています"
              + $"（もう一方の Wiki にしかない {other} 件は対象外）");
    }

    private void BeginHold()
    {
        if (_holding || _speech is null || _speechIndex is null) return;
        if (_levelTest is not null) StopLevelTest();

        try
        {
            _speech.Start();
            _holding = true;
            _micHint.Text = MicListening;
            _heardLabel.ForeColor = Theme.Muted;
            _heardLabel.Text = "…";
            SetStatus("マイク使用中");
        }
        catch (Exception ex)
        {
            SetStatus($"マイクを開けませんでした: {ex.Message}");
        }
    }

    private void EndHold()
    {
        if (!_holding || _speech is null) return;

        _holding = false;
        _micHint.Text = MicReady;
        SetStatus("認識中…");
        _speech.Stop();
    }

    /// <summary>The engine's running guess while the button is held.</summary>
    private void ShowLive(string text)
    {
        if (!_holding) return;
        _heardLabel.ForeColor = Theme.Muted;
        _heardLabel.Text = $"… {text}";
    }

    private void OnRecognitionFinished(RecognitionOutcome outcome)
    {
        _dial.Reset();

        if (outcome.IsEmpty)
        {
            // Say why. Silence, a too-quiet signal and "audio was fine but
            // nothing matched" are three different problems.
            var level = outcome.PeakDb <= AudioLevel.FloorDb
                ? "-∞"
                : $"{outcome.PeakDb:0} dBFS";

            _heardLabel.ForeColor = Theme.Danger;
            _heardLabel.Text = outcome.Seconds < 0.3
                ? $"録音が短すぎます（{outcome.Seconds:0.0}秒）— もう少し長く押してください"
                : outcome.PeakDb < AudioLevel.SilenceDb
                    ? $"無音でした（{outcome.Seconds:0.0}秒 / ピーク {level}）— 入力デバイスを確認"
                    : $"音は入っていますが一致しませんでした（{outcome.Seconds:0.0}秒 / ピーク {level}）";

            SetStatus("マイク停止。もう一度どうぞ。");
            return;
        }

        _heardLabel.ForeColor = outcome.Rejected ? Theme.Warning : Theme.Text;
        _heardLabel.Text = outcome.Rejected
            ? $"確信度不足: 「{outcome.Text}」  ({outcome.Confidence:P0}) — 候補から選んでください"
            : $"聞き取り: 「{outcome.Text}」  ({outcome.Confidence:P0})";

        var via = outcome.Path == "buffered" ? "（録音から再認識）" : "";
        SetStatus(outcome.Rejected
            ? $"一致しきりませんでした。近い候補を出しています。{via}"
            : $"マイク停止{via}");

        var matches = BuildCandidates(outcome);
        Populate(matches);

        // Never jump to a page off a rejected result, and never off a fragment:
        // "broadcast" means six different pages, so the list is the answer.
        var wholeName = _speechIndex?.Exact(outcome.Text) is not null;

        if (!outcome.Rejected
            && wholeName
            && _autoOpen.Checked
            && outcome.Confidence >= AutoOpenConfidence
            && matches.Count > 0
            && matches[0].Score >= 0.99)
        {
            OpenTask(matches[0].Task);
        }
    }

    /// <summary>
    /// Prefer what the engine itself ranked highest, then top the list up with
    /// fuzzy neighbours so a near-miss is still one click away.
    /// </summary>
    private List<TaskMatch> BuildCandidates(RecognitionOutcome outcome)
    {
        var result = new List<TaskMatch>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (_speechIndex is null) return result;

        void Add(TaskMatch match)
        {
            if (seen.Add(match.Task.Name)) result.Add(match);
        }

        var primary = _speechIndex.Exact(outcome.Text);
        if (primary is not null) Add(new TaskMatch(primary, 1.0, outcome.Text));

        foreach (var (text, _) in outcome.Alternates)
        {
            var hit = _speechIndex.Exact(text);
            if (hit is not null) Add(new TaskMatch(hit, 1.0, text));
        }

        // Saying only the start of a name is normal - "broadcast" for
        // "Broadcast - Part 4". A fragment cannot pick one entry, so everything
        // under it is offered.
        foreach (var entry in _speechIndex.StartingWith(outcome.Text))
            Add(new TaskMatch(entry, 1.0, outcome.Text));

        foreach (var (text, _) in outcome.Alternates)
        foreach (var entry in _speechIndex.StartingWith(text))
            Add(new TaskMatch(entry, 1.0, text));

        foreach (var match in _speechIndex.Rank(outcome.Text, 6)) Add(match);

        return result;
    }

    private void ShowCandidatesFor(string text)
    {
        if (_typedIndex is null && _catalog is not null)
            _typedIndex = new TaskIndex(_catalog.On(SelectedWiki), new EnglishScheme());

        if (_typedIndex is null) return;

        if (text.Trim().Length == 0)
        {
            _candidates.Items.Clear();
            _countLabel.Text = "";
            return;
        }

        var starting = _typedIndex.StartingWith(text)
            .Select(e => new TaskMatch(e, 1.0, text))
            .ToList();

        var ranked = _typedIndex.Rank(text, 12)
            .Where(m => starting.All(s => s.Task != m.Task));

        Populate(starting.Concat(ranked).Take(14).ToList());
    }

    private void Populate(IReadOnlyList<TaskMatch> matches)
    {
        _candidates.BeginUpdate();
        _candidates.Items.Clear();
        foreach (var match in matches)
            _candidates.Items.Add(new CandidateRow(match, ReadingHint(match.Task)));
        _candidates.EndUpdate();

        if (_candidates.Items.Count > 0) _candidates.SelectedIndex = 0;
        _countLabel.Text = _candidates.Items.Count > 0 ? $"候補 {_candidates.Items.Count} 件" : "";
    }

    /// <summary>The katakana reading, so the list also teaches how to say it.</summary>
    private string? ReadingHint(WikiEntry task)
    {
        if (SelectedLanguage != RecognitionLanguage.Japanese || _hintForms is null) return null;

        var readings = _hintForms.For(task.Name);
        return readings.Count > 0 ? readings[0] : null;
    }

    private void OpenSelected()
    {
        if (_candidates.SelectedItem is CandidateRow row) OpenTask(row.Match.Task);
    }

    private void OpenTask(WikiEntry task)
    {
        var wiki = SelectedWiki;
        var url = task.Url(wiki);

        if (url is null)
        {
            var label = wiki == WikiSource.Japanese ? "日本語 Wiki" : "英語 Wiki";
            SetStatus($"「{task.Name}」のページは {label} にありません");
            return;
        }

        try
        {
            BrowserLauncher.Open(url, SelectedBrowser);
            SetStatus($"開きました: {task.Name}");
        }
        catch (Exception ex)
        {
            SetStatus($"ブラウザを開けませんでした: {ex.Message}");
        }
    }

    private void SetStatus(string text) => _statusLabel.Text = text;

    private const int WmSettingChange = 0x001A;

    /// <summary>
    /// Windows broadcasts this when the light/dark setting flips. WinForms
    /// restyles the controls itself; the colours this app picks deliberately
    /// have to be re-read.
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        if (m.Msg != WmSettingChange) return;

        var changed = m.LParam == IntPtr.Zero
            ? null
            : System.Runtime.InteropServices.Marshal.PtrToStringAuto(m.LParam);

        if (changed != "ImmersiveColorSet") return;

        BeginInvoke(ReapplyThemeColors);
    }

    /// <summary>
    /// Every colour the app paints itself, re-read. WinForms restyles the system
    /// controls on its own, but the cards, the dial and the candidate rows are
    /// ours, and so are the ones handed to the combo boxes and the title bar.
    /// </summary>
    private void ReapplyThemeColors()
    {
        BackColor = Theme.Page;

        _grammarLabel.ForeColor = Theme.Faint;
        _statusLabel.ForeColor = Theme.Muted;
        _heardLabel.ForeColor = Theme.Muted;
        _micHint.ForeColor = Theme.Faint;
        _countLabel.ForeColor = Theme.Faint;
        _autoOpen.ForeColor = Theme.Muted;
        _noticeLabel.ForeColor = _pendingRelease is null ? Theme.Good : Theme.Info;

        _typedBox.BackColor = Theme.Panel;
        _typedBox.ForeColor = Theme.Text;
        _candidates.BackColor = Theme.Panel;
        _candidates.ForeColor = Theme.Text;

        ApplyCaptionColour();
        Refresh();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        StopLevelTest();
        _speech?.Dispose();
        base.OnFormClosed(e);
    }
}
