using System.Speech.AudioFormat;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Text;

namespace HeyTarkov;

/// <summary>
/// Isolates one question: can this recognizer recognize from a Stream at all?
/// The microphone path depends on it, and a failure there looks identical to
/// "the mic isn't picking anything up".
///
/// Each case gets a fresh engine and a one-phrase grammar, with wave-file input
/// as the control.
/// </summary>
public static class StreamDiagnostic
{
    private const string Phrase = "デビュー";

    public static int Run()
    {
        var report = new StringBuilder();
        var reportPath = Path.Combine(TaskCatalog.DataDirectory, "stream-diagnostic.txt");

        try
        {
            var recognizer = SpeechService.FindRecognizer(RecognitionLanguage.Japanese);
            if (recognizer is null)
            {
                report.AppendLine("no ja-JP recognizer");
                return 1;
            }

            var format = new SpeechAudioFormatInfo(
                MicrophoneCapture.SampleRate, AudioBitsPerSample.Sixteen, AudioChannel.Mono);

            var wavPath = Path.Combine(Path.GetTempPath(), "heytarkov-streamdiag.wav");
            using (var synth = new SpeechSynthesizer())
            {
                var voice = synth.GetInstalledVoices()
                    .Where(v => v.Enabled)
                    .Select(v => v.VoiceInfo)
                    .FirstOrDefault(v => v.Culture.Name == "ja-JP");

                if (voice is null)
                {
                    report.AppendLine("no ja-JP voice");
                    return 1;
                }

                synth.SelectVoice(voice.Name);
                synth.SetOutputToWaveFile(wavPath, format);
                synth.Speak(Phrase);
                synth.SetOutputToNull();
            }

            report.AppendLine($"phrase: {Phrase}");
            report.AppendLine($"wav   : {new FileInfo(wavPath).Length} bytes");
            report.AppendLine();

            report.AppendLine("A) wave file, sync Recognize      : "
                              + WaveFileCase(recognizer, wavPath));
            report.AppendLine("B) raw stream, async Multiple     : "
                              + StreamCase(recognizer, wavPath, format, RecognizeMode.Multiple, raw: true));
            report.AppendLine("C) raw stream, async Single       : "
                              + StreamCase(recognizer, wavPath, format, RecognizeMode.Single, raw: true));
            report.AppendLine("D) whole wav bytes, async Multiple: "
                              + StreamCase(recognizer, wavPath, format, RecognizeMode.Multiple, raw: false));
            report.AppendLine("E) whole wav bytes, no format arg : "
                              + WavStreamNoFormatCase(recognizer, wavPath));
            report.AppendLine();
            report.AppendLine("F) ONE engine reused for 3 stream recognitions (what the app does):");
            foreach (var line in ReuseCase(recognizer, wavPath, format))
                report.AppendLine("   " + line);
            report.AppendLine();
            report.AppendLine("G) after a wave-file recognition, same engine on a stream:");
            report.AppendLine("   " + AfterWaveFileCase(recognizer, wavPath, format));

            report.AppendLine();
            report.AppendLine("--- does CanSeek matter? (the microphone cannot seek) ---");
            report.AppendLine("H) CanSeek=false               : "
                              + WrappedCase(recognizer, wavPath, format, canSeek: false));
            report.AppendLine("I) CanSeek=true, Length=exact  : "
                              + WrappedCase(recognizer, wavPath, format, canSeek: true));
            report.AppendLine("J) CanSeek=true, Length=long.Max: "
                              + WrappedCase(recognizer, wavPath, format, true, long.MaxValue));
            report.AppendLine("K) CanSeek=true, Length=int.Max : "
                              + WrappedCase(recognizer, wavPath, format, true, int.MaxValue));
            report.AppendLine("L) CanSeek=true, Length=exact*10: "
                              + WrappedCase(recognizer, wavPath, format, true,
                                  Pcm(wavPath).Length * 10L));

            report.AppendLine();
            report.AppendLine("--- partial reads (a live mic can only return what has arrived) ---");
            report.AppendLine("M) returns <= 1024 bytes per Read      : "
                              + ChunkedCase(recognizer, wavPath, format, 1024, 0));
            report.AppendLine("N) <= 1024 bytes + 40ms delay per Read : "
                              + ChunkedCase(recognizer, wavPath, format, 1024, 40));
            report.AppendLine("O) fills the whole request, 40ms delay  : "
                              + ChunkedCase(recognizer, wavPath, format, int.MaxValue, 40));

            report.AppendLine();
            report.AppendLine("--- the real LiveAudioStream, fed the way the mic feeds it ---");
            report.AppendLine("P) 50ms chunks arriving in real time  : "
                              + LiveCase(recognizer, wavPath, format));

            File.Delete(wavPath);
            File.WriteAllText(reportPath, report.ToString());
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(reportPath, report + "\n\nFAILED: " + ex);
            return 1;
        }
    }

