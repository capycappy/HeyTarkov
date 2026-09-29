using System.Drawing.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace HeyTarkov;

/// <summary>
/// Reading the words out of a screenshot, using the recognizer that ships with
/// Windows.
///
/// The game lists a player's tasks on one screen, and copying nine names into a
/// search box one at a time is the sort of work a computer should be doing. A
/// picture of that screen - taken with Windows' own Win+Shift+S, pasted here -
/// is all this needs.
///
/// Nothing is installed and nothing is sent anywhere: the recognizer is part of
/// Windows and runs locally, which is the same promise the rest of the app
/// makes. The app still never looks at the game itself; it looks at a picture
/// the user chose to take.
/// </summary>
public static class ScreenText
{
    /// <summary>
    /// Below this the recognizer starts missing thin letters, so a small
    /// picture is enlarged first. Costs milliseconds and buys whole words.
    /// </summary>
    private const int ComfortableWidth = 1600;

    /// <summary>Whether Windows has a recognizer at all.</summary>
    public static bool Available => Engine() is not null;

    /// <summary>Which language it reads in, for saying so on screen.</summary>
    public static string? Language => Engine()?.RecognizerLanguage.DisplayName;

    /// <summary>
    /// Every line of text in the picture, top to bottom.
    ///
    /// Latin names are read by whichever recognizer is installed - the Japanese
    /// one reads the alphabet too, which matters here because the game shows
    /// task names in English even when it is played in Japanese.
    /// </summary>
    public static async Task<IReadOnlyList<string>> LinesAsync(Image image)
    {
        if (Engine() is not { } engine) return Array.Empty<string>();

        using var bitmap = await SoftwareAsync(image).ConfigureAwait(false);
        var result = await engine.RecognizeAsync(bitmap);

        return result.Lines.Select(line => line.Text.Trim())
            .Where(text => text.Length > 0)
            .ToList();
    }

    /// <summary>
    /// The recognizer to use: English if Windows has it, since these names are
    /// English; otherwise whatever the user's Windows was set up with.
    /// </summary>
    private static OcrEngine? Engine() =>
        OcrEngine.TryCreateFromLanguage(new Language("en-US"))
        ?? OcrEngine.TryCreateFromUserProfileLanguages()
        ?? (OcrEngine.AvailableRecognizerLanguages.Count > 0
            ? OcrEngine.TryCreateFromLanguage(OcrEngine.AvailableRecognizerLanguages[0])
            : null);

    /// <summary>
    /// A WinForms image, as the Windows recognizer wants it. PNG in between
    /// rather than pixel copying: the two worlds disagree about pixel formats
    /// and stride, and an encoder settles it in a line.
    /// </summary>
    private static async Task<SoftwareBitmap> SoftwareAsync(Image image)
    {
        using var enlarged = Enlarge(image);
        using var memory = new MemoryStream();

        enlarged.Save(memory, ImageFormat.Png);
        memory.Position = 0;

        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(memory.ToArray().AsBuffer());
        stream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(stream);
        return await decoder.GetSoftwareBitmapAsync();
    }

    private static Bitmap Enlarge(Image image)
    {
        var scale = image.Width >= ComfortableWidth
            ? 1
            : Math.Min(3, (int)Math.Ceiling(ComfortableWidth / (double)image.Width));

        var copy = new Bitmap(image.Width * scale, image.Height * scale, PixelFormat.Format32bppArgb);

        using var g = Graphics.FromImage(copy);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.DrawImage(image, 0, 0, copy.Width, copy.Height);

        return copy;
    }
}
