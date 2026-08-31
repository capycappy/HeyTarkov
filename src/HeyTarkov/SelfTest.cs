using System.Diagnostics;
using System.Speech.AudioFormat;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Text;

namespace HeyTarkov;

/// <summary>
/// End-to-end check of the recognition path with no human in the loop: the
/// installed voice speaks a task name into a wav, the recognizer (loaded with
/// the real task grammar) transcribes it, and the result is resolved back to a
/// task.
///
/// Synthesized speech is much easier than a real voice on a real mic, so a pass
/// here proves the wiring, not the accuracy.
/// </summary>
public static class SelfTest
{
    private static readonly string[] ProbeTasks =
    {
        "Wet Job - Part 4",
        "Gunsmith - AKS-74N",
        "Gunsmith Master - Part 13",
        "Is This a Reference?",
        "Debut",
        "Shortage",
        "The Punisher - Part 4",
        "What’s on the Flash Drive?",
    };

    /// <summary>Spelled out letter by letter - the escape hatch for a name whose
    /// pronunciation is not obvious.</summary>
    private static readonly string[] SpelledProbeTasks =
    {
        "Fall Ailment",
        "Debut",
        "Shortage",
        "Wet Job - Part 4",
    };

    public static int Run()
    {
        var report = new StringBuilder();
        var reportPath = Path.Combine(TaskCatalog.DataDirectory, "selftest-report.txt");
        var failed = false;

        try
        {
            var catalog = TaskCatalog.Load();

            report.AppendLine($"tasks: {catalog.Entries.Count}");
            report.AppendLine();

            failed |= !CheckUnknownWordFallback(report);
            report.AppendLine();

            failed |= !CheckMapsAndExtracts(report, catalog);
            report.AppendLine();

            failed |= !CheckPrefixes(report, catalog);
            report.AppendLine();

            failed |= !RunLanguage(report, catalog, RecognitionLanguage.English);
            report.AppendLine();
            failed |= !RunLanguage(report, catalog, RecognitionLanguage.Japanese);
        }
        catch (Exception ex)
        {
            report.AppendLine();
            report.AppendLine($"FAILED: {ex}");
            failed = true;
        }
        finally
        {
            File.WriteAllText(reportPath, report.ToString());
        }

        return failed ? 1 : 0;
    }

    /// <summary>
    /// A task added to a wiki tomorrow may use a word the katakana lexicon has
    /// never seen. It must still be reachable by spelling it out, otherwise a
    /// task-list update would make it invisible in Japanese until someone edits
    /// the lexicon by hand.
    /// </summary>
    private static bool CheckUnknownWordFallback(StringBuilder report)
    {
        report.AppendLine("=== unknown-word fallback ===");

        var invented = new WikiEntry
        {
            Name = "Zzyzx Grobnar",   // deliberately absent from the lexicon
            Group = "Test",
            JapaneseUrl = "https://example.invalid/",
        };

        var forms = new JapaneseForms(JapaneseLexicon.Load());
        var scheme = new JapaneseScheme(forms);
        var index = new TaskIndex(new[] { invented }, scheme);

        var wordForms = scheme.Phrases(invented.Name);
        var spelled = forms.SpelledHint(invented.Name);

        report.AppendLine($"task            : \"{invented.Name}\"");
        report.AppendLine($"word readings   : {(wordForms.Count == 0 ? "(none, as expected)" : string.Join(" | ", wordForms))}");
        report.AppendLine($"spelled reading : {spelled ?? "(none)"}");
        report.AppendLine($"unknown words   : {string.Join(", ", scheme.UnknownWords)}");

        var inGrammar = index.TaskCount == 1;
        var flagged = index.SpellOnlyTasks.Count == 1;
        var resolves = spelled is not null && index.Exact(spelled)?.Name == invented.Name;
        var ok = wordForms.Count == 0 && inGrammar && flagged && resolves;

        report.AppendLine($"  {(ok ? "PASS" : "FAIL")}  kept in grammar={inGrammar}, "
                          + $"flagged spell-only={flagged}, spelled form resolves={resolves}");

        return ok;
    }

