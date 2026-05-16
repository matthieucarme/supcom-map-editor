using Avalonia.Media.Imaging;
using SkiaSharp;
using SupremeCommanderEditor.Core.Models;
using SupremeCommanderEditor.Rendering;

namespace SupremeCommanderEditor.App.Services;

/// <summary>
/// Builds palette entries for every placeable <see cref="MarkerType"/>. Each entry carries a small
/// procedurally-rendered Skia icon (converted to an Avalonia <see cref="Bitmap"/>) so the palette
/// looks consistent with what the 2D view draws.
///
/// Lives in App (not Rendering) because Skia is only referenced here. The output plugs into
/// <see cref="PropCatalog"/> via <see cref="MainWindowViewModel.PropCategories"/>.
/// </summary>
public static class MarkerCatalog
{
    private const int IconSize = 96;

    /// <summary>Lookup the rendered icon for a marker type (or null if not in the catalog).</summary>
    public static Bitmap? GetIcon(MarkerType type) => _iconsByType.TryGetValue(type, out var b) ? b : null;

    /// <summary>Short English description of what a marker is for. Shown in the 2D hover popup so
    /// new mappers learn the role of each strategic / AI / engine marker without having to
    /// cross-reference the wiki.</summary>
    public static string GetDescription(MarkerType type) => type switch
    {
        MarkerType.Mass               => "Mass deposit — build a Mass Extractor here.",
        MarkerType.Hydrocarbon        => "Hydrocarbon deposit — build a Hydrocarbon Power Plant (higher yield than standard fusion).",
        MarkerType.BlankMarker        => "Player start location (ARMY_N) or generic script reference.",
        MarkerType.ExpansionArea      => "AI expansion target — build a forward base here.",
        MarkerType.LargeExpansionArea => "AI large expansion — build a full base with defenses.",
        MarkerType.NavalArea          => "AI naval target — control point at sea.",
        MarkerType.DefensePoint       => "AI defensive anchor — concentrate defenses here.",
        MarkerType.CombatZone         => "AI combat zone — push armies through this area.",
        MarkerType.RallyPoint         => "Rally point — default destination for newly produced units.",
        MarkerType.NavalRallyPoint    => "Naval rally point — destination for newly produced naval units.",
        MarkerType.ProtectedExperimentalConstruction => "Experimental build site — protected location for experimental factories.",
        MarkerType.LandPathNode       => "AI land pathfinding node — connects the land movement graph.",
        MarkerType.AirPathNode        => "AI air pathfinding node — connects the air movement graph.",
        MarkerType.WaterPathNode      => "AI water pathfinding node — connects the naval movement graph.",
        MarkerType.AmphibiousPathNode => "AI amphibious pathfinding node — connects the amphibious movement graph.",
        MarkerType.CameraInfo         => "Initial camera position — used at mission/game start.",
        MarkerType.WeatherGenerator   => "Spawns weather effects (clouds, rain, snow) in its area.",
        MarkerType.WeatherDefinition  => "Weather template referenced by Weather Generators.",
        MarkerType.Effect             => "Visual effect spawn (smoke, fire, sparkles, etc.).",
        _                             => string.Empty,
    };

    private static readonly Dictionary<MarkerType, Bitmap> _iconsByType = new();

    /// <summary>
    /// Ordered list mirroring the most common mapper workflow: resources first, then spawns,
    /// then expansion / military zones, then AI pathfinding, then world ambience markers.
    /// </summary>
    private static readonly (MarkerType type, string label)[] Ordered =
    [
        (MarkerType.Mass,                          "Mass"),
        (MarkerType.Hydrocarbon,                   "Hydrocarbon"),
        (MarkerType.BlankMarker,                   "Player spawn"),
        (MarkerType.ExpansionArea,                 "Expansion"),
        (MarkerType.LargeExpansionArea,            "Large expansion"),
        (MarkerType.NavalArea,                     "Naval area"),
        (MarkerType.DefensePoint,                  "Defense point"),
        (MarkerType.CombatZone,                    "Combat zone"),
        (MarkerType.RallyPoint,                    "Rally point"),
        (MarkerType.NavalRallyPoint,               "Naval rally"),
        (MarkerType.ProtectedExperimentalConstruction, "Protected exp."),
        (MarkerType.LandPathNode,                  "Land path"),
        (MarkerType.AirPathNode,                   "Air path"),
        (MarkerType.WaterPathNode,                 "Water path"),
        (MarkerType.AmphibiousPathNode,            "Amphibious path"),
        (MarkerType.CameraInfo,                    "Camera info"),
        (MarkerType.WeatherGenerator,              "Weather gen"),
        (MarkerType.WeatherDefinition,             "Weather def"),
        (MarkerType.Effect,                        "Effect"),
    ];

