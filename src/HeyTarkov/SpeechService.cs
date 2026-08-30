using System.Globalization;
using System.Speech.AudioFormat;
using System.Speech.Recognition;

namespace HeyTarkov;

public sealed record RecognitionOutcome(
    string Text,
    float Confidence,
    IReadOnlyList<(string Text, float Confidence)> Alternates,
    bool Rejected = false)
{
    public static readonly RecognitionOutcome Empty =
        new("", 0f, Array.Empty<(string, float)>());

    public bool IsEmpty => Text.Length == 0;

    /// <summary>Loudest sample of the audio this came from, in dBFS.</summary>
    public double PeakDb { get; init; } = AudioLevel.FloorDb;

    /// <summary>Seconds of audio captured.</summary>
    public double Seconds { get; init; }

    /// <summary>"live" or "buffered" - which pass produced the result.</summary>
    public string Path { get; init; } = "live";
}

/// <summary>
/// Push-to-talk wrapper around the Windows (SAPI) recognizer.
///
/// Two deliberate choices:
///  * The grammar is a closed list of task phrases, so the engine picks one of
///    ~600 known phrases instead of transcribing free speech. That is both far
///    more accurate and far cheaper than a general model.
///  * The microphone is attached on Start() and released on stop, so the app
///    holds no audio device while it is just sitting in the background.
/// </summary>
public sealed class SpeechService : IDisposable
{
    private readonly SpeechRecognitionEngine _engine;
    private readonly System.Threading.Lock _gate = new();

    private RecognitionOutcome _pending = RecognitionOutcome.Empty;
    private RecognitionOutcome _rejected = RecognitionOutcome.Empty;
    private MicrophoneCapture? _microphone;
    private bool _running;

    public SpeechService(RecognizerInfo recognizer)
    {
        _engine = new SpeechRecognitionEngine(recognizer);
        Tune(_engine);

        _engine.SpeechRecognized += OnSpeechRecognized;
        _engine.SpeechRecognitionRejected += OnSpeechRejected;
        _engine.SpeechHypothesized += OnSpeechHypothesized;
        _engine.SpeechDetected += (_, _) => SpeechDetected?.Invoke(this, EventArgs.Empty);
        _engine.RecognizeCompleted += OnRecognizeCompleted;
    }

    /// <summary>Raised on release, once the engine has finished the utterance.</summary>
    public event EventHandler<RecognitionOutcome>? Finished;

    /// <summary>The engine's running guess, while the button is still held.</summary>
    public event EventHandler<string>? Hypothesis;

    public event EventHandler? SpeechDetected;

    public event EventHandler<AudioLevel>? AudioLevel;

    public string RecognizerName => _engine.RecognizerInfo.Name;

    /// <summary>
    /// Shared engine tuning, so the self-test measures the same behaviour the
    /// app has. Spelling a name out loud means real pauses between the letters;
    /// a short end-of-speech timeout cuts the phrase in half and the partial
    /// match is then rejected, so this is deliberately generous.
    /// </summary>
    public static void Tune(SpeechRecognitionEngine engine)
    {
        engine.MaxAlternates = 5;
        engine.EndSilenceTimeout = TimeSpan.FromMilliseconds(1200);
        engine.EndSilenceTimeoutAmbiguous = TimeSpan.FromMilliseconds(1500);
        engine.InitialSilenceTimeout = TimeSpan.Zero;
        engine.BabbleTimeout = TimeSpan.Zero;
    }