    /// <summary>
    /// Maps and extracts resolve, and an extract's link carries the anchor that
    /// jumps to its section. An extract can be said with or without its map.
    /// </summary>
    private static bool CheckMapsAndExtracts(StringBuilder report, WikiCatalog catalog)
    {
        report.AppendLine("=== maps and extracts ===");

        var maps = catalog.Entries.Count(e => e.Kind == EntryKind.Map);
        var extracts = catalog.Entries.Count(e => e.Kind == EntryKind.Extract);
        report.AppendLine($"catalog: {maps} maps, {extracts} extracts");

        var index = new TaskIndex(catalog.Entries, new EnglishScheme());
        var ok = true;

        void Check(string said, string expected, EntryKind kind, bool wantAnchor)
        {
            var hit = index.Exact(said);
            var right = hit is not null
                        && hit.Kind == kind
                        && string.Equals(hit.Name, expected, StringComparison.OrdinalIgnoreCase);

            var anchored = hit?.JapaneseUrl?.Contains('#') == true;
            if (wantAnchor && !anchored) right = false;

            ok &= right;

            report.AppendLine($"  {(right ? "PASS" : "FAIL")}  \"{said}\" -> "
                              + $"{hit?.Kind.ToString() ?? "(none)"} {hit?.Display ?? ""}"
                              + (anchored ? "  [anchored]" : ""));
        }

        Check("ground zero", "Ground Zero", EntryKind.Map, wantAnchor: false);
        Check("customs", "Customs", EntryKind.Map, wantAnchor: false);
        Check("ground zero emercom checkpoint", "Emercom Checkpoint", EntryKind.Extract, true);
        Check("emercom checkpoint", "Emercom Checkpoint", EntryKind.Extract, true);
        Check("customs crossroads", "Crossroads", EntryKind.Extract, true);

        // The anchor has to be the one the user was shown originally.
        var known = catalog.Entries.FirstOrDefault(e =>
            e.Kind == EntryKind.Extract && e.Group == "Ground Zero"
            && e.Name == "Emercom Checkpoint" && e.Faction == "PMC");

        var expectedUrl = "https://wikiwiki.jp/eft/GROUND%20ZERO#j60a102a";
        var urlOk = known?.JapaneseUrl == expectedUrl;
        ok &= urlOk;

        report.AppendLine($"  {(urlOk ? "PASS" : "FAIL")}  Emercom Checkpoint (PMC) url");
        report.AppendLine($"        got      {known?.JapaneseUrl ?? "(missing)"}");
        if (!urlOk) report.AppendLine($"        expected {expectedUrl}");

        return ok;
    }

    /// <summary>
    /// Saying the start of a name finds it. A fragment resolves to everything
    /// under it, never to one entry.
    /// </summary>
    private static bool CheckPrefixes(StringBuilder report, WikiCatalog catalog)
    {
        report.AppendLine("=== speaking only the start of a name ===");

        var index = new TaskIndex(catalog.Entries, new EnglishScheme());
        var ok = true;

        void Check(string said, string mustInclude, int atLeast)
        {
            var hits = index.StartingWith(said);
            var names = hits.Select(h => h.Name).ToList();
            var found = names.Any(n => n.StartsWith(mustInclude, StringComparison.OrdinalIgnoreCase));
            var enough = hits.Count >= atLeast;

            ok &= found && enough;

            report.AppendLine($"  {(found && enough ? "PASS" : "FAIL")}  \"{said}\" -> "
                              + $"{hits.Count} entries"
                              + (hits.Count > 0 ? $": {string.Join(", ", names.Take(4))}" : ""));
        }

        Check("broadcast", "Broadcast", 2);
        Check("gunsmith", "Gunsmith", 10);
        Check("wet job", "Wet Job", 4);
        Check("ground zero emercom", "Emercom", 1);

        // A whole name is not a fragment: it must keep resolving to itself.
        var whole = index.Exact("debut");
        var notPrefix = index.StartingWith("debut").Count == 0;
        ok &= whole is not null && notPrefix;

        report.AppendLine($"  {(whole is not null && notPrefix ? "PASS" : "FAIL")}  "
                          + "a whole name still resolves to itself, not a fragment list");

        report.AppendLine($"  grammar with fragments: {index.GrammarPhrases.Count} phrases");
        return ok;
    }

