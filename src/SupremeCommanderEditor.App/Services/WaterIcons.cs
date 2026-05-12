using Avalonia.Media.Imaging;
using SkiaSharp;

namespace SupremeCommanderEditor.App.Services;

/// <summary>
/// Procedurally-rendered icons for the Water category. The Enable icon is state-aware (different
/// when water is on vs off) so the user can see at a glance whether water is active without
/// opening a popup. Depth-tier icons (Surface / Deep / Abyss) are static.
/// </summary>
public static class WaterIcons
{
    private const int Size = 96;
    private static readonly Dictionary<string, Bitmap> _cache = new();

    public static Bitmap GetEnable(bool isOn)
    {
        var key = isOn ? "enable-on" : "enable-off";
        if (_cache.TryGetValue(key, out var cached)) return cached;
        var bmp = Render(isOn ? DrawEnableOn : DrawEnableOff);
        _cache[key] = bmp;
        return bmp;
    }

    public static Bitmap Surface => GetOrRender("surface", DrawSurface);
    public static Bitmap Deep    => GetOrRender("deep",    DrawDeep);
    public static Bitmap Abyss   => GetOrRender("abyss",   DrawAbyss);

    private static Bitmap GetOrRender(string key, Action<SKCanvas> draw)
    {
        if (_cache.TryGetValue(key, out var cached)) return cached;
        var bmp = Render(draw);
        _cache[key] = bmp;
        return bmp;
    }

    private static Bitmap Render(Action<SKCanvas> draw)
    {
        using var sk = new SKBitmap(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(sk))
        {
            canvas.Clear(SKColors.Transparent);
            draw(canvas);
        }
        using var data = sk.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream(data.ToArray());
        return new Bitmap(ms);
    }

    // === Drawings ===

    private static void DrawEnableOn(SKCanvas c)
    {
        // Filled droplet — water "active".
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(80, 170, 240) };
        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = new SKColor(30, 80, 140), StrokeWidth = 3 };
        DrawDroplet(c, fill, stroke);
        // Small check mark.
        using var check = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = new SKColor(40, 220, 80), StrokeWidth = 5, StrokeCap = SKStrokeCap.Round };
        using var path = new SKPath();
        path.MoveTo(Size - 38, Size - 30);
        path.LineTo(Size - 26, Size - 18);
        path.LineTo(Size - 8, Size - 42);
        c.DrawPath(path, check);
    }

    private static void DrawEnableOff(SKCanvas c)
    {
        // Outline droplet, dim — water disabled.
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(40, 60, 80, 120) };
        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = new SKColor(120, 130, 140), StrokeWidth = 3 };
        DrawDroplet(c, fill, stroke);
        // Diagonal red strikethrough.
        using var slash = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = new SKColor(220, 70, 70), StrokeWidth = 6, StrokeCap = SKStrokeCap.Round };
        c.DrawLine(18, Size - 18, Size - 18, 18, slash);
    }

    private static void DrawDroplet(SKCanvas c, SKPaint fill, SKPaint stroke)
    {
        using var path = new SKPath();
        float cx = Size / 2f;
        float top = 14;
        float bottom = Size - 16;
        path.MoveTo(cx, top);
        path.CubicTo(cx + 30, top + 30, cx + 26, bottom, cx, bottom);
        path.CubicTo(cx - 26, bottom, cx - 30, top + 30, cx, top);
        path.Close();
        c.DrawPath(path, fill);
        c.DrawPath(path, stroke);
    }

    private static void DrawSurface(SKCanvas c)
    {
        // Bright waves near the top — surface water.
        DrawWaves(c, new SKColor(120, 200, 255), 28, 3);
        DrawDepthBar(c, new SKColor(100, 180, 240), highlightedTier: 0);
    }

    private static void DrawDeep(SKCanvas c)
    {
        DrawWaves(c, new SKColor(40, 110, 180), 28, 3);
        DrawDepthBar(c, new SKColor(40, 110, 180), highlightedTier: 1);
    }

    private static void DrawAbyss(SKCanvas c)
    {
        DrawWaves(c, new SKColor(20, 40, 80), 28, 3);
        DrawDepthBar(c, new SKColor(20, 40, 80), highlightedTier: 2);
    }

    private static void DrawWaves(SKCanvas c, SKColor color, float startY, int bands)
    {
        for (int i = 0; i < bands; i++)
        {
            float y = startY + i * 10;
            byte a = (byte)(220 - i * 50);
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                Color = new SKColor(color.Red, color.Green, color.Blue, a),
                StrokeWidth = 5,
                StrokeCap = SKStrokeCap.Round,
            };
            using var p = new SKPath();
            p.MoveTo(12, y);
            p.CubicTo(28, y - 6, 44, y + 6, 60, y);
            p.CubicTo(72, y - 6, 84, y + 6, Size - 12, y);
            c.DrawPath(p, paint);
        }
    }

    /// <summary>Three-tier depth indicator at the bottom: highlights which tier this icon represents.</summary>
    private static void DrawDepthBar(SKCanvas c, SKColor accent, int highlightedTier)
    {
        float y0 = Size - 18;
        float w = (Size - 28) / 3f;
        for (int i = 0; i < 3; i++)
        {
            var rect = new SKRect(14 + i * w, y0, 14 + (i + 1) * w - 2, y0 + 8);
            using var p = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = i == highlightedTier ? accent : new SKColor(60, 60, 70, 160),
            };
            c.DrawRoundRect(rect, 2, 2, p);
        }
    }
}
