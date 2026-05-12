using Avalonia.Media.Imaging;
using SkiaSharp;

namespace SupremeCommanderEditor.App.Services;

/// <summary>
/// Procedurally-rendered icons for the Lighting category of the 3D bottom palette.
/// Color swatches re-render on each call (the swatch IS the indicator of the current value);
/// the static icons (sun, arrow, glow, fog) are cached.
/// </summary>
public static class LightingIcons
{
    private const int Size = 96;
    private static readonly Dictionary<string, Bitmap> _staticCache = new();

    /// <summary>Square color swatch reflecting the given RGB (each 0..1). Clamped for display.</summary>
    public static Bitmap RenderColorSwatch(float r, float g, float b)
    {
        byte rb = (byte)Math.Clamp((int)MathF.Round(r * 255), 0, 255);
        byte gb = (byte)Math.Clamp((int)MathF.Round(g * 255), 0, 255);
        byte bb = (byte)Math.Clamp((int)MathF.Round(b * 255), 0, 255);

        using var sk = new SKBitmap(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(sk))
        {
            canvas.Clear(SKColors.Transparent);
            using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(rb, gb, bb) };
            using var border = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = new SKColor(30, 30, 36), StrokeWidth = 3 };
            var rect = new SKRect(8, 8, Size - 8, Size - 8);
            canvas.DrawRoundRect(rect, 8, 8, fill);
            canvas.DrawRoundRect(rect, 8, 8, border);
        }
        return ToAvalonia(sk);
    }

    public static Bitmap Sun => GetOrRender("sun", RenderSun);
    public static Bitmap Arrow3D => GetOrRender("arrow", RenderArrow);
    public static Bitmap Glow => GetOrRender("glow", RenderGlow);
    public static Bitmap Fog => GetOrRender("fog", RenderFog);

    private static Bitmap GetOrRender(string key, Action<SKCanvas, int, int> draw)
    {
        if (_staticCache.TryGetValue(key, out var cached)) return cached;
        using var sk = new SKBitmap(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(sk))
        {
            canvas.Clear(SKColors.Transparent);
            draw(canvas, Size, Size);
        }
        var bmp = ToAvalonia(sk);
        _staticCache[key] = bmp;
        return bmp;
    }

    private static void RenderSun(SKCanvas c, int w, int h)
    {
        float cx = w / 2f, cy = h / 2f;
        using var rays = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = new SKColor(255, 210, 80), StrokeWidth = 5, StrokeCap = SKStrokeCap.Round };
        for (int i = 0; i < 8; i++)
        {
            float a = i * MathF.PI / 4f;
            float x0 = cx + MathF.Cos(a) * 22;
            float y0 = cy + MathF.Sin(a) * 22;
            float x1 = cx + MathF.Cos(a) * 38;
            float y1 = cy + MathF.Sin(a) * 38;
            c.DrawLine(x0, y0, x1, y1, rays);
        }
        using var disc = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(255, 220, 100) };
        using var border = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = new SKColor(150, 100, 20), StrokeWidth = 3 };
        c.DrawCircle(cx, cy, 18, disc);
        c.DrawCircle(cx, cy, 18, border);
    }

    private static void RenderArrow(SKCanvas c, int w, int h)
    {
        // 3D-axis-looking arrow to suggest direction (just visual cue).
        float cx = w / 2f, cy = h / 2f;
        using var z = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = new SKColor(80, 160, 255), StrokeWidth = 6, StrokeCap = SKStrokeCap.Round };
        using var x = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = new SKColor(255, 100, 100), StrokeWidth = 6, StrokeCap = SKStrokeCap.Round };
        using var y = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = new SKColor(120, 220, 120), StrokeWidth = 6, StrokeCap = SKStrokeCap.Round };
        c.DrawLine(cx, cy, cx + 26, cy + 14, x);    // X
        c.DrawLine(cx, cy, cx, cy - 30, y);          // Y (up)
        c.DrawLine(cx, cy, cx - 24, cy + 18, z);    // Z
        using var node = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(220, 220, 220) };
        c.DrawCircle(cx, cy, 4, node);
    }

    private static void RenderGlow(SKCanvas c, int w, int h)
    {
        float cx = w / 2f, cy = h / 2f;
        using var halo = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(255, 250, 200, 80) };
        c.DrawCircle(cx, cy, 40, halo);
        using var mid = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(255, 240, 180, 160) };
        c.DrawCircle(cx, cy, 26, mid);
        using var core = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(255, 250, 210) };
        c.DrawCircle(cx, cy, 14, core);
    }

    private static void RenderFog(SKCanvas c, int w, int h)
    {
        // Horizontal soft bands suggesting layered fog.
        for (int i = 0; i < 4; i++)
        {
            float y = 24 + i * 14;
            byte alpha = (byte)(220 - i * 30);
            using var band = new SKPaint
            {
                IsAntialias = true, Style = SKPaintStyle.Stroke,
                Color = new SKColor(200, 210, 220, alpha),
                StrokeWidth = 6,
                StrokeCap = SKStrokeCap.Round,
            };
            c.DrawLine(14, y, w - 14, y, band);
        }
    }

    private static Bitmap ToAvalonia(SKBitmap sk)
    {
        using var data = sk.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream(data.ToArray());
        return new Bitmap(ms);
    }
}
