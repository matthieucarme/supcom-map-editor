using Avalonia.Media.Imaging;
using SkiaSharp;

namespace SupremeCommanderEditor.App.Services;

/// <summary>
/// Fallback icons for the "Map" palette category — used when a strata slot has no resolvable
/// library thumbnail (the texture is empty, or its path doesn't match any scanned library entry).
/// </summary>
public static class MapStrataPlaceholder
{
    private const int Size = 96;
    private static readonly Dictionary<(int strata, bool hasPath), Bitmap> _cache = new();

    public static Bitmap GetIcon(int strata, bool hasPath)
    {
        var key = (strata, hasPath);
        if (_cache.TryGetValue(key, out var cached)) return cached;
        var bmp = Render(strata, hasPath);
        _cache[key] = bmp;
        return bmp;
    }

    private static readonly Dictionary<int, Bitmap> _unavailable = new();
    public static Bitmap GetUnavailableIcon(int strata)
    {
        if (_unavailable.TryGetValue(strata, out var cached)) return cached;
        var bmp = RenderUnavailable(strata);
        _unavailable[strata] = bmp;
        return bmp;
    }

    private static Bitmap RenderUnavailable(int strata)
    {
        using var sk = new SKBitmap(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(sk))
        {
            canvas.Clear(SKColors.Transparent);
            // Hatched-looking strikethrough box so the slot reads as "unavailable" at a glance.
            using var bg = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(28, 28, 32) };
            using var border = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = new SKColor(80, 80, 90), StrokeWidth = 3, PathEffect = SKPathEffect.CreateDash([6, 4], 0) };
            var rect = new SKRect(8, 8, Size - 8, Size - 8);
            canvas.DrawRoundRect(rect, 8, 8, bg);
            canvas.DrawRoundRect(rect, 8, 8, border);

            using var slash = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = new SKColor(170, 70, 70, 200), StrokeWidth = 5, StrokeCap = SKStrokeCap.Round };
            canvas.DrawLine(20, Size - 20, Size - 20, 20, slash);

            using var digit = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(120, 120, 130) };
            using var font = new SKFont { Size = 36, Embolden = true };
            string label = strata.ToString();
            var w = font.MeasureText(label);
            canvas.DrawText(label, Size / 2f - w / 2f, Size / 2f + 12, font, digit);

            using var tag = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(170, 110, 110) };
            using var smallFont = new SKFont { Size = 11, Embolden = true };
            const string txt = "v53";
            var tw = smallFont.MeasureText(txt);
            canvas.DrawText(txt, Size / 2f - tw / 2f, Size - 14, smallFont, tag);
        }
        using var data = sk.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream(data.ToArray());
        return new Bitmap(ms);
    }

    private static Bitmap Render(int strata, bool hasPath)
    {
        using var sk = new SKBitmap(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(sk))
        {
            canvas.Clear(SKColors.Transparent);
            // Empty strata: dim grey hatched look. Assigned but no library match: mid-grey with "?"
            // overlay so the user can see "this strata has a texture but the editor can't preview it".
            var fillColor = hasPath ? new SKColor(80, 80, 90) : new SKColor(40, 40, 48);
            using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = fillColor };
            using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = new SKColor(120, 120, 130), StrokeWidth = 3 };
            var rect = new SKRect(8, 8, Size - 8, Size - 8);
            canvas.DrawRoundRect(rect, 8, 8, fill);
            canvas.DrawRoundRect(rect, 8, 8, stroke);

            using var digit = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(220, 220, 230) };
            using var font = new SKFont { Size = 44, Embolden = true };
            string text = hasPath ? "?" : strata.ToString();
            var w = font.MeasureText(text);
            canvas.DrawText(text, Size / 2f - w / 2f, Size / 2f + 16, font, digit);

            using var label = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(180, 180, 190) };
            using var smallFont = new SKFont { Size = 12 };
            string tag = hasPath ? $"strata {strata}" : "empty";
            var tw = smallFont.MeasureText(tag);
            canvas.DrawText(tag, Size / 2f - tw / 2f, Size - 14, smallFont, label);
        }
        using var data = sk.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream(data.ToArray());
        return new Bitmap(ms);
    }
}