    private static bool RunLanguage(
        StringBuilder report, WikiCatalog catalog, RecognitionLanguage language)
    {
        report.AppendLine($"=== {language} ===");

        var japaneseForms = language == RecognitionLanguage.Japanese
            ? new JapaneseForms(JapaneseLexicon.Load())
            : null;

        IPhraseScheme scheme = japaneseForms is not null
            ? new JapaneseScheme(japaneseForms)
            : new EnglishScheme();

        var index = new TaskIndex(catalog.Entries, scheme);
        report.AppendLine($"covered tasks : {index.TaskCount}/{catalog.Entries.Count}");
        report.AppendLine($"phrases       : {index.GrammarPhrases.Count}");

        if (scheme is JapaneseScheme japanese && japanese.UnknownWords.Count > 0)
            report.AppendLine($"unknown words : {string.Join(", ", japanese.UnknownWords)}");

        var recognizerInfo = SpeechService.FindRecognizer(language);
        if (recognizerInfo is null)
        {
            report.AppendLine("no recognizer installed for this language");
            return false;
        }

        report.AppendLine($"engine        : {recognizerInfo.Culture.Name} / {recognizerInfo.Name}");

        using var engine = new SpeechRecognitionEngine(recognizerInfo);
        SpeechService.Tune(engine);

        var watch = Stopwatch.StartNew();
        Load(engine, recognizerInfo.Culture, "tasks", index.GrammarPhrases);
        report.AppendLine($"grammar load  : {watch.ElapsedMilliseconds} ms "
                          + $"({index.GrammarPhrases.Count} phrases)");

        if (index.DeferredGrammarPhrases.Count > 0)
        {
            watch.Restart();
            Load(engine, recognizerInfo.Culture, "spelled", index.DeferredGrammarPhrases);
            report.AppendLine($"spelled load  : {watch.ElapsedMilliseconds} ms "
                              + $"({index.DeferredGrammarPhrases.Count} phrases)");
        }

        using var synth = new SpeechSynthesizer();
        var voice = synth.GetInstalledVoices()
            .Where(v => v.Enabled)
            .Select(v => v.VoiceInfo)
            .FirstOrDefault(v => v.Culture.Name == recognizerInfo.Culture.Name);

        if (voice is null)
        {
            report.AppendLine($"no {recognizerInfo.Culture.Name} voice available - cannot synthesize");
            return false;
        }

        synth.SelectVoice(voice.Name);
        report.AppendLine($"voice         : {voice.Name}");
        report.AppendLine();

        var passed = 0;
        var attempted = 0;
        var probeNumber = 0;

        foreach (var taskName in ProbeTasks)
        {
            var phrases = scheme.Phrases(taskName);
            if (phrases.Count == 0)
            {
                report.AppendLine($"  SKIP  \"{taskName}\" has no phrase in this language");
                continue;
            }

            // Say it the way a person would: the last English rendering spells
            // numbers out; the first Japanese one is the primary reading.
            var spoken = language == RecognitionLanguage.Japanese ? phrases[0] : phrases[^1];

            Probe(report, catalog, index, engine, synth, language, taskName, spoken,
                ref passed, ref attempted, ref probeNumber);
        }

        if (japaneseForms is not null)
        {
            report.AppendLine();
            report.AppendLine("  -- spelled out --");

            foreach (var taskName in SpelledProbeTasks)
            {
                var spoken = japaneseForms.SpelledHint(taskName);
                if (spoken is null)
                {
                    report.AppendLine($"  SKIP  \"{taskName}\" is too long to spell");
                    continue;
                }

                Probe(report, catalog, index, engine, synth, language, taskName, spoken,
                    ref passed, ref attempted, ref probeNumber);
            }

            // A person spelling a name out loud pauses between the words. Those
            // pauses are what a short end-of-speech timeout mistakes for the end
            // of the utterance, so test them explicitly.
            report.AppendLine();
            report.AppendLine("  -- spelled, with 600ms pauses between words --");

            foreach (var taskName in SpelledProbeTasks)
            {
                var spoken = japaneseForms.SpelledHint(taskName);
                if (spoken is null) continue;

                Probe(report, catalog, index, engine, synth, language, taskName, spoken,
                    ref passed, ref attempted, ref probeNumber, pauseMs: 600);
            }
        }

        report.AppendLine();
        report.AppendLine("  -- live-stream input path (as used by the microphone) --");
        var streamOk = CheckStreamPath(report, index, scheme, engine, synth);
        attempted++;
        if (streamOk) passed++;

        report.AppendLine();
        report.AppendLine($"{language} result: {passed}/{attempted} passed");
        return passed == attempted && attempted > 0;
    }

