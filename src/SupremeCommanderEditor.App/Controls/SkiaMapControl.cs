using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;
using SupremeCommanderEditor.App.Services;
using SupremeCommanderEditor.Core.Models;

namespace SupremeCommanderEditor.App.Controls;

public class SkiaMapControl : Panel
{
    private ScMap? _map;
    private SKBitmap? _heightmapBitmap;
    private WriteableBitmap? _avaloniaBuffer;

    /// <summary>Optional service that resolves marker icons from game data; null = use vector fallback.</summary>
    public MarkerIconService? MarkerIcons { get; set; }

    /// <summary>Fired when the user paints with the prop brush. Args: (mapX, mapZ).</summary>
    public event Action<float, float>? PropBrushDrag;
    /// <summary>Fired when the prop brush stroke starts (left button down in brush mode).</summary>
    public event Action<float, float>? PropBrushStart;
    /// <summary>Fired when the prop brush stroke ends (left button up).</summary>
    public event Action? PropBrushEnd;
    /// <summary>Fired on a single left-click when the host signals that a prop is selected and
    /// the brush is off — used to drop one prop at the cursor position.</summary>
    public event Action<float, float>? SinglePlaceProp;

    public static readonly StyledProperty<bool> IsSinglePlaceActiveProperty =
        AvaloniaProperty.Register<SkiaMapControl, bool>(nameof(IsSinglePlaceActive));
    public bool IsSinglePlaceActive { get => GetValue(IsSinglePlaceActiveProperty); set => SetValue(IsSinglePlaceActiveProperty, value); }

    // === Hover popup state (read-only from XAML via Popup IsOpen/Source bindings) ===
    public static readonly StyledProperty<Prop?> HoveredPropProperty =
        AvaloniaProperty.Register<SkiaMapControl, Prop?>(nameof(HoveredProp));
    public Prop? HoveredProp { get => GetValue(HoveredPropProperty); set => SetValue(HoveredPropProperty, value); }

    public static readonly StyledProperty<Bitmap?> HoveredPropIconProperty =
        AvaloniaProperty.Register<SkiaMapControl, Bitmap?>(nameof(HoveredPropIcon));
    public Bitmap? HoveredPropIcon { get => GetValue(HoveredPropIconProperty); set => SetValue(HoveredPropIconProperty, value); }

    public static readonly StyledProperty<string?> HoveredPropNameProperty =
        AvaloniaProperty.Register<SkiaMapControl, string?>(nameof(HoveredPropName));
    public string? HoveredPropName { get => GetValue(HoveredPropNameProperty); set => SetValue(HoveredPropNameProperty, value); }

    public static readonly StyledProperty<bool> HasHoveredPropProperty =
        AvaloniaProperty.Register<SkiaMapControl, bool>(nameof(HasHoveredProp));
    public bool HasHoveredProp { get => GetValue(HasHoveredPropProperty); set => SetValue(HasHoveredPropProperty, value); }

    public static readonly StyledProperty<bool> IsPropBrushActiveProperty =
        AvaloniaProperty.Register<SkiaMapControl, bool>(nameof(IsPropBrushActive));
    public static readonly StyledProperty<double> PropBrushRadiusProperty =
        AvaloniaProperty.Register<SkiaMapControl, double>(nameof(PropBrushRadius), defaultValue: 8);

    /// <summary>Set by the host: when true, left-click-drag stamps props instead of selecting.</summary>
    public bool IsPropBrushActive { get => GetValue(IsPropBrushActiveProperty); set => SetValue(IsPropBrushActiveProperty, value); }
    /// <summary>Brush radius in map units, used to render the cursor circle.</summary>
    public double PropBrushRadius { get => GetValue(PropBrushRadiusProperty); set => SetValue(PropBrushRadiusProperty, value); }
    private readonly Image _imageControl;
    private float _zoom = 1f;
    private float _panX, _panY;
    private Point _lastMouse;
    private bool _isDragging;
    private bool _isPanning;
    private bool _isDraggingMarker;
    private bool _isDraggingProp;
    private System.Numerics.Vector3 _propDragStartPos;
    private bool _isDraggingUnit;
    private System.Numerics.Vector3 _unitDragStartPos;
    private UnitSpawn? _selectedUnitSpawn;
    private Marker? _selectedMarker;
    private Prop? _selectedProp;

    // Box-selection state for multi-element selection (Shift+drag). Spans props, markers, units.
    private bool _isBoxSelecting;
    private Point _boxStartScreen;
    private Point _boxEndScreen;
    private readonly HashSet<Prop> _multiSelectedProps = new();
    private readonly HashSet<Marker> _multiSelectedMarkers = new();
    private readonly HashSet<UnitSpawn> _multiSelectedUnits = new();

    /// <summary>Snapshot of the currently multi-selected props (read-only).</summary>
    public IReadOnlyCollection<Prop> MultiSelectedProps => _multiSelectedProps;
    /// <summary>Snapshot of the currently multi-selected markers (read-only).</summary>
    public IReadOnlyCollection<Marker> MultiSelectedMarkers => _multiSelectedMarkers;
    /// <summary>Snapshot of the currently multi-selected unit spawns (read-only).</summary>
    public IReadOnlyCollection<UnitSpawn> MultiSelectedUnits => _multiSelectedUnits;

    /// <summary>True when at least one element of any kind is in the multi-selection.</summary>
    public bool HasMultiSelection =>
        _multiSelectedProps.Count + _multiSelectedMarkers.Count + _multiSelectedUnits.Count > 0;

    /// <summary>Fired whenever the multi-selection changes (any type).</summary>
    public event Action? MultiSelectionChanged;

    // Multi-drag state: snapshotted start positions per entity + the cursor's map-space anchor.
    // Translates every selected element by the same delta during the drag.
    private bool _isDraggingMulti;
    private System.Numerics.Vector2 _multiDragStartCursor;
    private readonly Dictionary<Prop, System.Numerics.Vector3> _multiDragStartProp = new();
    private readonly Dictionary<Marker, System.Numerics.Vector3> _multiDragStartMarker = new();
    private readonly Dictionary<UnitSpawn, System.Numerics.Vector3> _multiDragStartUnit = new();

    /// <summary>Fired when a group drag ends. Carries the pre-drag positions per entity so the
    /// host can push a single undoable batch move (and clamp final Y to terrain).</summary>
    public event Action<IReadOnlyDictionary<Prop, System.Numerics.Vector3>,
                       IReadOnlyDictionary<Marker, System.Numerics.Vector3>,
                       IReadOnlyDictionary<UnitSpawn, System.Numerics.Vector3>>? MultiSelectionMoved;

    /// <summary>World coordinates (mapX, mapZ) of the cursor on the 2D viewport. Used for paste-at-cursor.</summary>
    public System.Numerics.Vector2? CursorWorldPos { get; private set; }

    /// <summary>Fired when a marker is selected or deselected (null).</summary>
    public event Action<Marker?>? MarkerSelected;

    /// <summary>Fired when user clicks to place a marker (map X, map Z).</summary>
    public event Action<float, float>? MarkerPlaceRequested;

    /// <summary>Fired when a marker drag ends. Args: marker, position at drag start.</summary>
    public event Action<Marker, System.Numerics.Vector3>? MarkerMoved;
    /// <summary>Fired when a prop drag ends. Args: the prop, its position before the drag.</summary>
    public event Action<Prop, System.Numerics.Vector3>? PropMoved;
    /// <summary>Fired when the user clicks (without drag) on a UnitSpawn — used to sync the VM selection.</summary>
    public event Action<UnitSpawn?>? UnitSpawnSelected;
    /// <summary>Fired when a UnitSpawn drag ends. Args: the unit, its position before the drag.</summary>
    public event Action<UnitSpawn, System.Numerics.Vector3>? UnitSpawnMoved;

