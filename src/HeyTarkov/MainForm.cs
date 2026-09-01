namespace HeyTarkov;

public sealed class MainForm : Form
{
    private const double AutoOpenConfidence = 0.55;
    private const string MicIdleText = "\U0001F3A4  押している間だけ聞き取ります";
    private const string MicActiveText = "● 聞き取り中… 離すと検索します";


    private readonly ComboBox _languageBox = new();
    private readonly ComboBox _wikiBox = new();
    private readonly ComboBox _browserBox = new();
    private readonly ComboBox _deviceBox = new();
    private readonly Button _rescanButton = new();
    private readonly Button _levelTestButton = new();
    private readonly Label _grammarLabel = new();
    private readonly Label _noticeLabel = new();
    private readonly Button _micButton = new();
    private readonly ProgressBar _levelBar = new();
    private readonly Label _heardLabel = new();
    private readonly TextBox _typedBox = new();
    private readonly ListBox _candidates = new();
    private readonly Button _openButton = new();
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

        MinimumSize = new Size(600, 660);
        Size = new Size(680, 740);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Yu Gothic UI", 9.75f);

        ApplyIcon();
        BuildLayout();
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

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 11,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        ConfigureMic();
        ConfigureResults();

        root.Controls.Add(BuildChoiceRow());
        root.Controls.Add(BuildDeviceRow());
        root.Controls.Add(BuildStateLabels());
        root.Controls.Add(_micButton);
        root.Controls.Add(_levelBar);
        root.Controls.Add(_heardLabel);
        root.Controls.Add(_typedBox);
        root.Controls.Add(_candidates);
        root.Controls.Add(_openButton);
        root.Controls.Add(BuildBottomRow());
        root.Controls.Add(_statusLabel);

        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // language / wiki / browser
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // input device
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // grammar + notice
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // mic
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));   // level
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));   // heard
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // typed
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // candidates
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // open
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // bottom
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));   // status

        Controls.Add(root);
    }

    /// <summary>
    /// A grid, not a flow: the combo boxes give up width as the window narrows,
    /// so the buttons on the right can never be pushed off the edge.
    /// </summary>
    private static TableLayoutPanel Grid(int columns)
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = columns,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 6),
        };
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return grid;
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 6, 6, 0),
    };

    private TableLayoutPanel BuildChoiceRow()
    {
        // Anchored, not docked. Dock.Fill stretches a ComboBox to the row
        // height and the bottom edge ends up clipped; anchoring left+right
        // stretches the width only and keeps its natural height.
        _languageBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _languageBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _languageBox.Margin = new Padding(0, 3, 12, 3);
        _languageBox.Items.AddRange(new object[] { "日本語で言う", "英語で言う" });
        _languageBox.SelectedIndex = 0;
        _languageBox.SelectedIndexChanged += async (_, _) => await OnLanguageChangedAsync();

        _wikiBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _wikiBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _wikiBox.Margin = new Padding(0, 3, 12, 3);
        _wikiBox.Items.AddRange(new object[] { "日本語 Wiki", "英語 Wiki" });
        _wikiBox.SelectedIndex = 0;
        _wikiBox.SelectedIndexChanged += async (_, _) => await OnWikiChangedAsync();

        _browserBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _browserBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _browserBox.Margin = new Padding(0, 3, 0, 3);
        _browserBox.SelectedIndexChanged += (_, _) => OnBrowserChanged();

        var grid = Grid(6);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));

        grid.Controls.Add(FieldLabel("言語"), 0, 0);
        grid.Controls.Add(_languageBox, 1, 0);
        grid.Controls.Add(FieldLabel("Wiki"), 2, 0);
        grid.Controls.Add(_wikiBox, 3, 0);
        grid.Controls.Add(FieldLabel("ブラウザ"), 4, 0);
        grid.Controls.Add(_browserBox, 5, 0);

        return grid;
    }

    private TableLayoutPanel BuildDeviceRow()
    {
        var buttonSize = new Size(96, 26);

        _deviceBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _deviceBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _deviceBox.Margin = new Padding(0, 3, 6, 3);
        _deviceBox.SelectedIndexChanged += (_, _) => OnDeviceChanged();

        // Identical fixed sizes: the level button's caption toggles between
        // "レベル確認" and "確認を停止", and an auto-sized button would shift the
        // row every time it changed.
        _rescanButton.Text = "再検出";
        _rescanButton.AutoSize = false;
        _rescanButton.Size = buttonSize;
        _rescanButton.Anchor = AnchorStyles.Left;
        _rescanButton.Margin = new Padding(0, 2, 6, 2);
        _rescanButton.Click += (_, _) => LoadDevices(_settings.InputDeviceName);

        _levelTestButton.Text = "レベル確認";
        _levelTestButton.AutoSize = false;
        _levelTestButton.Size = buttonSize;
        _levelTestButton.Anchor = AnchorStyles.Left;
        _levelTestButton.Margin = new Padding(0, 2, 0, 2);
        _levelTestButton.Click += (_, _) => ToggleLevelTest();

        var grid = Grid(4);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        grid.Controls.Add(FieldLabel("入力"), 0, 0);
        grid.Controls.Add(_deviceBox, 1, 0);
        grid.Controls.Add(_rescanButton, 2, 0);
        grid.Controls.Add(_levelTestButton, 3, 0);

        return grid;
    }

    private FlowLayoutPanel BuildStateLabels()
    {
        _grammarLabel.AutoSize = true;
        _grammarLabel.ForeColor = Theme.Muted;
        _grammarLabel.Margin = new Padding(0, 0, 0, 2);

        _noticeLabel.AutoSize = true;
        _noticeLabel.ForeColor = Theme.Good;
        _noticeLabel.Margin = new Padding(0, 0, 0, 4);
        _noticeLabel.Visible = false;
        _noticeLabel.Cursor = Cursors.Hand;
        _noticeLabel.Click += (_, _) => OnNoticeClicked();

        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 4),
        };
        stack.Controls.Add(_grammarLabel);
        stack.Controls.Add(_noticeLabel);

        return stack;
    }

    private void ConfigureMic()
    {
        _micButton.Text = MicIdleText;
        _micButton.Height = 92;
        _micButton.Dock = DockStyle.Fill;
        _micButton.Font = new Font("Yu Gothic UI", 13f, FontStyle.Bold);
        _micButton.Enabled = false;
        _micButton.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) BeginHold(); };
        _micButton.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) EndHold(); };
        _micButton.KeyDown += (_, e) => { if (e.KeyCode == Keys.Space) BeginHold(); };
        _micButton.KeyUp += (_, e) => { if (e.KeyCode == Keys.Space) EndHold(); };

        _levelBar.Dock = DockStyle.Fill;
        _levelBar.Maximum = 100;
        _levelBar.Style = ProgressBarStyle.Continuous;
        _levelBar.Margin = new Padding(0, 6, 0, 6);

        _heardLabel.Dock = DockStyle.Fill;
        _heardLabel.TextAlign = ContentAlignment.MiddleLeft;
        _heardLabel.ForeColor = Theme.Muted;
        _heardLabel.Text = "聞き取り結果はここに出ます";
    }

    private void ConfigureResults()
    {
        _typedBox.Dock = DockStyle.Fill;
        _typedBox.PlaceholderText = "キーボードで探す（英語表記: wet job part 4）";
        _typedBox.Margin = new Padding(0, 4, 0, 8);
        _typedBox.TextChanged += (_, _) => ShowCandidatesFor(_typedBox.Text);
        _typedBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            OpenSelected();
        };

        _candidates.Dock = DockStyle.Fill;
        _candidates.IntegralHeight = false;
        _candidates.DoubleClick += (_, _) => OpenSelected();
        _candidates.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            OpenSelected();
        };

        _openButton.Text = "選択したページをブラウザで開く";
        _openButton.Dock = DockStyle.Fill;
        _openButton.Height = 36;
        _openButton.Click += (_, _) => OpenSelected();

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.ForeColor = Theme.Muted;
        _statusLabel.Text = "起動中…";
    }

    private FlowLayoutPanel BuildBottomRow()
    {
        _autoOpen.Text = "確信度が高いときは自動で開く";
        _autoOpen.Checked = true;
        _autoOpen.AutoSize = true;
        _autoOpen.Margin = new Padding(0, 8, 16, 0);
        _autoOpen.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.AutoOpen = _autoOpen.Checked;
            _settings.Save();
        };

        _themeBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _themeBox.Width = 120;
        _themeBox.Margin = new Padding(0, 5, 0, 4);
        _themeBox.Items.AddRange(new object[] { "システムに従う", "ライト", "ダーク" });
        _themeBox.SelectedIndex = 0;
        _themeBox.SelectedIndexChanged += (_, _) => OnThemeChanged();

        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 6, 0, 0),
        };
        row.Controls.Add(_autoOpen);
        row.Controls.Add(new Label
        {
            Text = "表示",
            AutoSize = true,
            Margin = new Padding(0, 9, 6, 0),
        });
        row.Controls.Add(_themeBox);

        return row;
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
            _micButton.Enabled = false;
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
        _levelBar.Value = AudioLevel.ToMeter(level.PeakDb);

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

        _levelBar.Value = 0;
        _levelTestButton.Text = "レベル確認";
        _micButton.Enabled = _speech is not null;
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

        _micButton.Enabled = false;
        _micButton.Text = "音声認識を準備中…";
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
            _micButton.Enabled = false;
            _micButton.Text = language == RecognitionLanguage.Japanese
                ? "日本語の音声認識が未インストールです"
                : "英語の音声認識が未インストールです";
            SetStatus($"利用可能な認識エンジン: {SpeechService.InstalledRecognizerSummary()}");
            return;
        }

        if (setup.Speech is null)
        {
            _micButton.Enabled = false;
            _micButton.Text = "音声認識を初期化できませんでした";
            if (setup.Error is not null) SetStatus($"音声認識の初期化に失敗しました: {setup.Error}");
            return;
        }

        _speech = setup.Speech;
        _speech.DeviceIndex = SelectedDevice?.Index ?? -1;
        _speech.AudioLevel += (_, level) =>
            BeginInvoke(() => _levelBar.Value = AudioLevel.ToMeter(level.PeakDb));
        _speech.Finished += (_, outcome) =>
            BeginInvoke(() => OnRecognitionFinished(outcome));
        _speech.Hypothesis += (_, text) =>
            BeginInvoke(() => ShowLive(text));
        _speech.SpeechDetected += (_, _) =>
            BeginInvoke(() => { if (_holding) _heardLabel.Text = "…（音声を検出）"; });

        _micButton.Enabled = true;
        _micButton.Text = MicIdleText;
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
            _micButton.Text = MicActiveText;
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
        _micButton.Text = MicIdleText;
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
        _levelBar.Value = 0;

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

    private void ReapplyThemeColors()
    {
        _grammarLabel.ForeColor = Theme.Muted;
        _statusLabel.ForeColor = Theme.Muted;
        _heardLabel.ForeColor = Theme.Muted;
        _noticeLabel.ForeColor = _pendingRelease is null ? Theme.Good : Theme.Info;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        StopLevelTest();
        _speech?.Dispose();
        base.OnFormClosed(e);
    }

    private sealed class CandidateRow(TaskMatch match, string? reading)
    {
        public TaskMatch Match { get; } = match;

        public override string ToString()
        {
            // Kind first, because "Ground Zero" as a map and as the map an
            // extract belongs to are different answers to the same words.
            var kind = Match.Task.Kind switch
            {
                EntryKind.Map => "[マップ] ",
                EntryKind.Extract => "[出口] ",
                _ => "",
            };

            var group = Match.Task.Group.Length > 0 ? $"  /  {Match.Task.Group}" : "";
            var row = $"{kind}{Match.Task.Display}{group}   [{Match.Score:P0}]";

            return reading is null ? row : $"{row}   {reading}";
        }
    }
}
