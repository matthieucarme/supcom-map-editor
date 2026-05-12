using Avalonia.Media.Imaging;
using SkiaSharp;

namespace SupremeCommanderEditor.App.Services;

/// <summary>Five-mode terrain brush — order matches <see cref="HeightmapBrushTool.BrushMode"/>.
/// "Smart texturing" is a separate global toggle (see <c>IsSmartTexturingEnabled</c>) that decorates
/// any of these modes with automatic texture painting.</summary>
public enum TerrainBrushMode
{
    Raise = 0,
    Lower = 1,
    Smooth = 2,
    Flatten = 3,
    Plateau = 4,
}

/// <summary>One entry in the bottom 3D-view palette: a brush mode + its icon + label.</summary>
public sealed record TerrainBrushEntry(TerrainBrushMode Mode, string Label, Bitmap Icon);

/// <summary>
/// Procedurally-rendered icons for the 5 heightmap brush modes. Same pattern as
/// <see cref="MarkerCatalog"/>: Skia draws a stylised shape, encoded to PNG, decoded to Avalonia
/// Bitmap. Lives in App because Skia is only referenced here.
/// </summary>
public static class TerrainBrushCatalog
{
    private const int IconSize = 96;

    private static readonly (TerrainBrushMode mode, string label)[] Ordered =
    [
        (TerrainBrushMode.Raise,   "Raise"),
        (TerrainBrushMode.Lower,   "Lower"),
        (TerrainBrushMode.Smooth,  "Smooth"),
        (TerrainBrushMode.Flatten, "Flatten"),
        (TerrainBrushMode.Plateau, "Plateau"),
    ];

    public static IReadOnlyList<TerrainBrushEntry> All { get; } = Build();

    private static IReadOnlyList<TerrainBrushEntry> Build()
    {
        var list = new List<TerrainBrushEntry>();
        foreach (var (mode, label) in Ordered)
            list.Add(new TerrainBrushEntry(mode, label, RenderIcon(mode)));
        return list;
    }

    private static Bitmap RenderIcon(TerrainBrushMode mode)
    {
        using var sk = new SKBitmap(IconSize, IconSize, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(sk))
        {
            canvas.Clear(SKColors.Transparent);
            DrawShape(canvas, mode);
        }
        using var data = sk.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream(data.ToArray());
        return new Bitmap(ms);
    }

