using NAudio.Wave;

namespace HeyTarkov;

/// <summary>
/// Captures from a chosen input device into a <see cref="LiveAudioStream"/> that
/// System.Speech can read. Doing the capture ourselves buys two things the
/// built-in default-device input cannot give us:
///
///  * the user picks which device to listen to
///  * a level meter computed from the actual samples, so "is the mic even
///    working?" has an answer that does not depend on the recognizer
/// </summary>
public sealed class MicrophoneCapture : IDisposable
{
    /// <summary>What SAPI wants: 16 kHz, 16-bit, mono. WinMM converts for us.</summary>
    public const int SampleRate = 16000;

    private const int BytesPerSecond = SampleRate * 2;
    private const int TrailingSilenceMs = 400;

    /// <summary>A held button is not expected to outlast this; a stuck one is.</summary>
    private const int MaxCaptureSeconds = 60;

    private readonly WaveInEvent _capture;
    private bool _disposed;

    public MicrophoneCapture(int deviceIndex)
    {
        Stream = new LiveAudioStream(BytesPerSecond * 4);

        _capture = new WaveInEvent
        {
            // -1 is WAVE_MAPPER, i.e. whatever Windows considers the default.
            DeviceNumber = deviceIndex,
            WaveFormat = new WaveFormat(SampleRate, 16, 1),
            BufferMilliseconds = 50,
        };

        _capture.DataAvailable += OnDataAvailable;
        _capture.StartRecording();
    }

    /// <summary>The audio, as the recognizer reads it.</summary>
    public LiveAudioStream Stream { get; }

    /// <summary>Peak and RMS of the last buffer, as a fraction of full scale.</summary>
    public event EventHandler<AudioLevel>? Level;

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (Stream.IsFinished) return;

        Stream.Append(e.Buffer, 0, e.BytesRecorded);

        var peak = 0;
        var sumOfSquares = 0.0;
        var samples = 0;

        for (var i = 0; i + 1 < e.BytesRecorded; i += 2)
        {
            var sample = BitConverter.ToInt16(e.Buffer, i);
            var magnitude = Math.Abs((int)sample);
            if (magnitude > peak) peak = magnitude;

            sumOfSquares += (double)sample * sample;
            samples++;
        }

        if (samples > 0)
        {
            var rms = Math.Sqrt(sumOfSquares / samples) / short.MaxValue;
            Level?.Invoke(this, new AudioLevel((float)peak / short.MaxValue, (float)rms));
        }

        if (Stream.Captured > BytesPerSecond * MaxCaptureSeconds) Finish();
    }

    /// <summary>
    /// Stop capturing. A little trailing silence gives the recognizer the
    /// end-of-speech it expects before the audio runs out.
    /// </summary>
    public void Finish()
    {
        Stream.Finish(BytesPerSecond / 1000 * TrailingSilenceMs);

        try { _capture.StopRecording(); }
        catch (Exception) { /* already stopping */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Finish();

        try { _capture.Dispose(); }
        catch (Exception) { /* nothing useful to do while tearing down */ }

        Stream.Dispose();
    }
}
