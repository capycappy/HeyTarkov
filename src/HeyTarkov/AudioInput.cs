using NAudio.Wave;

namespace HeyTarkov;

/// <summary>One selectable capture device. Index -1 is the Windows default.</summary>
public sealed record AudioDevice(int Index, string Name)
{
    public bool IsDefault => Index < 0;

    public override string ToString() => IsDefault ? Strings.DefaultDevice(Name) : Name;
}

public static class AudioInput
{
    /// <summary>
    /// Every WinMM capture device, with the system default first. On an
    /// interface like an RME the individual input pairs show up as separate
    /// devices, which is exactly why this needs to be selectable.
    /// </summary>
    public static List<AudioDevice> Devices()
    {
        var devices = new List<AudioDevice> { new(-1, DefaultDeviceName()) };

        for (var i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            try
            {
                devices.Add(new AudioDevice(i, WaveInEvent.GetCapabilities(i).ProductName));
            }
            catch (Exception)
            {
                // A device that disappeared between the count and the query.
            }
        }

        return devices;
    }

    private static string DefaultDeviceName()
    {
        try
        {
            return WaveInEvent.DeviceCount > 0
                ? WaveInEvent.GetCapabilities(0).ProductName
                : Strings.NoInputDevice;
        }
        catch (Exception)
        {
            return Strings.UnknownDevice;
        }
    }
}
