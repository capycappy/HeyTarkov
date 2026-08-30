using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// Generates the Hey Tarkov application icon.
//
//     dotnet run --project tools/IconMaker
//
// Writes src/HeyTarkov/app.ico plus PNG masters and a contact sheet under
// docs/icon/. Re-run it after changing a colour or a proportion; the icon is
// code so it never has to be redrawn by hand.
//
// The design has to read as "find one task out of many, by voice" rather than
// "a window with a microphone": a list panel with one row picked out, and a
// microphone badge over the corner.
//
// The same composition is drawn at every size: dropping the list at small
// sizes made the taskbar icon look like a different app.

internal static class Program
{
    /// <summary>
    /// Panel, unselected rows, the highlight/badge, the mic glyph, and the
    /// outline.
    ///
    /// Rim is separate from Ink on purpose. An outline darker than the panel
    /// only works on a light background: against a dark taskbar the whole shape
    /// dissolves. A mid-tone rim reads against both.
    /// </summary>
    private sealed record Palette(
        string Name, Color Panel, Color Dim, Color Accent, Color Ink, Color Rim);

    private static readonly Palette[] Palettes =
    {
        // Closest to the in-game UI: warm gunmetal with the tan the interface
        // uses for text, and its muted gold for anything important.
        new("A-Gunmetal", Color.FromArgb(0x35, 0x31, 0x2A), Color.FromArgb(0x8A, 0x81, 0x6E),
            Color.FromArgb(0xD2, 0xA3, 0x51), Color.FromArgb(0x14, 0x12, 0x0F),
            Color.FromArgb(0x6E, 0x64, 0x53)),

        // Military olive with a scavenged-orange accent. Reads as field kit.
        new("B-Olive", Color.FromArgb(0x45, 0x4B, 0x37), Color.FromArgb(0x98, 0x99, 0x7E),
            Color.FromArgb(0xD9, 0x7B, 0x2E), Color.FromArgb(0x14, 0x16, 0x10),
            Color.FromArgb(0x7A, 0x7F, 0x5E)),

        // Cold steel and bone: no strong hue, the highest contrast of the three.
        new("C-Steel", Color.FromArgb(0x33, 0x3A, 0x3E), Color.FromArgb(0x80, 0x88, 0x90),
            Color.FromArgb(0xDC, 0xD3, 0xBC), Color.FromArgb(0x0F, 0x11, 0x12),
            Color.FromArgb(0x6E, 0x77, 0x7D)),
    };

    /// <summary>The shipped palette. Change the index to switch, or add one.</summary>
    private static Palette P = Palettes[0];

    private static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

    private enum Variant
    {
        /// <summary>A deck of cards, front one selected, mic on it.</summary>
        Deck,

        /// <summary>P.Panel of list rows, one highlighted, mic as a corner badge.</summary>
        ListBadge,
    }

    private static void Main(string[] args)
    {
        var root = args.Length > 0 ? args[0] : FindRepositoryRoot();
        var iconPath = Path.Combine(root, "src", "HeyTarkov", "app.ico");
        var docs = Path.Combine(root, "docs", "icon");

        Directory.CreateDirectory(Path.GetDirectoryName(iconPath)!);
        Directory.CreateDirectory(docs);

        var frames = new List<(int Size, byte[] Png)>();

        foreach (var size in Sizes)
        {
            using var bitmap = Render(size, Variant.ListBadge);
            using var memory = new MemoryStream();
            bitmap.Save(memory, ImageFormat.Png);
            frames.Add((size, memory.ToArray()));

            if (size is 16 or 24 or 32 or 48 or 256)
                File.WriteAllBytes(Path.Combine(docs, $"icon-{size}.png"), memory.ToArray());
        }

        WriteIco(iconPath, frames);

        var shipped = P.Name;
        WriteComparison(Path.Combine(docs, "palettes.png"));   // this walks every palette
        P = Palettes[0];

        Console.WriteLine($"wrote {iconPath} ({Sizes.Length} sizes, palette {shipped})");
        Console.WriteLine($"wrote PNG masters and palettes.png to {docs}");
    }