    /// <summary>Single "Markers" category with one entry per <see cref="MarkerType"/>.
    /// IMPORTANT: must be declared AFTER <see cref="Ordered"/> and <see cref="_iconsByType"/> —
    /// static field initializers run in source order, so building too early sees them as null.</summary>
    public static IReadOnlyList<PropCategory> All { get; } = Build();

    private static IReadOnlyList<PropCategory> Build()
    {
        var entries = new List<PropEntry>();
        foreach (var (type, label) in Ordered)
        {
            var icon = RenderIcon(type);
            _iconsByType[type] = icon;
            entries.Add(new PropEntry(
                BlueprintPath: string.Empty,
                Biome: type.ToString(),  // sort hint
                Kind: "Markers",
                DisplayName: label,
                Icon: icon,
                EntryKind: PaletteEntryKind.Marker,
                MarkerKind: type));
        }
        return [new PropCategory("Markers", entries)];
    }

    /// <summary>Render a marker type as a 96×96 Avalonia bitmap using the same shapes as the 2D view.</summary>
    private static Bitmap RenderIcon(MarkerType type)
    {
        using var sk = new SKBitmap(IconSize, IconSize, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(sk))
        {
            canvas.Clear(SKColors.Transparent);
            // Center the shape and scale it up — the in-map shapes are sized for world units,
            // here we want a chunky preview that reads in a 86×100 menu card.
            float cx = IconSize / 2f;
            float cy = IconSize / 2f;
            DrawShape(canvas, type, cx, cy);
        }
        using var data = sk.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream(data.ToArray());
        return new Bitmap(ms);
    }