    /// <summary>
    /// Each icon shows a small piece of terrain (silhouette filled at the bottom) plus an action
    /// glyph layered on top so the user can read intent at a glance:
    ///   Raise   : up arrow over a small mound
    ///   Lower   : down arrow into a small dip
    ///   Smooth  : sine wave fading to flat
    ///   Flatten : horizontal bar with arrows pressing down
    ///   Plateau : stepped trapezoid (flat-topped hill)
    /// Colors are chosen so the icons are distinguishable at thumbnail size.
    /// </summary>
    private static void DrawShape(SKCanvas c, TerrainBrushMode mode)
    {
        using var ground = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(85, 110, 60) };
        using var groundStroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = new SKColor(50, 65, 35), StrokeWidth = 2 };
        using var glyph = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var glyphStroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = new SKColor(20, 20, 20, 220), StrokeWidth = 2 };

        // Baseline ground line (helps anchor the icon visually).
        float baseY = 72;
        switch (mode)
        {
            case TerrainBrushMode.Raise:
            {
                // Small mound + green up arrow.
                DrawMound(c, ground, groundStroke, cx: 48, baseY: baseY, halfWidth: 32, peakY: 56);
                glyph.Color = new SKColor(80, 220, 100);
                DrawArrow(c, glyph, glyphStroke, cx: 48, fromY: 70, toY: 18);
                break;
            }
            case TerrainBrushMode.Lower:
            {
                // Small dip + red down arrow.
                DrawDip(c, ground, groundStroke, cx: 48, baseY: baseY, halfWidth: 32, troughY: 84);
                glyph.Color = new SKColor(230, 90, 70);
                DrawArrow(c, glyph, glyphStroke, cx: 48, fromY: 26, toY: 78);
                break;
            }
            case TerrainBrushMode.Smooth:
            {
                // Sine wave fading to flat — light blue, suggests "evening out".
                using var stroke = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    Color = new SKColor(120, 200, 240),
                    StrokeWidth = 5,
                    StrokeCap = SKStrokeCap.Round,
                };
                using var path = new SKPath();
                path.MoveTo(12, 60);
                path.CubicTo(24, 24, 36, 96, 48, 60);
                path.CubicTo(60, 36, 72, 84, 84, 60);
                c.DrawPath(path, stroke);
                // Flat shadow under the wave for context.
                using var flat = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    Color = new SKColor(120, 200, 240, 100),
                    StrokeWidth = 3,
                };
                c.DrawLine(12, 80, 84, 80, flat);
                break;
            }
            case TerrainBrushMode.Flatten:
            {
                // Horizontal bar with two arrows compressing it from above. Grey, suggests "press flat".
                glyph.Color = new SKColor(210, 210, 210);
                // Compression arrows
                DrawArrow(c, glyph, glyphStroke, cx: 32, fromY: 14, toY: 52);
                DrawArrow(c, glyph, glyphStroke, cx: 64, fromY: 14, toY: 52);
                // Flat ground bar
                using var bar = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(180, 180, 180) };
                using var barStroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = new SKColor(60, 60, 60), StrokeWidth = 2 };
                var r = new SKRect(10, 64, 86, 78);
                c.DrawRoundRect(r, 3, 3, bar);
                c.DrawRoundRect(r, 3, 3, barStroke);
                break;
            }
            case TerrainBrushMode.Plateau:
            {
                // Stepped trapezoid (flat-topped hill). Yellow-ochre to read as "table-top terrain".
                using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(210, 175, 80) };
                using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = new SKColor(120, 90, 30), StrokeWidth = 2 };
                using var path = new SKPath();
                path.MoveTo(10, 80);
                path.LineTo(24, 80);
                path.LineTo(34, 56);
                path.LineTo(62, 56);
                path.LineTo(72, 80);
                path.LineTo(86, 80);
                path.Close();
                c.DrawPath(path, fill);
                c.DrawPath(path, stroke);
                // Highlight the flat top.
                using var topLine = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = new SKColor(255, 235, 160), StrokeWidth = 3 };
                c.DrawLine(36, 56, 60, 56, topLine);
                break;
            }
        }
    }

    private static void DrawMound(SKCanvas c, SKPaint fill, SKPaint stroke, float cx, float baseY, float halfWidth, float peakY)
    {
        using var path = new SKPath();
        path.MoveTo(cx - halfWidth, baseY);
        path.CubicTo(cx - halfWidth * 0.4f, peakY, cx + halfWidth * 0.4f, peakY, cx + halfWidth, baseY);
        path.LineTo(cx - halfWidth, baseY);
        path.Close();
        c.DrawPath(path, fill);
        c.DrawPath(path, stroke);
    }

    private static void DrawDip(SKCanvas c, SKPaint fill, SKPaint stroke, float cx, float baseY, float halfWidth, float troughY)
    {
        using var path = new SKPath();
        path.MoveTo(cx - halfWidth - 8, baseY);
        path.LineTo(cx - halfWidth, baseY);
        path.CubicTo(cx - halfWidth * 0.4f, troughY, cx + halfWidth * 0.4f, troughY, cx + halfWidth, baseY);
        path.LineTo(cx + halfWidth + 8, baseY);
        path.LineTo(cx + halfWidth + 8, baseY + 10);
        path.LineTo(cx - halfWidth - 8, baseY + 10);
        path.Close();
        c.DrawPath(path, fill);
        c.DrawPath(path, stroke);
    }

    /// <summary>Vertical arrow: a shaft + a triangular head. Direction = sign(toY - fromY).</summary>
    private static void DrawArrow(SKCanvas c, SKPaint fill, SKPaint stroke, float cx, float fromY, float toY)
    {
        bool down = toY > fromY;
        float headLen = 14;
        float shaftEnd = down ? toY - headLen : toY + headLen;
        using var shaft = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = fill.Color };
        var bar = new SKRect(cx - 4, MathF.Min(fromY, shaftEnd), cx + 4, MathF.Max(fromY, shaftEnd));
        c.DrawRoundRect(bar, 2, 2, shaft);
        c.DrawRoundRect(bar, 2, 2, stroke);

        using var head = new SKPath();
        if (down)
        {
            head.MoveTo(cx, toY);
            head.LineTo(cx - 11, shaftEnd);
            head.LineTo(cx + 11, shaftEnd);
        }
        else
        {
            head.MoveTo(cx, toY);
            head.LineTo(cx - 11, shaftEnd);
            head.LineTo(cx + 11, shaftEnd);
        }
        head.Close();
        c.DrawPath(head, fill);
        c.DrawPath(head, stroke);
    }
}
