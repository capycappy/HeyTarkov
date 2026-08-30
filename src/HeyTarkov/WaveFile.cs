namespace HeyTarkov;

/// <summary>
/// Minimal RIFF writer for the one format this app captures in. Used to retry a
/// failed live recognition from a file, which is the path System.Speech handles
/// most predictably.
/// </summary>
public static class WaveFile
{
    public static void Write(string path, byte[] pcm, int sampleRate)
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var blockAlign = (short)(channels * bitsPerSample / 8);

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8);
        writer.Write(36 + pcm.Length);
        writer.Write("WAVE"u8);

        writer.Write("fmt "u8);
        writer.Write(16);                 // PCM header size
        writer.Write((short)1);           // PCM
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);

        writer.Write("data"u8);
        writer.Write(pcm.Length);
        writer.Write(pcm);
    }

    /// <summary>Loudest sample as a fraction of full scale, for 16-bit PCM.</summary>
    public static double PeakDb(byte[] pcm)
    {
        var peak = 0;

        for (var i = 0; i + 1 < pcm.Length; i += 2)
        {
            var magnitude = Math.Abs(BitConverter.ToInt16(pcm, i));
            if (magnitude > peak) peak = magnitude;
        }

        return AudioLevel.ToDecibels((float)peak / short.MaxValue);
    }
}
