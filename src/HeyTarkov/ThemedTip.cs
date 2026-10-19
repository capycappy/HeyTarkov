namespace HeyTarkov;

/// <summary>
/// A tooltip in the window's own colours. The stock one is a white box with
/// black system text, which sat on the dark window like a sticker from another
/// program.
/// </summary>
public sealed class ThemedTip : ToolTip
{
    /// <summary>
    /// The size the reading column had beside the names. Katakana looks larger
    /// than Latin letters at the same size, and at 10pt the reading out-sized
    /// the name it was the reading of.
    /// </summary>
    private const float Points = 8.75f;

    /// <summary>
    /// The font for the monitor the control is on, sized in pixels. The
    /// tooltip window does not follow the monitor's scaling itself, so a size
    /// in points came out the same on a 100% and a 150% screen - right on one,
    /// tiny on the other.
    /// </summary>
    private Font? _font;
    private int _fontDpi;

    private Font FontFor(Control? control)
    {
        var dpi = control?.DeviceDpi ?? 96;
        if (_font is not null && _fontDpi == dpi) return _font;

        _font?.Dispose();
        _font = new Font("Yu Gothic UI", Points * dpi / 72f, GraphicsUnit.Pixel);
        _fontDpi = dpi;
        return _font;
    }

    private static int Scaled(Control? control, int pixels) => control?.LogicalToDeviceUnits(pixels) ?? pixels;
    private readonly System.Windows.Forms.Timer _wait = new() { Interval = 650 };

    private Control? _over;
    private string _pending = "";
    private string _showing = "";

    private const int PadX = 10;
    private const int PadY = 6;

    public ThemedTip()
    {
        OwnerDraw = true;
        InitialDelay = 650;
        ReshowDelay = 250;
        AutoPopDelay = 12000;

        Popup += (_, e) =>
        {
            var text = GetToolTip(e.AssociatedControl) is { Length: > 0 } fixedText ? fixedText : _showing;
            var control = e.AssociatedControl;
            var size = TextRenderer.MeasureText(text, FontFor(control), new Size(int.MaxValue, int.MaxValue), Flags);
            e.ToolTipSize = new Size(size.Width + Scaled(control, PadX) * 2, size.Height + Scaled(control, PadY) * 2);
        };

        Draw += (_, e) =>
        {
            var g = e.Graphics;
            var box = e.Bounds;
            var control = e.AssociatedControl;
            var padX = Scaled(control, PadX);
            var padY = Scaled(control, PadY);

            using (var fill = new SolidBrush(Theme.PanelHi)) g.FillRectangle(fill, box);
            using (var edge = new Pen(Theme.Field)) g.DrawRectangle(edge, box.X, box.Y, box.Width - 1, box.Height - 1);

            TextRenderer.DrawText(g, e.ToolTipText, FontFor(control),
                Rectangle.FromLTRB(box.Left + padX, box.Top + padY, box.Right - padX, box.Bottom - padY),
                Theme.Text, Flags);
        };

        _wait.Tick += (_, _) =>
        {
            _wait.Stop();
            if (_over is not { IsHandleCreated: true } control || _pending.Length == 0) return;

            // Below and to the right of the pointer, clear of the arrow, rather
            // than wherever the stock tooltip puts it - which was on top of it.
            var at = control.PointToClient(Cursor.Position);
            _showing = _pending;
            Show(_pending, control,
                at.X + control.LogicalToDeviceUnits(14), at.Y + control.LogicalToDeviceUnits(22), AutoPopDelay);
        };
    }

    private const TextFormatFlags Flags =
        TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    /// <summary>
    /// Show this near the pointer once it has rested a moment, for a control
    /// whose parts each say something different - rows of a list, one title in
    /// a header. The same text again does nothing; an empty one hides it.
    /// </summary>
    public void Rest(Control control, string text)
    {
        if (text == _pending && control == _over) return;

        _wait.Stop();
        if (_showing.Length > 0 && _over is not null) Hide(_over);

        _over = control;
        _pending = text;
        _showing = "";

        if (text.Length > 0) _wait.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _wait.Dispose();
            _font?.Dispose();
        }

        base.Dispose(disposing);
    }
}
