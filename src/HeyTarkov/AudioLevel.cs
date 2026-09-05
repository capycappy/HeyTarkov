namespace HeyTarkov;

/// <summary>
/// One buffer's loudness, as a fraction of full scale (1.0 = clipping).
///
/// Reported in dBFS because a linear percentage is misleading for speech: a
/// perfectly healthy voice peaks around 0.1 of full scale, which reads as "10%"
/// and looks broken even though it is a good signal.
/// </summary>
public readonly record struct AudioLevel(float Peak, float Rms)
{
    /// <summary>Quieter than this and there is effectively nothing there.</summary>
    public const double SilenceDb = -50;

    /// <summary>Bottom of the meter.</summary>
    public const double FloorDb = -60;

    public double PeakDb => ToDecibels(Peak);
    public double RmsDb => ToDecibels(Rms);

    public static double ToDecibels(float amplitude) =>
        20 * Math.Log10(Math.Max(amplitude, 0.000001f));

    /// <summary>0-100 for a progress bar, scaled over the useful dB range.</summary>
    public static int ToMeter(double decibels) =>
        (int)Math.Clamp((decibels - FloorDb) / -FloorDb * 100, 0, 100);

    /// <summary>Plain-language verdict on a peak-hold reading.</summary>
    public static string Verdict(double peakDb) => peakDb switch
    {
        < SilenceDb => Strings.VerdictSilent,
        < -35 => Strings.VerdictTooQuiet,
        < -3 => Strings.VerdictGood,
        _ => Strings.VerdictTooLoud,
    };
}