    public UnitSpawn? SelectedUnitSpawn
    {
        get => _selectedUnitSpawn;
        set
        {
            if (_selectedUnitSpawn == value) return;
            _selectedUnitSpawn = value;
            RedrawBitmap();
        }
    }

    private System.Numerics.Vector3 _markerDragStartPos;

    /// <summary>When true, left click places a new marker instead of selecting.</summary>
    public bool IsPlacingMarker { get; set; }

    /// <summary>Optional filter to hide certain marker types.</summary>
    public Func<Marker, bool>? MarkerFilter { get; set; }

    public static readonly StyledProperty<ScMap?> MapProperty =
        AvaloniaProperty.Register<SkiaMapControl, ScMap?>(nameof(Map));

    public ScMap? Map
    {
        get => GetValue(MapProperty);
        set => SetValue(MapProperty, value);
    }

    // Grid overlay properties (bound from MainWindowViewModel)
    public static readonly StyledProperty<bool> ShowGridProperty =
        AvaloniaProperty.Register<SkiaMapControl, bool>(nameof(ShowGrid), defaultValue: true);
    public static readonly StyledProperty<bool> ShowDiagonalGridProperty =
        AvaloniaProperty.Register<SkiaMapControl, bool>(nameof(ShowDiagonalGrid));
    public static readonly StyledProperty<int> GridStepProperty =
        AvaloniaProperty.Register<SkiaMapControl, int>(nameof(GridStep), defaultValue: 32);
    public static readonly StyledProperty<bool> SnapToGridProperty =
        AvaloniaProperty.Register<SkiaMapControl, bool>(nameof(SnapToGrid));

    public bool ShowGrid { get => GetValue(ShowGridProperty); set => SetValue(ShowGridProperty, value); }
    public bool ShowDiagonalGrid { get => GetValue(ShowDiagonalGridProperty); set => SetValue(ShowDiagonalGridProperty, value); }
    public int GridStep { get => GetValue(GridStepProperty); set => SetValue(GridStepProperty, value); }
    public bool SnapToGrid { get => GetValue(SnapToGridProperty); set => SetValue(SnapToGridProperty, value); }

    public SkiaMapControl()
    {
        Background = Brushes.Transparent;
        Focusable = true;
        IsHitTestVisible = true;
        ClipToBounds = true;

        _imageControl = new Image
        {
            Stretch = Stretch.None,
            IsHitTestVisible = false  // Let pointer events pass through to this Panel
        };
        Children.Add(_imageControl);
    }

    static SkiaMapControl()
    {
        AffectsRender<SkiaMapControl>(MapProperty);
        MapProperty.Changed.AddClassHandler<SkiaMapControl>((c, _) => c.OnMapChanged());
        ShowGridProperty.Changed.AddClassHandler<SkiaMapControl>((c, _) => c.RedrawBitmap());
        ShowDiagonalGridProperty.Changed.AddClassHandler<SkiaMapControl>((c, _) => c.RedrawBitmap());
        GridStepProperty.Changed.AddClassHandler<SkiaMapControl>((c, _) => c.RedrawBitmap());
        IsPropBrushActiveProperty.Changed.AddClassHandler<SkiaMapControl>((c, _) => c.RedrawBitmap());
        PropBrushRadiusProperty.Changed.AddClassHandler<SkiaMapControl>((c, _) => c.RedrawBitmap());
    }

    private void OnMapChanged()
    {
        _map = Map;
        _heightmapBitmap?.Dispose();
        _heightmapBitmap = null;
        _hasTopDownSnapshot = false;

        if (_map != null)
        {
            BuildHeightmapBitmap(_map.Heightmap);
            FitToMap();
        }

        RedrawBitmap();
    }

    /// <summary>Rebuild the heightmap bitmap from current data (after terrain editing).</summary>
    public void RefreshHeightmap()
    {
        if (_map == null) return;
        // If the 3D view will send us an updated snapshot, keep the existing one until it arrives.
        // Otherwise (no snapshot yet), regenerate the height-colored fallback.
        if (!_hasTopDownSnapshot)
        {
            _heightmapBitmap?.Dispose();
            BuildHeightmapBitmap(_map.Heightmap);
        }
        RedrawBitmap();
    }

    /// <summary>
    /// Install an RGBA bitmap (top-left origin) captured from the 3D view as the 2D background.
    /// Called by the host after the OpenGL control fires its TopDownReady event.
    /// </summary>
    public void SetTopDownBitmap(byte[] rgba, int width, int height)
    {
        if (rgba.Length < width * height * 4) return;
        _heightmapBitmap?.Dispose();
        _heightmapBitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        System.Runtime.InteropServices.Marshal.Copy(rgba, 0, _heightmapBitmap.GetPixels(), rgba.Length);
        _hasTopDownSnapshot = true;

        // Build an Avalonia-side bitmap of the same data so the symmetry thumbnails can display it
        // without going through Skia. Replacing the bitmap fires the styled property changed.
        var avalonia = new WriteableBitmap(
            new PixelSize(width, height), new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Opaque);
        using (var locked = avalonia.Lock())
        {
            System.Runtime.InteropServices.Marshal.Copy(rgba, 0, locked.Address, rgba.Length);
        }
        TopDownBackground = avalonia;

        RedrawBitmap();
    }

    private bool _hasTopDownSnapshot;

    public static readonly StyledProperty<IImage?> TopDownBackgroundProperty =
        AvaloniaProperty.Register<SkiaMapControl, IImage?>(nameof(TopDownBackground));

    /// <summary>Exposed snapshot bitmap, suitable for binding to the symmetry thumbnails.</summary>
    public IImage? TopDownBackground
    {
        get => GetValue(TopDownBackgroundProperty);
        private set => SetValue(TopDownBackgroundProperty, value);
    }

    private unsafe void BuildHeightmapBitmap(Heightmap hm)
    {
        // Height-colored placeholder: used until the 3D view delivers a top-down snapshot via
        // SetTopDownBitmap. Once a snapshot arrives, _hasTopDownSnapshot suppresses regeneration.
        int w = hm.Width;
        int h = hm.Height;
        _heightmapBitmap = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Opaque);
        var ptr = (byte*)_heightmapBitmap.GetPixels();