    /// <summary>
    /// The microphone feeds the recognizer through a Stream of unknown length,
    /// which System.Speech handles differently from a wave file. This checks that
    /// path end to end, since a mistake there shows up as silent non-recognition.
    /// </summary>
    private static bool CheckStreamPath(
        StringBuilder report, TaskIndex index, IPhraseScheme scheme,
        SpeechRecognitionEngine engine, SpeechSynthesizer synth)
    {
        const string taskName = "Debut";

        var phrases = scheme.Phrases(taskName);
        if (phrases.Count == 0)
        {
            report.AppendLine("  SKIP  no phrase for the stream probe");
            return true;
        }

        var format = new SpeechAudioFormatInfo(
            MicrophoneCapture.SampleRate, AudioBitsPerSample.Sixteen, AudioChannel.Mono);

        // Go through a wave file so the bytes are unambiguous, then hand over the
        // raw samples the way the microphone does - trailing silence included,
        // because the recognizer needs an end-of-speech to finalize on.
        var wavPath = Path.Combine(Path.GetTempPath(), "heytarkov-streamprobe.wav");
        synth.SetOutputToWaveFile(wavPath, format);
        synth.Speak(phrases[0]);
        synth.SetOutputToNull();

        var samples = ReadPcm(wavPath);
        TryDelete(wavPath);

        using var pcm = new MemoryStream();
        pcm.Write(samples);
        pcm.Write(new byte[MicrophoneCapture.SampleRate / 1000 * 500 * 2]);   // 500ms silence
        pcm.Position = 0;

        var data = pcm.ToArray();
        var spoken = phrases[0];

        // Same stream contract as MicrophoneCapture: seekable, open-ended length.
        using var live = new UnknownLengthStream(new MemoryStream(data));

        // Must mirror the app: a synchronous Recognize() never returns on a
        // stream whose length is open-ended, because it waits for more audio.
        RecognitionResult? result = null;
        using var completed = new ManualResetEventSlim(false);

        void OnRecognized(object? s, SpeechRecognizedEventArgs e) => result = e.Result;
        void OnCompleted(object? s, RecognizeCompletedEventArgs e) => completed.Set();

        engine.SpeechRecognized += OnRecognized;
        engine.RecognizeCompleted += OnCompleted;

        try
        {
            engine.SetInputToAudioStream(live, format);
            engine.RecognizeAsync(RecognizeMode.Multiple);

            if (!completed.Wait(TimeSpan.FromSeconds(15)))
            {
                engine.RecognizeAsyncCancel();
                report.AppendLine("  FAIL  stream probe timed out");
                return false;
            }
        }
        finally
        {
            engine.SpeechRecognized -= OnRecognized;
            engine.RecognizeCompleted -= OnCompleted;
            engine.SetInputToNull();
        }

        if (result is null)
        {
            report.AppendLine($"  FAIL  said \"{spoken}\" over a live-style stream -> (no recognition)");
            return false;
        }

        var resolved = index.Exact(result.Text);
        var matched = resolved is not null && resolved.Name == taskName;

        report.AppendLine(
            $"  {(matched ? "PASS" : "FAIL")}  said \"{spoken}\" -> heard \"{result.Text}\" "
            + $"({result.Confidence:0.00}) -> {resolved?.Name ?? "(unresolved)"}");

        return matched;
    }