    /// <summary>Walk up until the solution layout appears, so the tool can be
    /// run from anywhere.</summary>
    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "HeyTarkov"))) return dir.FullName;
            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static Bitmap Render(int size, Variant variant)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);

        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        float U(float v) => v * size;
        var rim = Math.Max(1f, U(0.016f));

        switch (variant)
        {
            case Variant.Deck: DrawDeck(g, U, rim); break;
            case Variant.ListBadge: DrawListBadge(g, U, rim, size); break;
        }

        return bitmap;
    }

    // ---------------------------------------------------------------- large

    /// <summary>Three offset cards - "many" - with the front one picked out.</summary>
    private static void DrawDeck(Graphics g, Func<float, float> U, float rim)
    {
        float[] offsets = { 0.00f, 0.09f, 0.18f };
        var side = 0.62f;

        for (var i = 0; i < 3; i++)
        {
            var o = offsets[i];
            var last = i == 2;
            using var path = Rounded(U(0.04f + o), U(0.04f + o), U(side), U(side), U(0.15f));
            using var brush = new SolidBrush(last ? P.Accent : P.Panel);
            g.FillPath(brush, path);
            using var pen = new Pen(P.Rim, rim);
            g.DrawPath(pen, path);
        }

        var c = 0.04f + offsets[2] + side / 2;
        DrawMic(g, U(c), U(c), U(side), rim, P.Ink, filled: false);
    }

    /// <summary>A list of rows with one highlighted, and a mic badge over the corner.</summary>
    private static void DrawListBadge(Graphics g, Func<float, float> U, float rim, int size)
    {
        // Same composition at every size; only the weights change. Below 32px
        // the mic needs the heavier strokes to survive, and the badge grows a
        // little to give it room.
        var tiny = size < 32;

        using (var path = Rounded(U(0.04f), U(0.04f), U(0.74f), U(0.74f), U(0.15f)))
        {
            using var brush = new SolidBrush(P.Panel);
            g.FillPath(brush, path);
            using var pen = new Pen(P.Rim, rim);
            g.DrawPath(pen, path);
        }

        // Rows: the second one is the hit.
        float[] tops = { 0.16f, 0.355f, 0.55f };
        float[] widths = { 0.52f, 0.60f, 0.40f };

        for (var i = 0; i < 3; i++)
        {
            var selected = i == 1;
            using var row = Rounded(U(0.13f), U(tops[i]), U(widths[i]), U(0.125f), U(0.0625f));
            using var brush = new SolidBrush(selected ? P.Accent : P.Dim);
            g.FillPath(brush, row);
        }

        // Mic badge, overlapping the lower-right corner.
        var bx = tiny ? 0.69f : 0.70f;
        var br = tiny ? 0.31f : 0.27f;

        using (var badge = new GraphicsPath())
        {
            badge.AddEllipse(U(bx - br), U(bx - br), U(br * 2), U(br * 2));
            using var brush = new SolidBrush(P.Accent);
            g.FillPath(brush, badge);
            using var pen = new Pen(P.Rim, rim);
            g.DrawPath(pen, badge);
        }

        DrawMic(g, U(bx), U(bx), U(br * (tiny ? 1.70f : 1.85f)), rim, P.Ink, filled: false, bold: tiny);
    }

    // ------------------------------------------------------------------ mic

    private static void DrawMic(Graphics g, float cx, float cy, float span, float rim,
        Color color, bool filled, bool bold = false)
    {
        var capsuleW = span * (bold ? 0.30f : 0.26f);
        var capsuleH = span * (bold ? 0.40f : 0.36f);
        var stroke = span * (bold ? 0.12f : 0.085f);
        var top = cy - span * (bold ? 0.30f : 0.27f);

        using var brush = new SolidBrush(color);
        using var pen = new Pen(color, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        using (var capsule = Rounded(cx - capsuleW / 2, top, capsuleW, capsuleH, capsuleW / 2))
        {
            g.FillPath(brush, capsule);
            if (filled)
            {
                using var outline = new Pen(P.Ink, rim);
                g.DrawPath(outline, capsule);
            }
        }

        var cradleR = span * (bold ? 0.26f : 0.24f);
        var cradleCy = top + capsuleH * 0.72f;
        g.DrawArc(pen, cx - cradleR, cradleCy - cradleR, cradleR * 2, cradleR * 2, 20, 140);

        var stemTop = cradleCy + cradleR;
        var stemBottom = stemTop + span * 0.12f;
        g.DrawLine(pen, cx, stemTop, cx, stemBottom);

        var footW = span * (bold ? 0.26f : 0.24f);
        g.DrawLine(pen, cx - footW / 2, stemBottom, cx + footW / 2, stemBottom);
    }

    // ------------------------------------------------------------- plumbing

    private static GraphicsPath Rounded(float x, float y, float w, float h, float r)
    {
        r = Math.Min(r, Math.Min(w, h) / 2);
        var path = new GraphicsPath();
        var d = r * 2;

        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }

    private static void WriteIco(string path, List<(int Size, byte[] Png)> frames)
    {
        using var stream = File.Create(path);
        using var w = new BinaryWriter(stream);

        w.Write((short)0);
        w.Write((short)1);
        w.Write((short)frames.Count);

        var offset = 6 + 16 * frames.Count;

        foreach (var (size, png) in frames)
        {
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((short)1);
            w.Write((short)32);
            w.Write(png.Length);
            w.Write(offset);
            offset += png.Length;
        }

        foreach (var (_, png) in frames) w.Write(png);
    }

    /// <summary>Each palette at every size, over light and dark.</summary>
    private static void WriteComparison(string path)
    {
        int[] shown = { 16, 24, 32, 48, 64, 128, 256 };
        int[] zoom = { 16, 24, 32 };
        const int pad = 22;
        const int labelW = 130;

        var rowH = 256 + pad * 2 + 18;
        var width = labelW + shown.Sum(s => s + pad) + zoom.Sum(s => s * 8 + pad) + pad;
        var height = rowH * Palettes.Length * 2;

        using var sheet = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var font = new Font("Segoe UI", 11f, FontStyle.Bold);
        using var small = new Font("Segoe UI", 8f);

        var y = 0;

        foreach (var dark in new[] { false, true })
        {
            foreach (var palette in Palettes)
            {
                P = palette;

                var band = new Rectangle(0, y, width, rowH);
                using (var b = new SolidBrush(dark
                           ? Color.FromArgb(0x1E, 0x1E, 0x1E)
                           : Color.FromArgb(0xF4, 0xF4, 0xF4)))
                    g.FillRectangle(b, band);

                var text = dark ? Color.FromArgb(0xE6, 0xE6, 0xE6) : Color.FromArgb(0x22, 0x22, 0x22);
                using (var b = new SolidBrush(text))
                    g.DrawString(palette.Name, font, b, 12, y + pad);

                var x = labelW;

                foreach (var size in shown)
                {
                    using var icon = Render(size, Variant.ListBadge);
                    g.DrawImageUnscaled(icon, x, y + pad + (256 - size));

                    using var b = new SolidBrush(text);
                    g.DrawString($"{size}", small, b, x, y + pad + 258);

                    x += size + pad;
                }

                // The small sizes again at 8x, nearest-neighbour, so the actual
                // pixels can be judged rather than guessed at.
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;

                foreach (var size in zoom)
                {
                    using var icon = Render(size, Variant.ListBadge);
                    var box = size * 8;
                    g.DrawImage(icon, x, y + pad + (256 - box), box, box);
                    using var b2 = new SolidBrush(text);
                    g.DrawString($"{size} x8", small, b2, x, y + pad + 258);
                    x += box + pad;
                }

                g.InterpolationMode = InterpolationMode.Default;
                g.PixelOffsetMode = PixelOffsetMode.Default;

                y += rowH;
            }
        }

        sheet.Save(path, ImageFormat.Png);
    }
}
