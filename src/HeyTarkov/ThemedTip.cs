namespace HeyTarkov;

/// <summary>
/// A tooltip in the window's own colours. The stock one is a white box with
/// black system text, which sat on the dark window like a sticker from another
/// program.
/// </summary>
public sealed class ThemedTip : ToolTip
{
    private readonly Font _font = new("Yu Gothic UI", 10f);
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
            var size = TextRenderer.MeasureText(text, _font, new Size(int.MaxValue, int.MaxValue), Flags);
            e.ToolTipSize = new Size(size.Width + PadX * 2, size.Height + PadY * 2);
        };

        Draw += (_, e) =>
        {
            var g = e.Graphics;
            var box = e.Bounds;

            using (var fill = new SolidBrush(Theme.PanelHi)) g.FillRectangle(fill, box);
            using (var edge = new Pen(Theme.Field)) g.DrawRectangle(edge, box.X, box.Y, box.Width - 1, box.Height - 1);

            TextRenderer.DrawText(g, e.ToolTipText, _font,
                Rectangle.FromLTRB(box.Left + PadX, box.Top + PadY, box.Right - PadX, box.Bottom - PadY),
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
            _font.Dispose();
        }

        base.Dispose(disposing);
    }
}