    private static SpeechRecognitionEngine NewEngine(RecognizerInfo info)
    {
        var engine = new SpeechRecognitionEngine(info);
        SpeechService.Tune(engine);

        var builder = new GrammarBuilder { Culture = info.Culture };
        builder.Append(new Choices(Phrase));
        engine.LoadGrammar(new Grammar(builder));

        return engine;
    }

    private static string WaveFileCase(RecognizerInfo info, string wavPath)
    {
        using var engine = NewEngine(info);
        engine.SetInputToWaveFile(wavPath);
        var result = engine.Recognize(TimeSpan.FromSeconds(15));
        return Describe(result);
    }

    private static string StreamCase(
        RecognizerInfo info, string wavPath, SpeechAudioFormatInfo format,
        RecognizeMode mode, bool raw)
    {
        var bytes = raw ? Pcm(wavPath) : File.ReadAllBytes(wavPath);
        using var stream = new MemoryStream(bytes);

        using var engine = NewEngine(info);
        engine.SetInputToAudioStream(stream, format);
        return RunAsync(engine, mode);
    }

    private static string WavStreamNoFormatCase(RecognizerInfo info, string wavPath)
    {
        using var stream = new MemoryStream(File.ReadAllBytes(wavPath));
        using var engine = NewEngine(info);

        // The wave header carries the format, so let the wrapper read it.
        engine.SetInputToWaveStream(stream);
        return RunAsync(engine, RecognizeMode.Multiple);
    }

    /// <summary>The real usage pattern: one long-lived engine, a new capture
    /// stream on every button press.</summary>
    private static List<string> ReuseCase(
        RecognizerInfo info, string wavPath, SpeechAudioFormatInfo format)
    {
        var lines = new List<string>();
        var pcm = Pcm(wavPath);

        using var engine = NewEngine(info);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var stream = new MemoryStream(pcm);
            engine.SetInputToAudioStream(stream, format);
            lines.Add($"attempt {attempt}: {RunAsync(engine, RecognizeMode.Multiple)}");
            engine.SetInputToNull();
        }

