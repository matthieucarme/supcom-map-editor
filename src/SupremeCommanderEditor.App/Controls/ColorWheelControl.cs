using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace SupremeCommanderEditor.App.Controls;

/// <summary>
/// HSV color wheel — circular hue/saturation picker rendered to a WriteableBitmap once on size
/// change. Pointer events are computed against the control's own bounds (Avalonia layout coords),
/// which avoids the DPI/scaling hit-test bugs of <c>Avalonia.Controls.ColorView</c>'s built-in
/// spectrum. Brightness (V) is exposed separately so the dialog can put a slider next to it.
/// </summary>
public class ColorWheelControl : Control
{
    public static readonly StyledProperty<Color> SelectedColorProperty =
        AvaloniaProperty.Register<ColorWheelControl, Color>(nameof(SelectedColor), Colors.White, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public Color SelectedColor
    {
        get => GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    private WriteableBitmap? _wheel;
    private int _wheelSize;

    public ColorWheelControl()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    static ColorWheelControl()
    {
        AffectsRender<ColorWheelControl>(SelectedColorProperty);
    }

    public override void Render(DrawingContext ctx)
    {
        EnsureWheel();
        if (_wheel == null) return;
        var rect = WheelRect();
        ctx.DrawImage(_wheel, rect);

        // Selector dot at the SelectedColor's H/S position. V from the color is ignored for
        // positioning (the wheel only encodes H+S).
        var (h, s, _) = RgbToHsv(SelectedColor);
        var cx = rect.Center.X;
        var cy = rect.Center.Y;
        var radius = rect.Width / 2 - 2;
        var angle = h * System.Math.PI / 180.0;
        var px = cx + System.Math.Cos(angle) * s * radius;
        var py = cy + System.Math.Sin(angle) * s * radius;
        var center = new Point(px, py);
        ctx.DrawEllipse(null, new Pen(Brushes.Black, 3), center, 7, 7);
        ctx.DrawEllipse(null, new Pen(Brushes.White, 1.5), center, 7, 7);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var side = System.Math.Min(
            double.IsInfinity(availableSize.Width) ? 256 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 256 : availableSize.Height);
        return new Size(side, side);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var s = System.Math.Min(finalSize.Width, finalSize.Height);
        return new Size(s, s);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        // Force rebuild next render.
        _wheel = null;
        InvalidateVisual();
    }

    private Rect WheelRect()
    {
        double s = System.Math.Min(Bounds.Width, Bounds.Height);
        double x = (Bounds.Width - s) / 2;
        double y = (Bounds.Height - s) / 2;
        return new Rect(x, y, s, s);
    }

    private void EnsureWheel()
    {
        int s = (int)System.Math.Min(Bounds.Width, Bounds.Height);
        if (s < 4) return;
        if (_wheel != null && _wheelSize == s) return;
        _wheel?.Dispose();
        _wheel = new WriteableBitmap(new PixelSize(s, s), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        _wheelSize = s;
        RenderWheelInto(s);
    }

    private unsafe void RenderWheelInto(int size)
    {
        if (_wheel == null) return;
        using var locked = _wheel.Lock();
        var ptr = (byte*)locked.Address;
        int stride = locked.RowBytes;
        float cx = size / 2f;
        float cy = size / 2f;
        float radius = size / 2f - 2;
        float r2 = radius * radius;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float d2 = dx * dx + dy * dy;
                int o = y * stride + x * 4;
                if (d2 > r2)
                {
                    // Fully transparent outside the disk.
                    ptr[o] = 0; ptr[o + 1] = 0; ptr[o + 2] = 0; ptr[o + 3] = 0;
                    continue;
                }
                float dist = System.MathF.Sqrt(d2);
                float angle = System.MathF.Atan2(dy, dx);
                float hue = (angle * 180f / System.MathF.PI + 360f) % 360f;
                float sat = dist / radius;
                var (rr, gg, bb) = HsvToRgb(hue, sat, 1f);
                // BGRA premul (alpha=1 means values are unchanged).
                ptr[o]     = bb;
                ptr[o + 1] = gg;
                ptr[o + 2] = rr;
                ptr[o + 3] = 255;
            }
        }
    }

    // === Pointer interaction ===

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Pointer.Capture(this);
        UpdateFromPointer(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            UpdateFromPointer(e.GetPosition(this));
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
    }

    private void UpdateFromPointer(Point p)
    {
        var rect = WheelRect();
        double cx = rect.Center.X;
        double cy = rect.Center.Y;
        double radius = rect.Width / 2 - 2;
        if (radius < 1) return;
        double dx = p.X - cx;
        double dy = p.Y - cy;
        double dist = System.Math.Sqrt(dx * dx + dy * dy);
        double angle = System.Math.Atan2(dy, dx) * 180.0 / System.Math.PI;
        if (angle < 0) angle += 360;
        double sat = System.Math.Min(1.0, dist / radius);
        // Preserve the current Value (brightness) from SelectedColor so the wheel only changes H+S.
        var (_, _, v) = RgbToHsv(SelectedColor);
        if (v <= 0.01) v = 1f; // if the user picked from pure black, snap to full brightness
        var (r, g, b) = HsvToRgb((float)angle, (float)sat, v);
        SelectedColor = Color.FromRgb(r, g, b);
        InvalidateVisual();
    }

    // === HSV math ===

    public static (byte r, byte g, byte b) HsvToRgb(float h, float s, float v)
    {
        h = ((h % 360f) + 360f) % 360f;
        s = System.Math.Clamp(s, 0f, 1f);
        v = System.Math.Clamp(v, 0f, 1f);
        float c = v * s;
        float x = c * (1 - System.MathF.Abs((h / 60f) % 2 - 1));
        float m = v - c;
        float r, g, b;
        if      (h < 60)  { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else              { r = c; g = 0; b = x; }
        return ((byte)System.Math.Round((r + m) * 255f),
                (byte)System.Math.Round((g + m) * 255f),
                (byte)System.Math.Round((b + m) * 255f));
    }

    public static (float h, float s, float v) RgbToHsv(Color c)
    {
        float r = c.R / 255f;
        float g = c.G / 255f;
        float b = c.B / 255f;
        float max = System.Math.Max(r, System.Math.Max(g, b));
        float min = System.Math.Min(r, System.Math.Min(g, b));
        float delta = max - min;
        float h = 0;
        if (delta > 0)
        {
            if (max == r)      h = ((g - b) / delta) % 6f;
            else if (max == g) h = (b - r) / delta + 2f;
            else               h = (r - g) / delta + 4f;
            h *= 60f;
            if (h < 0) h += 360f;
        }
        float s = max > 0 ? delta / max : 0;
        return (h, s, max);
    }
}