    /// <summary>Returns just the samples from a wave file, skipping to the
    /// "data" chunk rather than assuming a fixed header size.</summary>
    private static byte[] ReadPcm(string wavPath)
    {
        var bytes = File.ReadAllBytes(wavPath);

        for (var i = 12; i + 8 < bytes.Length;)
        {
            var chunkId = System.Text.Encoding.ASCII.GetString(bytes, i, 4);
            var chunkSize = BitConverter.ToInt32(bytes, i + 4);

            if (chunkId == "data")
            {
                var start = i + 8;
                var length = Math.Min(chunkSize, bytes.Length - start);
                return bytes.AsSpan(start, length).ToArray();
            }

            i += 8 + chunkSize + (chunkSize % 2);
        }

        return Array.Empty<byte>();
    }

    /// <summary>Mimics <see cref="MicrophoneStream"/>: no known length, end of
    /// data signalled by a zero-length read.</summary>
    private sealed class UnknownLengthStream(MemoryStream inner) : Stream
    {
        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, count);

        public override bool CanRead => true;
        public override bool CanWrite => false;

        // Both of these mirror LiveAudioStream, and CanSeek in particular is
        // load-bearing: false makes the recognizer return nothing at all.
        public override bool CanSeek => true;
        public override long Length => long.MaxValue;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    private static void Probe(
        StringBuilder report, WikiCatalog catalog, TaskIndex index,
        SpeechRecognitionEngine engine, SpeechSynthesizer synth,
        RecognitionLanguage language, string taskName, string spoken,
        ref int passed, ref int attempted, ref int probeNumber, int pauseMs = 0)
    {
        probeNumber++;

        var expected = catalog.Entries.FirstOrDefault(t =>
            string.Equals(t.Name, taskName, StringComparison.OrdinalIgnoreCase));

        if (expected is null)
        {
            report.AppendLine($"  SKIP  \"{taskName}\" is not in the catalog");
            return;
        }

        attempted++;

        // A fresh file per probe: the recognizer holds the previous wav open
        // until its input is detached.
        var wavPath = Path.Combine(
            Path.GetTempPath(), $"heytarkov-selftest-{language}-{probeNumber}.wav");

        synth.SetOutputToWaveFile(wavPath);
        if (pauseMs > 0) synth.SpeakSsml(WithPauses(spoken, pauseMs, language));
        else synth.Speak(spoken);
        synth.SetOutputToNull();

        engine.SetInputToWaveFile(wavPath);
        var result = engine.Recognize(TimeSpan.FromSeconds(20));
        engine.SetInputToNull();
        TryDelete(wavPath);

        if (result is null)
        {
            report.AppendLine($"  MISS  said \"{spoken}\" -> (no recognition)");
            return;
        }

        var resolved = index.Exact(result.Text);
        var ok = resolved is not null
                 && string.Equals(resolved.Name, expected.Name, StringComparison.OrdinalIgnoreCase);
        if (ok) passed++;

        report.AppendLine(
            $"  {(ok ? "PASS" : "FAIL")}  said \"{spoken}\" -> heard \"{result.Text}\" "
            + $"({result.Confidence:0.00}) -> {resolved?.Name ?? "(unresolved)"}");
    }

    /// <summary>Speaks the words with a real gap between them, the way someone
    /// spelling a name out loud actually sounds.</summary>
    private static string WithPauses(string spoken, int pauseMs, RecognitionLanguage language)
    {
        var lang = language == RecognitionLanguage.Japanese ? "ja-JP" : "en-US";
        var words = spoken.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var body = string.Join($"<break time=\"{pauseMs}ms\"/>",
            words.Select(System.Security.SecurityElement.Escape));

        return $"""
                <speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xml:lang="{lang}">{body}</speak>
                """;
    }

    private static void Load(
        SpeechRecognitionEngine engine, System.Globalization.CultureInfo culture,
        string name, IReadOnlyList<string> phrases)
    {
        var builder = new GrammarBuilder { Culture = culture };
        builder.Append(new Choices(phrases.ToArray()));
        engine.LoadGrammar(new Grammar(builder) { Name = name });
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { /* the engine may still hold it; harmless in temp */ }
    }
}
