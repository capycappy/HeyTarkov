using System.Text;

namespace HeyTarkov;

/// <summary>
/// Records from a real microphone through the app's own SpeechService and
/// reports every stage: how much audio arrived, how loud it was, what the
/// engine guessed while listening, and what came back at the end.
///
/// This is the one check the synthetic self-test cannot make, and it is what
/// separates "the mic is not reaching the app" from "the audio was fine but the
/// grammar did not match".
/// </summary>
public static class MicTest
{
    public static int Run(int seconds, int deviceIndex, RecognitionLanguage language)
    {
        var report = new StringBuilder();
        var reportPath = Path.Combine(TaskCatalog.DataDirectory, "mictest-report.txt");

        try
        {
            var catalog = TaskCatalog.Load();

            IPhraseScheme scheme = language == RecognitionLanguage.Japanese
                ? new JapaneseScheme(new JapaneseForms(JapaneseLexicon.Load()))
                : new EnglishScheme();

            var index = new TaskIndex(catalog.Tasks, scheme);

            var recognizerInfo = SpeechService.FindRecognizer(language);
            if (recognizerInfo is null)
            {
                report.AppendLine($"no recognizer installed for {language}");
                return 1;
            }

            var device = AudioInput.Devices()
                .FirstOrDefault(d => d.Index == deviceIndex) ?? new AudioDevice(deviceIndex, "?");

            report.AppendLine($"language : {language}");
            report.AppendLine($"engine   : {recognizerInfo.Culture.Name} / {recognizerInfo.Name}");
            report.AppendLine($"device   : [{device.Index}] {device}");
            report.AppendLine($"grammar  : {index.GrammarPhrases.Count} phrases "
                              + $"+ {index.DeferredGrammarPhrases.Count} spelled");
            report.AppendLine();
            report.AppendLine($">>> SPEAK NOW - recording for {seconds} seconds <<<");
            report.AppendLine();

            using var speech = new SpeechService(recognizerInfo) { DeviceIndex = deviceIndex };
            speech.LoadVocabulary(index.GrammarPhrases);
            speech.AddVocabulary("spelled", index.DeferredGrammarPhrases);

            var hypotheses = new List<string>();
            var detected = false;
            var peakDb = AudioLevel.FloorDb;
            RecognitionOutcome? outcome = null;
            using var finished = new ManualResetEventSlim(false);

            speech.Hypothesis += (_, text) =>
            {
                if (hypotheses.Count == 0 || hypotheses[^1] != text) hypotheses.Add(text);
            };
            speech.SpeechDetected += (_, _) => detected = true;
            speech.AudioLevel += (_, level) =>
            {
                if (level.PeakDb > peakDb) peakDb = level.PeakDb;
            };
            speech.Finished += (_, result) => { outcome = result; finished.Set(); };

            speech.Start();
            Thread.Sleep(seconds * 1000);
            speech.Stop();

            var completed = finished.Wait(TimeSpan.FromSeconds(20));

            report.AppendLine($"audio captured : {outcome?.Seconds ?? 0:0.00}s");
            report.AppendLine($"peak level     : {peakDb:0} dBFS ({AudioLevel.Verdict(peakDb)})");
            report.AppendLine($"speech detected: {detected}");
            report.AppendLine($"hypotheses     : {(hypotheses.Count == 0 ? "(none)" : string.Join(" | ", hypotheses))}");
            report.AppendLine($"completed      : {completed}");
            report.AppendLine();

            if (outcome is null || outcome.IsEmpty)
            {
                report.AppendLine("RESULT: nothing recognized");
                report.AppendLine(peakDb < AudioLevel.SilenceDb
                    ? "  -> the audio was silent: wrong input device, or the mic is muted"
                    : "  -> audio arrived but matched no phrase in the grammar");
            }
            else
            {
                var resolved = index.Exact(outcome.Text);
                report.AppendLine($"RESULT: \"{outcome.Text}\" ({outcome.Confidence:0.00}) "
                                  + $"via {outcome.Path}"
                                  + (outcome.Rejected ? " [rejected]" : ""));
                report.AppendLine($"  -> {resolved?.Name ?? "(unresolved)"}");

                foreach (var (text, confidence) in outcome.Alternates.Take(5))
                    report.AppendLine($"     alt: \"{text}\" ({confidence:0.00})");
            }

            return 0;
        }
        catch (Exception ex)
        {
            report.AppendLine();
            report.AppendLine($"FAILED: {ex}");
            return 1;
        }
        finally
        {
            File.WriteAllText(reportPath, report.ToString());
        }
    }
}