    public static RecognizerInfo? FindEnglishRecognizer()
    {
        var installed = SpeechRecognitionEngine.InstalledRecognizers();
        return installed.FirstOrDefault(r => r.Culture.Name == "en-US")
            ?? installed.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == "en");
    }

    public static RecognizerInfo? FindJapaneseRecognizer()
    {
        var installed = SpeechRecognitionEngine.InstalledRecognizers();
        return installed.FirstOrDefault(r => r.Culture.Name == "ja-JP")
            ?? installed.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == "ja");
    }

    public static RecognizerInfo? FindRecognizer(RecognitionLanguage language) =>
        language == RecognitionLanguage.Japanese
            ? FindJapaneseRecognizer()
            : FindEnglishRecognizer();

    public void LoadVocabulary(IReadOnlyList<string> phrases)
    {
        _engine.UnloadAllGrammars();
        AddVocabulary("tasks", phrases);
    }

    /// <summary>
    /// Adds a second grammar alongside the one already loaded. Used for the
    /// spelled-out phrases, which take seconds to compile - the microphone works
    /// off the main grammar while this is still being built.
    /// </summary>
    public void AddVocabulary(string name, IReadOnlyList<string> phrases)
    {
        if (phrases.Count == 0) return;

        var builder = new GrammarBuilder { Culture = _engine.RecognizerInfo.Culture };
        builder.Append(new Choices(phrases.ToArray()));

        _engine.LoadGrammar(new Grammar(builder) { Name = name });
    }

    /// <summary>Which input device to capture from; -1 is the Windows default.</summary>
    public int DeviceIndex { get; set; } = -1;

    /// <summary>Attach the microphone and start listening. Called on button press.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_running) return;

            _pending = RecognitionOutcome.Empty;
            _rejected = RecognitionOutcome.Empty;

            _microphone = new MicrophoneCapture(DeviceIndex);

            // Levels come from our own capture, not the engine: it is the only
            // reading that is true even when the recognizer matches nothing.
            _microphone.Level += (_, level) => AudioLevel?.Invoke(this, level);

            _engine.SetInputToAudioStream(
                _microphone.Stream,
                new SpeechAudioFormatInfo(
                    MicrophoneCapture.SampleRate, AudioBitsPerSample.Sixteen, AudioChannel.Mono));

            _engine.RecognizeAsync(RecognizeMode.Multiple);
            _running = true;
        }
    }

    /// <summary>
    /// Stop capturing. Ending the audio stream is what finalizes the utterance,
    /// so the result comes back as soon as the trailing silence is consumed
    /// rather than after a silence timer.
    /// </summary>
    public void Stop()
    {
        lock (_gate)
        {
            if (!_running) return;
            _microphone?.Finish();
        }

        // If the engine never completes (stalled device, driver hiccup), force it.
        _ = Task.Delay(3000).ContinueWith(_ =>
        {
            lock (_gate)
            {
                if (_running) _engine.RecognizeAsyncCancel();
            }
        });
    }

    private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        _pending = Describe(e.Result, rejected: false);
    }

    /// <summary>
    /// A rejection still carries the engine's best guess. Keeping it means the
    /// user sees what was heard instead of a bare "couldn't understand", and the
    /// fuzzy matcher gets something to work with.
    /// </summary>
    private void OnSpeechRejected(object? sender, SpeechRecognitionRejectedEventArgs e)
    {
        if (e.Result is null) return;
        _rejected = Describe(e.Result, rejected: true);
    }

    private void OnSpeechHypothesized(object? sender, SpeechHypothesizedEventArgs e)
    {
        if (e.Result.Text.Length > 0) Hypothesis?.Invoke(this, e.Result.Text);
    }

    /// <summary>
    /// Second chance for an utterance the live stream produced nothing from.
    /// Same audio, same grammar, but fed as a wave file - a path with none of
    /// the timing sensitivity of a stream that is still filling.
    /// </summary>
    private RecognitionOutcome? RetryFromBuffer(byte[] pcm)
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"heytarkov-retry-{Environment.ProcessId}.wav");

        try
        {
            WaveFile.Write(path, pcm, MicrophoneCapture.SampleRate);

            RecognitionResult? best = null;
            RecognitionResult? rejected = null;

            void OnRecognized(object? s, SpeechRecognizedEventArgs e) => best ??= e.Result;
            void OnRejected(object? s, SpeechRecognitionRejectedEventArgs e) =>
                rejected ??= e.Result;

            _engine.SpeechRecognized += OnRecognized;
            _engine.SpeechRecognitionRejected += OnRejected;

            try
            {
                _engine.SetInputToWaveFile(path);
                _engine.Recognize(TimeSpan.FromSeconds(20));
            }
            finally
            {
                _engine.SpeechRecognized -= OnRecognized;
                _engine.SpeechRecognitionRejected -= OnRejected;

                try { _engine.SetInputToNull(); }
                catch (InvalidOperationException) { /* already detached */ }
            }

            if (best is not null) return Describe(best, rejected: false) with { Path = "buffered" };
            if (rejected is not null) return Describe(rejected, rejected: true) with { Path = "buffered" };

            return null;
        }
        catch (Exception)
        {
            return null;   // the live result (or lack of one) still stands
        }
        finally
        {
            try { File.Delete(path); }
            catch (IOException) { }
        }
    }

    private static RecognitionOutcome Describe(RecognitionResult result, bool rejected)
    {
        var alternates = result.Alternates
            .Select(a => (a.Text, a.Confidence))
            .Where(a => a.Text.Length > 0)
            .ToArray();

        return new RecognitionOutcome(result.Text, result.Confidence, alternates, rejected);
    }

    private void OnRecognizeCompleted(object? sender, RecognizeCompletedEventArgs e)
    {
        RecognitionOutcome outcome;
        byte[] captured;

        lock (_gate)
        {
            if (!_running) return;
            _running = false;

            try { _engine.SetInputToNull(); }
            catch (InvalidOperationException) { /* already detached */ }

            // Keep the audio: if the live pass found nothing it gets one more
            // try as a wave file, which is the input System.Speech handles most
            // predictably.
            captured = _microphone?.Stream.Snapshot() ?? Array.Empty<byte>();

            _microphone?.Dispose();
            _microphone = null;

            // Fall back to the rejected guess so the user always sees something.
            outcome = _pending.IsEmpty ? _rejected : _pending;
            _pending = RecognitionOutcome.Empty;
            _rejected = RecognitionOutcome.Empty;
        }

        var seconds = captured.Length / 2.0 / MicrophoneCapture.SampleRate;
        var peakDb = WaveFile.PeakDb(captured);

        if (outcome.IsEmpty && captured.Length > 0)
            outcome = RetryFromBuffer(captured) ?? outcome;

        Finished?.Invoke(this, outcome with { PeakDb = peakDb, Seconds = seconds });
    }

    public void Dispose()
    {
        try
        {
            lock (_gate)
            {
                if (_running) _engine.RecognizeAsyncCancel();
                _microphone?.Dispose();
                _microphone = null;
            }
        }
        catch (Exception) { /* shutting down anyway */ }

        _engine.Dispose();
    }

    public static string InstalledRecognizerSummary()
    {
        var installed = SpeechRecognitionEngine.InstalledRecognizers();
        if (installed.Count == 0) return "(なし)";
        return string.Join(", ", installed.Select(r =>
            $"{r.Culture.Name} ({r.Name})".ToString(CultureInfo.InvariantCulture)));
    }
}
