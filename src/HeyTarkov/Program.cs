using System.Text;

namespace HeyTarkov;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--selftest", StringComparer.OrdinalIgnoreCase))
            return SelfTest.Run();

        if (args.Contains("--devices", StringComparer.OrdinalIgnoreCase))
            return CheckDevices();

        if (args.Contains("--streamdiag", StringComparer.OrdinalIgnoreCase))
            return StreamDiagnostic.Run();

        if (args.Contains("--checkupdate", StringComparer.OrdinalIgnoreCase))
            return UpdateDiagnostic.Run();

        // --mictest [seconds] [deviceIndex] [en|ja] - records from a real
        // microphone through the app's own recognition path.
        if (args.Contains("--mictest", StringComparer.OrdinalIgnoreCase))
        {
            var rest = args.SkipWhile(a =>
                !a.Equals("--mictest", StringComparison.OrdinalIgnoreCase)).Skip(1).ToArray();

            var seconds = rest.Length > 0 && int.TryParse(rest[0], out var s) ? s : 6;
            var device = rest.Length > 1 && int.TryParse(rest[1], out var d) ? d : -1;
            var language = rest.Length > 2 && rest[2].StartsWith("en", StringComparison.OrdinalIgnoreCase)
                ? RecognitionLanguage.English
                : RecognitionLanguage.Japanese;

            return MicTest.Run(seconds, device, language);
        }

        ApplicationConfiguration.Initialize();

        // Must happen before any control exists: WinForms picks the rendering
        // for the whole process here. Theme covers the few colours the app
        // chooses itself.
        Application.SetColorMode(Settings.Load().Theme switch
        {
            ThemeMode.Light => SystemColorMode.Classic,
            ThemeMode.Dark => SystemColorMode.Dark,
            _ => SystemColorMode.System,
        });

        Application.Run(new MainForm());
        return 0;
    }

    /// <summary>
    /// Lists the capture devices and actually opens each one, so "no audio is
    /// reaching the app" can be separated from "the recognizer did not match".
    /// </summary>
    private static int CheckDevices()
    {
        var reportPath = Path.Combine(TaskCatalog.DataDirectory, "devices-report.txt");
        var report = new StringBuilder();

        try
        {
            foreach (var device in AudioInput.Devices())
            {
                report.AppendLine($"[{device.Index,2}] {device}");

                try
                {
                    using var capture = new MicrophoneCapture(device.Index);
                    var peakDb = AudioLevel.FloorDb;
                    capture.Level += (_, level) =>
                    {
                        if (level.PeakDb > peakDb) peakDb = level.PeakDb;
                    };

                    var scratch = new byte[4096];
                    var total = 0;
                    var until = Environment.TickCount64 + 1500;

                    while (Environment.TickCount64 < until)
                    {
                        var read = capture.Stream.Read(scratch, 0, scratch.Length);
                        if (read == 0) break;
                        total += read;
                    }

                    report.AppendLine($"       opened OK, {total} bytes in 1.5s, "
                                      + $"peak {peakDb:0} dBFS ({AudioLevel.Verdict(peakDb)})");
                    if (total == 0) report.AppendLine("       WARNING: no audio captured");
                }
                catch (Exception ex)
                {
                    report.AppendLine($"       FAILED to open: {ex.Message}");
                }
            }

            report.AppendLine();
            report.AppendLine("--- recognizer plumbing (default device) ---");
            report.AppendLine(CheckRecognizerPlumbing());

            File.WriteAllText(reportPath, report.ToString());
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(reportPath, $"FAILED: {ex}");
            return 1;
        }
    }

    /// <summary>
    /// Confirms the recognizer accepts our capture stream and completes when the
    /// stream ends. Says nothing about accuracy - only that the path is wired.
    /// </summary>
    private static string CheckRecognizerPlumbing()
    {
        var recognizer = SpeechService.FindRecognizer(RecognitionLanguage.Japanese)
                         ?? SpeechService.FindRecognizer(RecognitionLanguage.English);

        if (recognizer is null) return "no recognizer installed";

        try
        {
            using var speech = new SpeechService(recognizer);
            speech.LoadVocabulary(new[] { "テスト" });

            var finished = new ManualResetEventSlim(false);
            speech.Finished += (_, _) => finished.Set();

            speech.Start();
            Thread.Sleep(1000);
            speech.Stop();

            return finished.Wait(TimeSpan.FromSeconds(5))
                ? "OK: capture stream accepted, recognition completed on stream end"
                : "FAILED: recognition never completed";
        }
        catch (Exception ex)
        {
            return $"FAILED: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
        }
    }
}