    /// <summary>Replicates the vector shapes from <c>SkiaMapControl.DrawMarkerIcon</c> at icon scale.
    /// Kept in sync so palette previews match what the user sees placed on the map.</summary>
    private static void DrawShape(SKCanvas c, MarkerType t, float x, float y)
    {
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var stroke = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f, Color = new SKColor(20, 20, 20, 220)
        };
        switch (t)
        {
            case MarkerType.Mass:
                fill.Color = new SKColor(40, 220, 70);
                c.DrawCircle(x, y, 26, fill); c.DrawCircle(x, y, 26, stroke);
                break;
            case MarkerType.Hydrocarbon:
                fill.Color = new SKColor(60, 220, 220);
                DrawPlus(c, x, y, 32, fill); DrawPlus(c, x, y, 32, stroke);
                break;
            case MarkerType.BlankMarker:
                fill.Color = new SKColor(255, 70, 70);
                DrawStar(c, x, y, 36, 16, 5, fill);
                DrawStar(c, x, y, 36, 16, 5, stroke);
                break;
            case MarkerType.ExpansionArea:
            case MarkerType.LargeExpansionArea:
                fill.Color = new SKColor(0, 200, 200, 120);
                float er = t == MarkerType.LargeExpansionArea ? 34f : 28f;
                c.DrawCircle(x, y, er, fill);
                stroke.Color = new SKColor(0, 220, 220);
                c.DrawCircle(x, y, er, stroke);
                break;
            case MarkerType.NavalArea:
                fill.Color = new SKColor(80, 130, 255);
                DrawTriangle(c, x, y, 32, false, fill); DrawTriangle(c, x, y, 32, false, stroke);
                break;
            case MarkerType.DefensePoint:
                fill.Color = new SKColor(60, 200, 60);
                DrawShield(c, x, y, 30, fill); DrawShield(c, x, y, 30, stroke);
                break;
            case MarkerType.RallyPoint:
            case MarkerType.NavalRallyPoint:
                fill.Color = t == MarkerType.NavalRallyPoint
                    ? new SKColor(120, 180, 255) : new SKColor(240, 200, 50);
                DrawFlag(c, x, y, 28, fill, stroke);
                break;
            case MarkerType.LandPathNode:
            case MarkerType.WaterPathNode:
            case MarkerType.AmphibiousPathNode:
                fill.Color = new SKColor(170, 170, 170, 220);
                c.DrawRect(x - 18, y - 18, 36, 36, fill);
                c.DrawRect(x - 18, y - 18, 36, 36, stroke);
                break;
            case MarkerType.AirPathNode:
                fill.Color = new SKColor(140, 200, 255, 220);
                c.DrawRect(x - 18, y - 18, 36, 36, fill);
                c.DrawRect(x - 18, y - 18, 36, 36, stroke);
                break;
            case MarkerType.CombatZone:
                fill.Color = new SKColor(255, 100, 0, 140);
                c.DrawCircle(x, y, 30, fill);
                stroke.Color = new SKColor(255, 140, 50);
                c.DrawCircle(x, y, 30, stroke);
                break;
            case MarkerType.ProtectedExperimentalConstruction:
                fill.Color = new SKColor(255, 80, 200, 160);
                DrawTriangle(c, x, y, 30, true, fill); DrawTriangle(c, x, y, 30, true, stroke);
                break;
            case MarkerType.CameraInfo:
                fill.Color = new SKColor(180, 180, 220);
                DrawDiamond(c, x, y, 26, fill); DrawDiamond(c, x, y, 26, stroke);
                break;
            case MarkerType.WeatherGenerator:
            case MarkerType.WeatherDefinition:
                fill.Color = new SKColor(200, 220, 255, 180);
                c.DrawCircle(x, y - 4, 18, fill);
                c.DrawCircle(x + 10, y + 2, 16, fill);
                c.DrawCircle(x - 12, y + 2, 14, fill);
                stroke.Color = new SKColor(120, 140, 180);
                c.DrawCircle(x, y - 4, 18, stroke);
                c.DrawCircle(x + 10, y + 2, 16, stroke);
                c.DrawCircle(x - 12, y + 2, 14, stroke);
                break;
            case MarkerType.Effect:
                fill.Color = new SKColor(255, 220, 80);
                DrawStar(c, x, y, 30, 12, 4, fill); DrawStar(c, x, y, 30, 12, 4, stroke);
                break;
            default:
                fill.Color = new SKColor(180, 180, 180);
                c.DrawCircle(x, y, 24, fill); c.DrawCircle(x, y, 24, stroke);
                break;
        }
    }

    // --- Shape helpers (mirrors SkiaMapControl helpers at palette scale) ---

    private static void DrawDiamond(SKCanvas c, float x, float y, float r, SKPaint p)
    {
        using var path = new SKPath();
        path.MoveTo(x, y - r);
        path.LineTo(x + r, y);
        path.LineTo(x, y + r);
        path.LineTo(x - r, y);
        path.Close();
        c.DrawPath(path, p);
    }

    private static void DrawPlus(SKCanvas c, float x, float y, float r, SKPaint p)
    {
        float t = r * 0.45f;
        using var path = new SKPath();
        path.MoveTo(x - t, y - r); path.LineTo(x + t, y - r);
        path.LineTo(x + t, y - t); path.LineTo(x + r, y - t);
        path.LineTo(x + r, y + t); path.LineTo(x + t, y + t);
        path.LineTo(x + t, y + r); path.LineTo(x - t, y + r);
        path.LineTo(x - t, y + t); path.LineTo(x - r, y + t);
        path.LineTo(x - r, y - t); path.LineTo(x - t, y - t);
        path.Close();
        c.DrawPath(path, p);
    }

    private static void DrawStar(SKCanvas c, float x, float y, float outer, float inner, int points, SKPaint p)
    {
        using var path = new SKPath();
        float step = MathF.PI / points;
        for (int i = 0; i < points * 2; i++)
        {
            float radius = (i & 1) == 0 ? outer : inner;
            float angle = -MathF.PI / 2f + i * step;
            float px = x + MathF.Cos(angle) * radius;
            float py = y + MathF.Sin(angle) * radius;
            if (i == 0) path.MoveTo(px, py);
            else path.LineTo(px, py);
        }
        path.Close();
        c.DrawPath(path, p);
    }

    private static void DrawTriangle(SKCanvas c, float x, float y, float r, bool pointingUp, SKPaint p)
    {
        using var path = new SKPath();
        if (pointingUp) { path.MoveTo(x, y - r); path.LineTo(x + r, y + r * 0.8f); path.LineTo(x - r, y + r * 0.8f); }
        else { path.MoveTo(x, y + r); path.LineTo(x + r, y - r * 0.8f); path.LineTo(x - r, y - r * 0.8f); }
        path.Close();
        c.DrawPath(path, p);
    }

    private static void DrawShield(SKCanvas c, float x, float y, float r, SKPaint p)
    {
        using var path = new SKPath();
        path.MoveTo(x, y - r);
        path.LineTo(x + r * 0.9f, y - r * 0.3f);
        path.LineTo(x + r * 0.6f, y + r);
        path.LineTo(x - r * 0.6f, y + r);
        path.LineTo(x - r * 0.9f, y - r * 0.3f);
        path.Close();
        c.DrawPath(path, p);
    }

    private static void DrawFlag(SKCanvas c, float x, float y, float r, SKPaint fill, SKPaint stroke)
    {
        using (var pole = new SKPath())
        {
            pole.MoveTo(x - r * 0.5f, y + r);
            pole.LineTo(x - r * 0.5f, y - r);
            c.DrawPath(pole, stroke);
        }
        using var path = new SKPath();
        path.MoveTo(x - r * 0.5f, y - r);
        path.LineTo(x + r, y - r * 0.4f);
        path.LineTo(x - r * 0.5f, y + r * 0.2f);
        path.Close();
        c.DrawPath(path, fill);
        c.DrawPath(path, stroke);
    }
}