        ushort min = ushort.MaxValue, max = ushort.MinValue;
        var data = hm.Data;
        int hmW = w + 1;
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] < min) min = data[i];
            if (data[i] > max) max = data[i];
        }
        float invRange = 1f / Math.Max(max - min, 1);

        for (int y = 0; y < h; y++)
        {
            int rowOffset = y * hmW;
            int pixRow = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                float t = (data[rowOffset + x] - min) * invRange;
                int pixIdx = pixRow + x * 4;
                byte r, g, b;
                if (t < 0.2f) { float s = t * 5f; r = (byte)(30 + 20 * s); g = (byte)(60 + 80 * s); b = (byte)(120 - 70 * s); }
                else if (t < 0.5f) { float s = (t - 0.2f) * 3.333f; r = (byte)(50 + 90 * s); g = (byte)(140 - 20 * s); b = (byte)(50 + 10 * s); }
                else if (t < 0.8f) { float s = (t - 0.5f) * 3.333f; r = (byte)(140 + 40 * s); g = (byte)(120 + 50 * s); b = (byte)(60 + 90 * s); }
                else { float s = (t - 0.8f) * 5f; r = (byte)(180 + 65 * s); g = (byte)(170 + 75 * s); b = (byte)(150 + 95 * s); }
                ptr[pixIdx] = r; ptr[pixIdx + 1] = g; ptr[pixIdx + 2] = b; ptr[pixIdx + 3] = 255;
            }
        }
    }

    private void FitToMap()
    {
        if (_map == null) return;
        float mapW = _map.Heightmap.Width;
        float mapH = _map.Heightmap.Height;
        float viewW = (float)Bounds.Width;
        float viewH = (float)Bounds.Height;
        if (viewW < 1 || viewH < 1) return;

        _zoom = Math.Min(viewW / mapW, viewH / mapH) * 0.9f;
        _panX = (viewW - mapW * _zoom) / 2f;
        _panY = (viewH - mapH * _zoom) / 2f;
    }

    private void RedrawBitmap()
    {
        int w = Math.Max(1, (int)Bounds.Width);
        int h = Math.Max(1, (int)Bounds.Height);
        if (w < 2 || h < 2) return;

        if (_map == null || _heightmapBitmap == null)
        {
            _imageControl.Source = null;
            return;
        }

        using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;

        canvas.Clear(new SKColor(20, 20, 30));
        canvas.Save();
        canvas.Translate(_panX, _panY);
        canvas.Scale(_zoom, _zoom);

        // Bitmap is at its native resolution (heightmap-sized fallback or 3D snapshot resolution);
        // draw it stretched to fill the map's world-space bounds so canvas scaling lines up.
        var destRect = new SKRect(0, 0, _map.Heightmap.Width, _map.Heightmap.Height);
        canvas.DrawBitmap(_heightmapBitmap, destRect);

        DrawProps(canvas);
        DrawInitialUnits(canvas);
        DrawMarkers(canvas);
        DrawGrid(canvas);
        DrawBrushCursor(canvas);
        canvas.Restore();
        DrawBoxSelection(canvas);
        DrawHud(canvas, w, h);

        // Copy to Avalonia WriteableBitmap
        if (_avaloniaBuffer == null || _avaloniaBuffer.PixelSize.Width != w || _avaloniaBuffer.PixelSize.Height != h)
        {
            _avaloniaBuffer?.Dispose();
            _avaloniaBuffer = new WriteableBitmap(
                new PixelSize(w, h), new Vector(96, 96),
                Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Premul);
        }

        using (var locked = _avaloniaBuffer.Lock())
        {
            using var snapshot = surface.Snapshot();
            using var pixmap = snapshot.PeekPixels();
            if (pixmap != null)
            {
                unsafe
                {
                    Buffer.MemoryCopy(
                        (void*)pixmap.GetPixels(),
                        (void*)locked.Address,
                        locked.RowBytes * h,
                        pixmap.RowBytes * h);
                }
            }
        }

        // Force Image to pick up new pixel data by toggling source
        _imageControl.Source = null;
        _imageControl.Source = _avaloniaBuffer;
        _imageControl.InvalidateVisual();
    }

    // Cache of decoded SC prop icons (PNG bytes → SKBitmap). Lazy-loaded on first hit. A null value
    // means we tried and there is no icon for that blueprint (don't retry).
    private readonly Dictionary<string, SKBitmap?> _propIconCache = new();

    private SKBitmap? GetPropIcon(string blueprintPath)
    {
        if (_propIconCache.TryGetValue(blueprintPath, out var cached)) return cached;
        SKBitmap? bmp = null;
        var bytes = SupremeCommanderEditor.Rendering.PropIconService.LoadIconBytes(blueprintPath);
        if (bytes != null)
        {
            try { bmp = SKBitmap.Decode(bytes); } catch { bmp = null; }
        }
        _propIconCache[blueprintPath] = bmp;
        return bmp;
    }

    /// <summary>Zoom level above which we draw each prop as its 3D-rendered icon instead of a dot.
    /// Below this, all props would render as a few pixels of overlapping icons — circles read better.</summary>
    private const float PropIconZoomThreshold = 4.5f;

    private void DrawProps(SKCanvas canvas)
    {
        if (_map == null || _map.Props.Count == 0) return;
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(120, 90, 50, 200) };
        using var stroke = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke,
            Color = new SKColor(20, 20, 20, 200),
            StrokeWidth = 0.8f / Math.Max(_zoom, 0.1f)
        };
        using var selectionPaint = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke,
            Color = new SKColor(255, 255, 0),
            StrokeWidth = 2f / Math.Max(_zoom, 0.1f)
        };

        const float r = 2.2f;
        bool useIcons = _zoom >= PropIconZoomThreshold;
        // Icon side length in *map* units: keep ~28px on screen regardless of zoom.
        float iconHalf = (48f / _zoom) * 0.5f;
        // Bilinear bitmap filtering for crisp icons at any zoom (the 192px source gets downscaled).
        using var iconPaint = new SKPaint { FilterQuality = SKFilterQuality.High };

        foreach (var prop in _map.Props)
        {
            float x = prop.Position.X;
            float y = prop.Position.Z;
            bool isSelected = prop == _selectedProp || _multiSelectedProps.Contains(prop);

            SKBitmap? icon = useIcons ? GetPropIcon(prop.BlueprintPath) : null;
            if (icon != null)
            {
                var rect = new SKRect(x - iconHalf, y - iconHalf, x + iconHalf, y + iconHalf);
                canvas.DrawBitmap(icon, rect, iconPaint);
                if (isSelected) canvas.DrawRect(rect, selectionPaint);
            }
            else
            {
                canvas.DrawCircle(x, y, r, fill);
                canvas.DrawCircle(x, y, r, stroke);
                if (isSelected) canvas.DrawCircle(x, y, r + 3f, selectionPaint);
            }
        }
    }

    // Cache of unit icons (blueprintId → SKBitmap). Same lifetime as _propIconCache.
    private readonly Dictionary<string, SKBitmap?> _unitIconCache = new();

    private SKBitmap? GetUnitIcon(string blueprintId)
    {
        if (_unitIconCache.TryGetValue(blueprintId, out var cached)) return cached;
        SKBitmap? bmp = null;
        var bytes = SupremeCommanderEditor.Rendering.PropIconService.LoadUnitIconBytes(blueprintId);
        if (bytes != null) { try { bmp = SKBitmap.Decode(bytes); } catch { bmp = null; } }
        _unitIconCache[blueprintId] = bmp;
        return bmp;
    }

    private void DrawInitialUnits(SKCanvas canvas)
    {
        if (_map == null) return;
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var stroke = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke,
            Color = new SKColor(20, 20, 20, 220),
            StrokeWidth = 1f / Math.Max(_zoom, 0.1f),
        };
        using var selPaint = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke,
            Color = new SKColor(255, 255, 0),
            StrokeWidth = 2f / Math.Max(_zoom, 0.1f)
        };
        bool useIcons = _zoom >= PropIconZoomThreshold;
        float iconHalf = (48f / _zoom) * 0.5f;
        using var iconPaint = new SKPaint { FilterQuality = SKFilterQuality.High };

        foreach (var army in _map.Info.Armies)
        {
            // Neutral / civilian armies render in a distinct cyan so they stand out from player slots.
            bool isNeutral = army.Name.StartsWith("NEUTRAL", StringComparison.OrdinalIgnoreCase)
                          || army.Name.Equals("ARMY_9", StringComparison.OrdinalIgnoreCase) // common neutral slot
                          || army.Name.StartsWith("CIV", StringComparison.OrdinalIgnoreCase);
            fill.Color = isNeutral
                ? new SKColor(80, 220, 220, 220)   // cyan = neutral
                : new SKColor(255, 160, 80, 220);  // orange = player-owned pre-placed
            foreach (var u in army.InitialUnits)
            {
                float x = u.Position.X;
                float y = u.Position.Z;
                bool isSelected = u == _selectedUnitSpawn || _multiSelectedUnits.Contains(u);

                SKBitmap? icon = useIcons ? GetUnitIcon(u.BlueprintId) : null;
                if (icon != null)
                {
                    var rect = new SKRect(x - iconHalf, y - iconHalf, x + iconHalf, y + iconHalf);
                    canvas.DrawBitmap(icon, rect, iconPaint);
                    // Faction tint along the bottom edge so we still see who owns it under the icon.
                    using var tint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill, Color = fill.Color };
                    var bar = new SKRect(x - iconHalf, y + iconHalf - iconHalf * 0.18f, x + iconHalf, y + iconHalf);
                    canvas.DrawRect(bar, tint);
                    if (isSelected) canvas.DrawRect(rect, selPaint);
                }
                else
                {
                    // Square representation (the original style).
                    var rect = new SKRect(x - 2.5f, y - 2.5f, x + 2.5f, y + 2.5f);
                    canvas.DrawRect(rect, fill);
                    canvas.DrawRect(rect, stroke);
                    if (isSelected)
                    {
                        var halo = new SKRect(x - 4f, y - 4f, x + 4f, y + 4f);
                        canvas.DrawRect(halo, selPaint);
                    }
                }
            }
        }
    }

    private void DrawBrushCursor(SKCanvas canvas)
    {
        if (!IsPropBrushActive || CursorWorldPos == null) return;
        using var pen = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke,
            Color = new SKColor(120, 240, 120, 200),
            StrokeWidth = 1.5f / Math.Max(_zoom, 0.1f),
        };
        canvas.DrawCircle(CursorWorldPos.Value.X, CursorWorldPos.Value.Y, (float)PropBrushRadius, pen);
    }

    private void DrawBoxSelection(SKCanvas canvas)
    {
        if (!_isBoxSelecting) return;
        // The box is in screen coords (translated by pan/zoom), so we draw it AFTER canvas.Restore.
        var rect = new SKRect(
            Math.Min((float)_boxStartScreen.X, (float)_boxEndScreen.X),
            Math.Min((float)_boxStartScreen.Y, (float)_boxEndScreen.Y),
            Math.Max((float)_boxStartScreen.X, (float)_boxEndScreen.X),
            Math.Max((float)_boxStartScreen.Y, (float)_boxEndScreen.Y));
        using var fill = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill, Color = new SKColor(120, 180, 255, 50) };
        using var stroke = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Stroke, Color = new SKColor(120, 200, 255), StrokeWidth = 1 };
        canvas.DrawRect(rect, fill);
        canvas.DrawRect(rect, stroke);
    }

    /// <summary>Convert the current box rect to map coords and collect every scene element inside.
    /// Props, markers (subject to MarkerFilter), and pre-placed units are all captured.</summary>
    private void FinalizeBoxSelection()
    {
        _multiSelectedProps.Clear();
        _multiSelectedMarkers.Clear();
        _multiSelectedUnits.Clear();
        if (_map == null) { MultiSelectionChanged?.Invoke(); return; }
        var (x0, y0) = ScreenToMap(new Point(
            Math.Min(_boxStartScreen.X, _boxEndScreen.X),
            Math.Min(_boxStartScreen.Y, _boxEndScreen.Y)));
        var (x1, y1) = ScreenToMap(new Point(
            Math.Max(_boxStartScreen.X, _boxEndScreen.X),
            Math.Max(_boxStartScreen.Y, _boxEndScreen.Y)));

        foreach (var p in _map.Props)
        {
            float px = p.Position.X, pz = p.Position.Z;
            if (px >= x0 && px <= x1 && pz >= y0 && pz <= y1)
                _multiSelectedProps.Add(p);
        }
        foreach (var m in _map.Markers)
        {
            if (MarkerFilter != null && !MarkerFilter(m)) continue;
            float mx = m.Position.X, mz = m.Position.Z;
            if (mx >= x0 && mx <= x1 && mz >= y0 && mz <= y1)
                _multiSelectedMarkers.Add(m);
        }
        foreach (var army in _map.Info.Armies)
            foreach (var u in army.InitialUnits)
            {
                float ux = u.Position.X, uz = u.Position.Z;
                if (ux >= x0 && ux <= x1 && uz >= y0 && uz <= y1)
                    _multiSelectedUnits.Add(u);
            }

        // Clear single selections so the side panel doesn't keep stale state when a box pulls in
        // multiple items.
        if (HasMultiSelection)
        {
            _selectedMarker = null;
            _selectedProp = null;
            _selectedUnitSpawn = null;
        }
        MultiSelectionChanged?.Invoke();
    }

    /// <summary>Wipe the multi-selection (all types). Called by the host after delete/escape.</summary>
    public void ClearMultiSelection()
    {
        if (!HasMultiSelection) return;
        _multiSelectedProps.Clear();
        _multiSelectedMarkers.Clear();
        _multiSelectedUnits.Clear();
        MultiSelectionChanged?.Invoke();
        RedrawBitmap();
    }

    /// <summary>True if a hit-test at (mx,mz) lands on any element currently in the multi-selection.</summary>
    private bool HitsMultiSelection(float mx, float mz)
    {
        var hitMarker = HitTestMarker(mx, mz);
        if (hitMarker != null && _multiSelectedMarkers.Contains(hitMarker)) return true;
        var hitProp = HitTestProp(mx, mz);
        if (hitProp != null && _multiSelectedProps.Contains(hitProp)) return true;
        var hitUnit = HitTestUnitSpawn(mx, mz);
        if (hitUnit != null && _multiSelectedUnits.Contains(hitUnit)) return true;
        return false;
    }

    private void BeginMultiDrag(float mx, float mz)
    {
        _isDraggingMulti = true;
        _multiDragStartCursor = new System.Numerics.Vector2(mx, mz);
        _multiDragStartProp.Clear();
        _multiDragStartMarker.Clear();
        _multiDragStartUnit.Clear();
        foreach (var p in _multiSelectedProps) _multiDragStartProp[p] = p.Position;
        foreach (var m in _multiSelectedMarkers) _multiDragStartMarker[m] = m.Position;
        foreach (var u in _multiSelectedUnits) _multiDragStartUnit[u] = u.Position;
    }

    private void ApplyMultiDrag(float mx, float mz)
    {
        float dx = mx - _multiDragStartCursor.X;
        float dz = mz - _multiDragStartCursor.Y;
        foreach (var (p, start) in _multiDragStartProp)
            p.Position = new System.Numerics.Vector3(start.X + dx, p.Position.Y, start.Z + dz);
        foreach (var (m, start) in _multiDragStartMarker)
            m.Position = new System.Numerics.Vector3(start.X + dx, m.Position.Y, start.Z + dz);
        foreach (var (u, start) in _multiDragStartUnit)
            u.Position = new System.Numerics.Vector3(start.X + dx, u.Position.Y, start.Z + dz);
    }

    /// <summary>Zoom-adapted hit radius (map units): scales down when icons are drawn so the hit
    /// area matches what's actually rendered. 24/zoom = half-side of the icon (48px on-screen).</summary>
    private float HitRadius()
        => _zoom >= PropIconZoomThreshold ? 24f / Math.Max(_zoom, 0.1f) : 6f;

    /// <summary>Nearest prop to a map-space point, within `maxDist` map units; null otherwise.</summary>
    private Prop? HitTestProp(float mapX, float mapY, float? maxDistOverride = null)
    {
        if (_map == null) return null;
        float maxDist = maxDistOverride ?? HitRadius();
        float best = maxDist * maxDist;
        Prop? hit = null;
        foreach (var p in _map.Props)
        {
            float dx = p.Position.X - mapX;
            float dz = p.Position.Z - mapY;
            float d2 = dx * dx + dz * dz;
            if (d2 < best) { best = d2; hit = p; }
        }
        return hit;
    }

    /// <summary>Nearest pre-placed unit (any army) within `maxDist` map units. Used for selection and hover.</summary>
    private UnitSpawn? HitTestUnitSpawn(float mapX, float mapY, float? maxDistOverride = null)
    {
        if (_map == null) return null;
        float maxDist = maxDistOverride ?? HitRadius();
        float best = maxDist * maxDist;
        UnitSpawn? hit = null;
        foreach (var army in _map.Info.Armies)
            foreach (var u in army.InitialUnits)
            {
                float dx = u.Position.X - mapX;
                float dz = u.Position.Z - mapY;
                float d2 = dx * dx + dz * dz;
                if (d2 < best) { best = d2; hit = u; }
            }
        return hit;
    }

    private void DrawMarkers(SKCanvas canvas)
    {
        if (_map == null) return;

        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var stroke = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f / Math.Max(_zoom, 0.1f),
            Color = new SKColor(20, 20, 20, 220)
        };
        using var font = new SKFont { Size = 12 };
        using var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var selectionPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(255, 255, 0),
            StrokeWidth = 2f / Math.Max(_zoom, 0.1f)
        };

        foreach (var marker in _map.Markers)
        {
            if (MarkerFilter != null && !MarkerFilter(marker)) continue;

            float x = marker.Position.X;
            float y = marker.Position.Z;

            float r;
            var bitmap = MarkerIcons?.Get(marker.Type);
            if (bitmap != null)
            {
                r = MarkerIconRadius(marker);
                var dest = new SKRect(x - r, y - r, x + r, y + r);
                canvas.DrawBitmap(bitmap, dest);
            }
            else
            {
                r = DrawMarkerIcon(canvas, marker, x, y, fill, stroke);
            }

            if (marker == _selectedMarker || _multiSelectedMarkers.Contains(marker))
                canvas.DrawCircle(x, y, r + 3f, selectionPaint);

            if (marker.Type == MarkerType.BlankMarker && marker.Name.StartsWith("ARMY_"))
                canvas.DrawText(marker.Name, x + r + 4, y + 4, SKTextAlign.Left, font, textPaint);
        }
    }

    /// <summary>Pixel half-size (in world units) used when drawing a bitmap icon for a marker type.</summary>
    private static float MarkerIconRadius(Marker m) => m.Type switch
    {
        MarkerType.BlankMarker when m.Name.StartsWith("ARMY_") => 10f,
        MarkerType.Mass or MarkerType.Hydrocarbon => 6f,
        MarkerType.ExpansionArea or MarkerType.LargeExpansionArea => 8f,
        MarkerType.NavalArea or MarkerType.DefensePoint or MarkerType.CombatZone => 7f,
        MarkerType.RallyPoint or MarkerType.NavalRallyPoint => 6f,
        MarkerType.LandPathNode or MarkerType.WaterPathNode
            or MarkerType.AirPathNode or MarkerType.AmphibiousPathNode => 4f,
        _ => 5f,
    };

    /// <summary>
    /// Draw a type-specific icon at (x, y). Shapes are filled then outlined for legibility on any
    /// terrain. Returns the bounding radius used by the selection ring and label offset.
    /// </summary>
    private static float DrawMarkerIcon(SKCanvas canvas, Marker m, float x, float y, SKPaint fill, SKPaint stroke)
    {
        switch (m.Type)
        {
            case MarkerType.Mass:
            {
                // Plain green dot — mass extractor point. Kept simple per user request.
                fill.Color = new SKColor(40, 220, 70);
                float r = 3.5f;
                canvas.DrawCircle(x, y, r, fill);
                canvas.DrawCircle(x, y, r, stroke);
                return r;
            }
            case MarkerType.Hydrocarbon:
            {
                // Cyan plus sign — hydro extractor
                fill.Color = new SKColor(60, 220, 220);
                float r = 6f;
                DrawPlus(canvas, x, y, r, fill);
                DrawPlus(canvas, x, y, r, stroke);
                return r;
            }
            case MarkerType.BlankMarker when m.Name.StartsWith("ARMY_"):
            {
                // Red 5-point star — player spawn
                fill.Color = new SKColor(255, 70, 70);
                float r = 9f;
                DrawStar(canvas, x, y, r, r * 0.45f, 5, fill);
                DrawStar(canvas, x, y, r, r * 0.45f, 5, stroke);
                return r;
            }
            case MarkerType.ExpansionArea:
            case MarkerType.LargeExpansionArea:
            {
                // Cyan circle outline — expansion zone
                fill.Color = new SKColor(0, 200, 200, 90);
                float r = m.Type == MarkerType.LargeExpansionArea ? 9f : 7f;
                canvas.DrawCircle(x, y, r, fill);
                stroke.Color = new SKColor(0, 220, 220);
                canvas.DrawCircle(x, y, r, stroke);
                stroke.Color = new SKColor(20, 20, 20, 220);
                return r;
            }
            case MarkerType.NavalArea:
            {
                // Blue anchor-like triangle pointing down
                fill.Color = new SKColor(80, 130, 255);
                float r = 7f;
                DrawTriangle(canvas, x, y, r, pointingUp: false, fill);
                DrawTriangle(canvas, x, y, r, pointingUp: false, stroke);
                return r;
            }
            case MarkerType.DefensePoint:
            {
                // Green shield (pentagon up)
                fill.Color = new SKColor(60, 200, 60);
                float r = 6f;
                DrawShield(canvas, x, y, r, fill);
                DrawShield(canvas, x, y, r, stroke);
                return r;
            }
            case MarkerType.RallyPoint:
            case MarkerType.NavalRallyPoint:
            {
                // Yellow flag (triangle on a stick)
                fill.Color = m.Type == MarkerType.NavalRallyPoint
                    ? new SKColor(120, 180, 255) : new SKColor(240, 200, 50);
                float r = 6f;
                DrawFlag(canvas, x, y, r, fill, stroke);
                return r;
            }
            case MarkerType.LandPathNode:
            case MarkerType.WaterPathNode:
            case MarkerType.AmphibiousPathNode:
            {
                // Small grey square — pathfinding node
                fill.Color = new SKColor(170, 170, 170, 200);
                float r = 2.5f;
                canvas.DrawRect(x - r, y - r, r * 2, r * 2, fill);
                return r;
            }
            case MarkerType.AirPathNode:
            {
                // Small light-blue square
                fill.Color = new SKColor(140, 200, 255, 200);
                float r = 2.5f;
                canvas.DrawRect(x - r, y - r, r * 2, r * 2, fill);
                return r;
            }
            case MarkerType.CombatZone:
            {
                fill.Color = new SKColor(255, 100, 0, 120);
                float r = 7f;
                canvas.DrawCircle(x, y, r, fill);
                stroke.Color = new SKColor(255, 140, 50);
                canvas.DrawCircle(x, y, r, stroke);
                stroke.Color = new SKColor(20, 20, 20, 220);
                return r;
            }
            case MarkerType.ProtectedExperimentalConstruction:
            {
                // Magenta triangle — matches the icon shown in the catalog/hover popup.
                fill.Color = new SKColor(255, 80, 200, 180);
                float r = 7f;
                DrawTriangle(canvas, x, y, r, pointingUp: true, fill);
                DrawTriangle(canvas, x, y, r, pointingUp: true, stroke);
                return r;
            }
            case MarkerType.CameraInfo:
            {
                // Light-blue diamond — saved camera viewpoint.
                fill.Color = new SKColor(180, 180, 220);
                float r = 6f;
                DrawDiamond(canvas, x, y, r, fill);
                DrawDiamond(canvas, x, y, r, stroke);
                return r;
            }
            case MarkerType.WeatherGenerator:
            case MarkerType.WeatherDefinition:
            {
                // Three overlapping circles → cloud silhouette.
                fill.Color = new SKColor(200, 220, 255, 180);
                stroke.Color = new SKColor(120, 140, 180);
                canvas.DrawCircle(x,        y - 1.5f, 4.5f, fill);
                canvas.DrawCircle(x + 2.5f, y + 0.5f, 4f,   fill);
                canvas.DrawCircle(x - 3f,   y + 0.5f, 3.5f, fill);
                canvas.DrawCircle(x,        y - 1.5f, 4.5f, stroke);
                canvas.DrawCircle(x + 2.5f, y + 0.5f, 4f,   stroke);
                canvas.DrawCircle(x - 3f,   y + 0.5f, 3.5f, stroke);
                stroke.Color = new SKColor(20, 20, 20, 220);
                return 5.5f;
            }
            case MarkerType.Effect:
            {
                // Yellow 4-pointed spark.
                fill.Color = new SKColor(255, 220, 80);
                float r = 6f;
                DrawStar(canvas, x, y, r, 2.5f, 4, fill);
                DrawStar(canvas, x, y, r, 2.5f, 4, stroke);
                return r;
            }
            default:
            {
                // Generic small dot — only for non-ARMY BlankMarker variants (editor stubs M_Mass/M_Blank/…).
                fill.Color = new SKColor(200, 200, 200, 180);
                float r = 2.5f;
                canvas.DrawCircle(x, y, r, fill);
                return r;
            }
        }
    }

    // --- Skia shape helpers ----------------------------------------------------------------

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
        path.MoveTo(x - t, y - r);
        path.LineTo(x + t, y - r);
        path.LineTo(x + t, y - t);
        path.LineTo(x + r, y - t);
        path.LineTo(x + r, y + t);
        path.LineTo(x + t, y + t);
        path.LineTo(x + t, y + r);
        path.LineTo(x - t, y + r);
        path.LineTo(x - t, y + t);
        path.LineTo(x - r, y + t);
        path.LineTo(x - r, y - t);
        path.LineTo(x - t, y - t);
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
        if (pointingUp)
        {
            path.MoveTo(x, y - r);
            path.LineTo(x + r, y + r * 0.8f);
            path.LineTo(x - r, y + r * 0.8f);
        }
        else
        {
            path.MoveTo(x, y + r);
            path.LineTo(x + r, y - r * 0.8f);
            path.LineTo(x - r, y - r * 0.8f);
        }
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
        // Pole
        using (var pole = new SKPath())
        {
            pole.MoveTo(x - r * 0.5f, y + r);
            pole.LineTo(x - r * 0.5f, y - r);
            c.DrawPath(pole, stroke);
        }
        // Flag triangle
        using var path = new SKPath();
        path.MoveTo(x - r * 0.5f, y - r);
        path.LineTo(x + r, y - r * 0.4f);
        path.LineTo(x - r * 0.5f, y + r * 0.2f);
        path.Close();
        c.DrawPath(path, fill);
        c.DrawPath(path, stroke);
    }

    private void DrawGrid(SKCanvas canvas)
    {
        if (_map == null) return;

        int mapW = _map.Heightmap.Width;
        int mapH = _map.Heightmap.Height;

        // Map border is always drawn so the playable area is visible
        using var borderPaint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 80),
            StrokeWidth = 2f / _zoom,
            Style = SKPaintStyle.Stroke
        };
        canvas.DrawRect(0, 0, mapW, mapH, borderPaint);

        if (!ShowGrid && !ShowDiagonalGrid) return;
        if (_zoom < 0.1f) return;

        float step = Math.Max(1, GridStep);

        if (ShowGrid)
        {
            using var gridPaint = new SKPaint
            {
                Color = new SKColor(255, 255, 255, 35),
                StrokeWidth = 1f / _zoom,
                Style = SKPaintStyle.Stroke,
                IsAntialias = false
            };
            for (float x = 0; x <= mapW; x += step)
                canvas.DrawLine(x, 0, x, mapH, gridPaint);
            for (float y = 0; y <= mapH; y += step)
                canvas.DrawLine(0, y, mapW, y, gridPaint);
        }

        if (ShowDiagonalGrid)
        {
            using var diagPaint = new SKPaint
            {
                Color = new SKColor(120, 200, 255, 30),
                StrokeWidth = 1f / _zoom,
                Style = SKPaintStyle.Stroke,
                IsAntialias = false
            };
            // Lines at 45°: y = x + c (slope 1) and y = -x + c (slope -1).
            // Spacing along the diagonal axis equals step * sqrt(2), so for visual density
            // matching the orthogonal grid we step the intercept by `step` along each axis.
            float maxExtent = mapW + mapH;
            // y = x + c, c from -mapW to mapH
            for (float c = -mapW; c <= mapH; c += step)
            {
                float x0 = Math.Max(0, -c);
                float y0 = x0 + c;
                float x1 = Math.Min(mapW, mapH - c);
                float y1 = x1 + c;
                if (x1 > x0) canvas.DrawLine(x0, y0, x1, y1, diagPaint);
            }
            // y = -x + c, c from 0 to mapW+mapH
            for (float c = 0; c <= maxExtent; c += step)
            {
                float x0 = Math.Max(0, c - mapH);
                float y0 = c - x0;
                float x1 = Math.Min(mapW, c);
                float y1 = c - x1;
                if (x1 > x0) canvas.DrawLine(x0, y0, x1, y1, diagPaint);
            }
        }
    }

    private (float x, float y) Snap(float mapX, float mapY)
    {
        if (!SnapToGrid || GridStep < 1) return (mapX, mapY);
        float s = GridStep;

        // Orthogonal grid intersections: integer multiples of step.
        float ox = MathF.Round(mapX / s) * s;
        float oy = MathF.Round(mapY / s) * s;

        if (!ShowDiagonalGrid) return (ox, oy);

        // Diagonals cross each other at cell centers (offset by step/2 on both axes); offer those
        // as additional snap candidates when the diagonal overlay is on. Pick whichever is closer.
        float dx = MathF.Round((mapX - s / 2f) / s) * s + s / 2f;
        float dy = MathF.Round((mapY - s / 2f) / s) * s + s / 2f;

        float oDist2 = (ox - mapX) * (ox - mapX) + (oy - mapY) * (oy - mapY);
        float dDist2 = (dx - mapX) * (dx - mapX) + (dy - mapY) * (dy - mapY);
        return dDist2 < oDist2 ? (dx, dy) : (ox, oy);
    }

    private void DrawHud(SKCanvas canvas, int viewW, int viewH)
    {
        if (_map == null) return;

        using var bgPaint = new SKPaint { Color = new SKColor(0, 0, 0, 150) };
        using var font = new SKFont { Size = 12 };
        using var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };

        string info = $"{_map.Heightmap.Width}x{_map.Heightmap.Height}  Zoom: {_zoom:F2}x  Markers: {_map.Markers.Count}";
        float textW = font.MeasureText(info);
        canvas.DrawRect(4, viewH - 22, textW + 12, 20, bgPaint);
        canvas.DrawText(info, 10, viewH - 7, SKTextAlign.Left, font, textPaint);
    }

    public Marker? SelectedMarker
    {
        get => _selectedMarker;
        set
        {
            if (_selectedMarker == value) return;
            _selectedMarker = value;
            if (value != null) _selectedProp = null;  // marker and prop selection are exclusive
            MarkerSelected?.Invoke(value);
            RedrawBitmap();
        }
    }

    public Prop? SelectedProp
    {
        get => _selectedProp;
        set
        {
            if (_selectedProp == value) return;
            _selectedProp = value;
            if (value != null) _selectedMarker = null;
            PropSelected?.Invoke(value);
            RedrawBitmap();
        }
    }

    public event Action<Prop?>? PropSelected;

    /// <summary>Refresh markers overlay (after add/delete/move).</summary>
    public void RefreshMarkers() => RedrawBitmap();

    private Marker? HitTestMarker(float mapX, float mapY)
    {
        if (_map == null) return null;
        Marker? best = null;
        float bestDist = float.MaxValue;
        foreach (var m in _map.Markers)
        {
            if (MarkerFilter != null && !MarkerFilter(m)) continue;

            float dx = m.Position.X - mapX;
            float dz = m.Position.Z - mapY;
            float dist = dx * dx + dz * dz;
            float hitRadius = m.Type switch
            {
                MarkerType.BlankMarker when m.Name.StartsWith("ARMY_") => 12f,
                MarkerType.Hydrocarbon => 6f,
                MarkerType.Mass => 5f,
                _ => 6f
            };
            if (dist < hitRadius * hitRadius && dist < bestDist)
            {
                best = m;
                bestDist = dist;
            }
        }
        return best;
    }

    private (float mapX, float mapY) ScreenToMap(Point screen)
    {
        float mapX = ((float)screen.X - _panX) / _zoom;
        float mapY = ((float)screen.Y - _panY) / _zoom;
        return (mapX, mapY);
    }

    // === Mouse input ===

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        e.Pointer.Capture(this);
        _lastMouse = e.GetPosition(this);

        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsMiddleButtonPressed || props.IsRightButtonPressed)
        {
            _isPanning = true;
        }
        else if (props.IsLeftButtonPressed)
        {
            _isDragging = false;
            _isDraggingMarker = false;
            _isDraggingProp = false;
            _isDraggingUnit = false;
            _isDraggingMulti = false;
            _isBoxSelecting = false;

            // Prop brush takes priority over selection/box: a left click in brush mode starts a stroke.
            if (IsPropBrushActive)
            {
                var (mx, my) = ScreenToMap(_lastMouse);
                PropBrushStart?.Invoke(mx, my);
                _isDragging = true; // suppress drag-to-pan
                e.Handled = true;
                return;
            }

            // Single-prop placement: a prop is picked in the bottom icon menu and the brush is off.
            // BUT: selection takes priority. If the click lands on an existing marker/prop/unit,
            // select it instead of placing on top. Hold Ctrl to force placement over a target.
            if (IsSinglePlaceActive && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                var (mx, my) = ScreenToMap(_lastMouse);
                bool overExisting =
                    HitTestMarker(mx, my) != null ||
                    HitTestProp(mx, my) != null ||
                    HitTestUnitSpawn(mx, my) != null;
                if (!overExisting || e.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    SinglePlaceProp?.Invoke(mx, my);
                    _isDragging = true;
                    e.Handled = true;
                    return;
                }
                // Fall through to the standard selection logic below.
            }

            // Shift+left-drag triggers a box selection (no group/single drag in this mode).
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                _isBoxSelecting = true;
                _boxStartScreen = _lastMouse;
                _boxEndScreen = _lastMouse;
            }
            // Group drag: if there's a multi-selection and the click lands on one of its members,
            // start a group drag instead of single-element drag/selection.
            else if (HasMultiSelection)
            {
                var (mx, my) = ScreenToMap(_lastMouse);
                if (HitsMultiSelection(mx, my))
                    BeginMultiDrag(mx, my);
            }
            else if (_selectedMarker != null && !IsPlacingMarker)
            {
                var (mx, my) = ScreenToMap(_lastMouse);
                var hit = HitTestMarker(mx, my);
                if (hit == _selectedMarker)
                {
                    _isDraggingMarker = true;
                    _markerDragStartPos = _selectedMarker.Position;
                }
            }
            else if (_selectedProp != null)
            {
                // Click on a selected prop starts a drag — same UX as markers. Multi-selection
                // (Shift+drag) was handled above; we only support dragging the single primary selection.
                var (mx, my) = ScreenToMap(_lastMouse);
                var hit = HitTestProp(mx, my);
                if (hit == _selectedProp)
                {
                    _isDraggingProp = true;
                    _propDragStartPos = _selectedProp.Position;
                }
            }
            else if (_selectedUnitSpawn != null)
            {
                var (mx, my) = ScreenToMap(_lastMouse);
                var hit = HitTestUnitSpawn(mx, my);
                if (hit == _selectedUnitSpawn)
                {
                    _isDraggingUnit = true;
                    _unitDragStartPos = _selectedUnitSpawn.Position;
                }
            }
        }

        e.Handled = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        // Hide the brush circle when the cursor leaves the 2D view.
        if (CursorWorldPos != null)
        {
            CursorWorldPos = null;
            if (IsPropBrushActive) RedrawBitmap();
        }
        ClearHover();
    }

    private void ClearHover()
    {
        if (HoveredProp == null) return;
        HoveredProp = null;
        HoveredPropIcon = null;
        HoveredPropName = null;
        HasHoveredProp = false;
    }

    private void UpdateHoverFromCursor(float mapX, float mapZ)
    {
        // Hit-test radius widens slightly when zoomed-in icons are visible, so the popup tracks
        // whatever the user sees on screen. 28 vs 24 = ~17% padding around the visible icon.
        float maxDist = _zoom >= PropIconZoomThreshold ? 28f / Math.Max(_zoom, 0.1f) : 6f;

        // Same precedence as click selection: marker → prop → unit. Markers are smallest and
        // usually drawn on top of props, so the popup should follow that.
        var marker = HitTestMarker(mapX, mapZ);
        if (marker != null)
        {
            string label = marker.Type == MarkerType.BlankMarker && marker.Name.StartsWith("ARMY_")
                ? marker.Name
                : $"{marker.Name} ({marker.Type})";
            if (HoveredProp == null && HasHoveredProp && HoveredPropName == label) return;
            HoveredProp = null;
            HoveredPropIcon = Services.MarkerCatalog.GetIcon(marker.Type);
            HoveredPropName = label;
            HasHoveredProp = true;
            return;
        }

        var prop = HitTestProp(mapX, mapZ, maxDist);
        if (prop != null)
        {
            if (prop == HoveredProp) return;
            HoveredProp = prop;
            var entry = LookupCatalog(prop.BlueprintPath);
            HoveredPropIcon = entry?.Icon;
            HoveredPropName = entry?.DisplayName ?? System.IO.Path.GetFileNameWithoutExtension(prop.BlueprintPath);
            HasHoveredProp = true;
            return;
        }

        var unit = HitTestUnitSpawn(mapX, mapZ);
        if (unit != null)
        {
            if (HoveredProp == null && HasHoveredProp &&
                string.Equals(HoveredPropName, unit.BlueprintId, StringComparison.OrdinalIgnoreCase))
                return; // same unit, no churn
            HoveredProp = null;
            var entry = LookupCatalogByUnitId(unit.BlueprintId);
            HoveredPropIcon = entry?.Icon;
            HoveredPropName = unit.BlueprintId;
            HasHoveredProp = true;
            return;
        }

        if (HoveredProp == null && !HasHoveredProp) return;
        HoveredProp = null;
        HoveredPropIcon = null;
        HoveredPropName = null;
        HasHoveredProp = false;
    }

    private static Rendering.PropEntry? LookupCatalog(string blueprintPath)
    {
        foreach (var cat in Rendering.PropCatalog.All)
            foreach (var en in cat.Items)
                if (string.Equals(en.BlueprintPath, blueprintPath, StringComparison.OrdinalIgnoreCase))
                    return en;
        return null;
    }

    private static Rendering.PropEntry? LookupCatalogByUnitId(string unitId)
    {
        // Unit catalog entries: BlueprintPath = "/units/<ID>/<ID>_unit.bp", DisplayName = <ID> upper.
        foreach (var cat in Rendering.PropCatalog.All)
            foreach (var en in cat.Items)
                if (en.IsUnit && string.Equals(en.DisplayName, unitId, StringComparison.OrdinalIgnoreCase))
                    return en;
        return null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        // Track cursor in map coords so the window can paste at the current mouse position.
        var (cmx, cmy) = ScreenToMap(pos);
        CursorWorldPos = new System.Numerics.Vector2(cmx, cmy);
        // When the prop brush is enabled, the on-canvas cursor circle should follow the mouse even
        // before any click — otherwise the user can't see where the brush will land.
        if (IsPropBrushActive) RedrawBitmap();

        // Hover detection: only when idle (not while panning/dragging/box-selecting/brushing).
        var pProps = e.GetCurrentPoint(this).Properties;
        if (!_isPanning && !_isDragging && !_isBoxSelecting && !pProps.IsLeftButtonPressed)
            UpdateHoverFromCursor(cmx, cmy);
        else
            ClearHover();

        if (_isPanning)
        {
            _panX += (float)(pos.X - _lastMouse.X);
            _panY += (float)(pos.Y - _lastMouse.Y);
            _lastMouse = pos;
            RedrawBitmap();
            e.Handled = true;
            return;
        }

        // Detect drag threshold for left button
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsLeftButtonPressed && !_isDragging)
        {
            double dx = pos.X - _lastMouse.X;
            double dy = pos.Y - _lastMouse.Y;
            if (dx * dx + dy * dy > 9) // 3px threshold
                _isDragging = true;
        }

        // While brushing, stream stamps to the VM at every move.
        if (IsPropBrushActive && props.IsLeftButtonPressed)
        {
            PropBrushDrag?.Invoke(cmx, cmy);
            RedrawBitmap();
            e.Handled = true;
            return;
        }

        if (_isDragging && props.IsLeftButtonPressed)
        {
            if (_isBoxSelecting)
            {
                _boxEndScreen = pos;
                RedrawBitmap();
            }
            else if (_isDraggingMulti)
            {
                var (mapX, mapY) = ScreenToMap(pos);
                ApplyMultiDrag(mapX, mapY);
                RedrawBitmap();
            }
            else if (_isDraggingMarker && _selectedMarker != null)
            {
                // Move the selected marker
                var (mapX, mapY) = ScreenToMap(pos);
                (mapX, mapY) = Snap(mapX, mapY);
                _selectedMarker.Position = new System.Numerics.Vector3(mapX, _selectedMarker.Position.Y, mapY);
                RedrawBitmap();
            }
            else if (_isDraggingProp && _selectedProp != null)
            {
                // Move the selected prop. Y stays at its current value during the drag; the host
                // re-clamps it to terrain on release (RecordPropMove).
                var (mapX, mapY) = ScreenToMap(pos);
                (mapX, mapY) = Snap(mapX, mapY);
                _selectedProp.Position = new System.Numerics.Vector3(mapX, _selectedProp.Position.Y, mapY);
                RedrawBitmap();
            }
            else if (_isDraggingUnit && _selectedUnitSpawn != null)
            {
                var (mapX, mapY) = ScreenToMap(pos);
                (mapX, mapY) = Snap(mapX, mapY);
                _selectedUnitSpawn.Position = new System.Numerics.Vector3(mapX, _selectedUnitSpawn.Position.Y, mapY);
                RedrawBitmap();
            }
            else
            {
                // Pan the view
                _panX += (float)(pos.X - _lastMouse.X);
                _panY += (float)(pos.Y - _lastMouse.Y);
                _lastMouse = pos;
                RedrawBitmap();
            }
        }

        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);

        if (IsPropBrushActive)
        {
            PropBrushEnd?.Invoke();
            RedrawBitmap();
        }
        else if (_isBoxSelecting)
        {
            // Finalize box selection: collect every prop whose screen position falls within the rect.
            FinalizeBoxSelection();
            _isBoxSelecting = false;
            RedrawBitmap();
        }
        else if (_isDraggingMulti && _isDragging)
        {
            // Group drag ended — hand the snapshot start positions back so the host can push a
            // single batch op and clamp final Y.
            MultiSelectionMoved?.Invoke(_multiDragStartProp, _multiDragStartMarker, _multiDragStartUnit);
        }
        else if (_isDraggingMarker && _isDragging && _selectedMarker != null)
        {
            // Marker drag ended — notify ViewModel with the original position so it can record undo
            MarkerMoved?.Invoke(_selectedMarker, _markerDragStartPos);
        }
        else if (_isDraggingProp && _isDragging && _selectedProp != null)
        {
            PropMoved?.Invoke(_selectedProp, _propDragStartPos);
        }
        else if (_isDraggingUnit && _isDragging && _selectedUnitSpawn != null)
        {
            UnitSpawnMoved?.Invoke(_selectedUnitSpawn, _unitDragStartPos);
        }
        else if (!_isPanning && !_isDragging && !_isDraggingMulti)
        {
            // Left click without drag. Excluding _isDraggingMulti preserves the multi-selection
            // when the user taps (no move) on one of its members instead of collapsing to single-select.
            var (mapX, mapY) = ScreenToMap(e.GetPosition(this));
            if (IsPlacingMarker)
            {
                (mapX, mapY) = Snap(mapX, mapY);
                MarkerPlaceRequested?.Invoke(mapX, mapY);
            }
            else
            {
                // Prefer markers (smallest hit radius), then props, then pre-placed units.
                var marker = HitTestMarker(mapX, mapY);
                if (marker != null)
                {
                    SelectedMarker = marker;
                    SelectedUnitSpawn = null;
                    UnitSpawnSelected?.Invoke(null);
                }
                else
                {
                    var prop = HitTestProp(mapX, mapY);
                    if (prop != null)
                    {
                        SelectedProp = prop;
                        SelectedUnitSpawn = null;
                        UnitSpawnSelected?.Invoke(null);
                    }
                    else
                    {
                        var unit = HitTestUnitSpawn(mapX, mapY);
                        if (unit != null)
                        {
                            SelectedUnitSpawn = unit;
                            SelectedMarker = null;
                            SelectedProp = null;
                            UnitSpawnSelected?.Invoke(unit);
                        }
                        else
                        {
                            SelectedMarker = null;
                            SelectedProp = null;
                            SelectedUnitSpawn = null;
                            UnitSpawnSelected?.Invoke(null);
                        }
                    }
                }
            }
        }

        _isPanning = false;
        _isDragging = false;
        _isDraggingMarker = false;
        _isDraggingProp = false;
        _isDraggingUnit = false;
        _isDraggingMulti = false;
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        float mouseX = (float)e.GetPosition(this).X;
        float mouseY = (float)e.GetPosition(this).Y;

        float oldZoom = _zoom;
        _zoom *= (1f + (float)e.Delta.Y * 0.15f);
        _zoom = Math.Clamp(_zoom, 0.05f, 20f);

        float ratio = _zoom / oldZoom;
        _panX = mouseX - ratio * (mouseX - _panX);
        _panY = mouseY - ratio * (mouseY - _panY);

        RedrawBitmap();
        e.Handled = true;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_map != null && _heightmapBitmap != null)
            FitToMap();
        RedrawBitmap();
    }
}