        return lines;
    }

    private static string AfterWaveFileCase(
        RecognizerInfo info, string wavPath, SpeechAudioFormatInfo format)
    {
        using var engine = NewEngine(info);

        engine.SetInputToWaveFile(wavPath);
        engine.Recognize(TimeSpan.FromSeconds(15));
        engine.SetInputToNull();

        using var stream = new MemoryStream(Pcm(wavPath));
        engine.SetInputToAudioStream(stream, format);
        var outcome = RunAsync(engine, RecognizeMode.Multiple);
        engine.SetInputToNull();

        return outcome;
    }

    private static string WrappedCase(
        RecognizerInfo info, string wavPath, SpeechAudioFormatInfo format, bool canSeek,
        long? reportedLength = null)
    {
        using var engine = NewEngine(info);
        using var stream = new WrappedStream(new MemoryStream(Pcm(wavPath)), canSeek, reportedLength);

        try
        {
            engine.SetInputToAudioStream(stream, format);
            return RunAsync(engine, RecognizeMode.Multiple);
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>A MemoryStream that can pretend not to be seekable, which is the
    /// one thing a live microphone cannot do.</summary>
    private sealed class WrappedStream(MemoryStream inner, bool canSeek, long? reportedLength)
        : Stream
    {
        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, count);

        public override bool CanRead => true;
        public override bool CanSeek => canSeek;
        public override bool CanWrite => false;
        public override long Length => reportedLength ?? inner.Length;

        public override long Position
        {
            get => inner.Position;
            set
            {
                if (!canSeek) throw new NotSupportedException();
                inner.Position = value;
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (!canSeek) throw new NotSupportedException();
            return inner.Seek(offset, origin);
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    /// <summary>
    /// Drives the actual LiveAudioStream the microphone uses, with audio
    /// appended in 50 ms slices on a background thread - the same shape as a
    /// real capture, minus the hardware.
    /// </summary>
    private static string LiveCase(
        RecognizerInfo info, string wavPath, SpeechAudioFormatInfo format)
    {
        var pcm = Pcm(wavPath);
        var chunk = MicrophoneCapture.SampleRate * 2 / 1000 * 50;

        using var engine = NewEngine(info);
        using var live = new LiveAudioStream();

        var feeder = new Thread(() =>
        {
            for (var offset = 0; offset < pcm.Length; offset += chunk)
            {
                Thread.Sleep(50);
                live.Append(pcm, offset, Math.Min(chunk, pcm.Length - offset));
            }

            Thread.Sleep(50);
            live.Finish(MicrophoneCapture.SampleRate * 2 / 1000 * 400);
        }) { IsBackground = true };

        try
        {
            engine.SetInputToAudioStream(live, format);
            feeder.Start();
            return RunAsync(engine, RecognizeMode.Multiple);
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    private static string ChunkedCase(
        RecognizerInfo info, string wavPath, SpeechAudioFormatInfo format,
        int maxPerRead, int delayMs)
    {
        using var engine = NewEngine(info);
        using var stream = new ChunkedStream(Pcm(wavPath), maxPerRead, delayMs);

        try
        {
            engine.SetInputToAudioStream(stream, format);
            return RunAsync(engine, RecognizeMode.Multiple);
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>
    /// Seekable and open-ended like LiveAudioStream, but able to hand back less
    /// than was asked for - which is all a live capture can ever do.
    /// </summary>
    private sealed class ChunkedStream(byte[] data, int maxPerRead, int delayMs) : Stream
    {
        private int _position;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (delayMs > 0) Thread.Sleep(delayMs);

            var available = data.Length - _position;
            if (available <= 0) return 0;

            var taken = Math.Min(Math.Min(count, maxPerRead), available);
            Array.Copy(data, _position, buffer, offset, taken);
            _position += taken;
            return taken;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => long.MaxValue;

        public override long Position
        {
            get => _position;
            set => _position = (int)Math.Clamp(value, 0, data.Length);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                _ => data.Length + offset,
            };
            _position = (int)Math.Clamp(target, 0, data.Length);
            return _position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    private static string RunAsync(SpeechRecognitionEngine engine, RecognizeMode mode)
    {
        RecognitionResult? result = null;
        using var completed = new ManualResetEventSlim(false);

        // Unsubscribe afterwards: this engine gets reused across attempts.
        void OnRecognized(object? s, SpeechRecognizedEventArgs e) => result = e.Result;
        void OnCompleted(object? s, RecognizeCompletedEventArgs e) => completed.Set();

        engine.SpeechRecognized += OnRecognized;
        engine.RecognizeCompleted += OnCompleted;

        try
        {
            engine.RecognizeAsync(mode);

            if (!completed.Wait(TimeSpan.FromSeconds(15)))
            {
                engine.RecognizeAsyncCancel();
                return "timed out";
            }

            return Describe(result);
        }
        finally
        {
            engine.SpeechRecognized -= OnRecognized;
            engine.RecognizeCompleted -= OnCompleted;
        }
    }

    private static string Describe(RecognitionResult? result) =>
        result is null ? "NO RECOGNITION" : $"OK  \"{result.Text}\" ({result.Confidence:0.00})";

    private static byte[] Pcm(string wavPath)
    {
        var bytes = File.ReadAllBytes(wavPath);

        for (var i = 12; i + 8 < bytes.Length;)
        {
            var id = System.Text.Encoding.ASCII.GetString(bytes, i, 4);
            var size = BitConverter.ToInt32(bytes, i + 4);

            if (id == "data")
            {
                var start = i + 8;
                return bytes.AsSpan(start, Math.Min(size, bytes.Length - start)).ToArray();
            }

            i += 8 + size + (size % 2);
        }

        return Array.Empty<byte>();
    }
}
