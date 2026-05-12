using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using SupremeCommanderEditor.Core.Models;
using SupremeCommanderEditor.Core.Services;
using SupremeCommanderEditor.Rendering;
using static Avalonia.OpenGL.GlConsts;

namespace SupremeCommanderEditor.App.Controls;

/// <summary>
/// Inner GL control that does the actual rendering.
/// </summary>
public class GlTerrainControl : OpenGlControlBase
{
    public TerrainRenderer? Terrain { get; private set; }
    public WaterRenderer? Water { get; private set; }
    public MarkerRenderer? Markers { get; private set; }
    public Camera Camera { get; } = new();
    public bool IsGlInitialized { get; private set; }

    /// <summary>Set by the wrapper to show the brush cursor on terrain.</summary>
    public System.Numerics.Vector2? BrushPos { get; set; }
    public float BrushRadius { get; set; }

    /// <summary>Game data service for loading textures from SCD archives.</summary>
    public GameDataService? GameData { get; set; }

    private ScMap? _map;
    private bool _meshDirty;
    private bool _texturesDirty;
    private bool _markersDirty;
    private bool _recenterCamera;
    private float _lastWaterElev = float.NaN;

    private bool _snapshotPending;
    private int _snapshotMaxSize = 1024;

    /// <summary>Fired after a top-down snapshot is captured. Arguments: rgba bytes (top-left origin), width, height.</summary>
    public event Action<byte[], int, int>? TopDownReady;

    /// <summary>Request an orthographic top-down capture on the next render frame.</summary>
    public void RequestTopDownSnapshot(int maxSize = 1024)
    {
        _snapshotMaxSize = Math.Max(64, maxSize);
        _snapshotPending = true;
        RequestNextFrameRendering();
    }

    /// <summary>Selected marker, for highlight in 3D view.</summary>
    public Core.Models.Marker? SelectedMarker { get; set; }

    /// <summary>Filter for marker visibility.</summary>
    public Func<Marker, bool>? MarkerFilter { get; set; }

    public void SetMap(ScMap? map)
    {
        _map = map;
        _meshDirty = true;
        _texturesDirty = true;
        _markersDirty = true;
        _recenterCamera = true; // re-frame the view only when the user loads/changes the map
        _lastWaterElev = float.NaN;
        RequestNextFrameRendering();
    }

    /// <summary>Force a camera re-fit on the next render — used after operations that change the
    /// map dimensions (Scale) so the user doesn't have to manually search for the resized terrain.</summary>
    public void RecenterOnNextFrame()
    {
        _recenterCamera = true;
        RequestNextFrameRendering();
    }

    public void MarkMeshDirty()
    {
        _meshDirty = true;
        // Force the water quad to rebuild on the next render: its size is baked from the heightmap
        // dimensions and would otherwise keep the pre-scale geometry, leaving a giant blue plane
        // around the resized terrain.
        _lastWaterElev = float.NaN;
        // Same pattern as SetMap: request a frame so the render loop wakes even if the control
        // was invisible at invalidation time (e.g. symmetry applied from the Symétrie tab).
        RequestNextFrameRendering();
    }

    public void MarkMarkersDirty()
    {
        _markersDirty = true;
        RequestNextFrameRendering();
    }

    public void MarkTexturesDirty()
    {
        _texturesDirty = true;
        RequestNextFrameRendering();
    }

    /// <summary>Public wake-up: force an OnOpenGlRender pass on the next frame. Used when the
    /// control becomes visible again after being hidden — Avalonia may not auto-trigger the loop.</summary>
    public void RequestRender() => RequestNextFrameRendering();

    private bool _isGlEs;

    protected override void OnOpenGlInit(GlInterface gl)
    {
        _isGlEs = DetectGlEs(gl);
        LogGlInfo(gl);

        try
        {
            Terrain = new TerrainRenderer(gl);
            Terrain.Initialize(PrepareShader("terrain.vert", isFragment: false), PrepareShader("terrain.frag", isFragment: true));
            SupremeCommanderEditor.Core.Services.DebugLog.Write("[GL] Terrain shader compiled+linked OK");

            Water = new WaterRenderer(gl);
            Water.Initialize(PrepareShader("water.vert", isFragment: false), PrepareShader("water.frag", isFragment: true));
            SupremeCommanderEditor.Core.Services.DebugLog.Write("[GL] Water shader compiled+linked OK");

            Markers = new MarkerRenderer(gl);
            Markers.Initialize(PrepareShader("marker.vert", isFragment: false), PrepareShader("marker.frag", isFragment: true));
            SupremeCommanderEditor.Core.Services.DebugLog.Write("[GL] Marker shader compiled+linked OK");
        }
        catch (Exception ex)
        {
            SupremeCommanderEditor.Core.Services.DebugLog.Write($"[GL] Shader init FAILED: {ex.Message}");
            throw;
        }

        IsGlInitialized = true;
        _meshDirty = true;
        _texturesDirty = true;
        _markersDirty = true;
        _lastWaterElev = float.NaN;
    }

    /// <summary>
    /// Shader source files contain no #version line; we prepend the right one at compile time.
    /// On Avalonia/Windows the context is OpenGL ES 3.0 via ANGLE (GLSL ES 300 + precision quals);
    /// on Linux/macOS we get desktop GL 3.3 (GLSL 330 core). The shader body is identical for both.
    /// </summary>
    private string PrepareShader(string fileName, bool isFragment)
    {
        string body = LoadShader(fileName);
        string header = _isGlEs
            ? "#version 300 es\n" + (isFragment ? "precision highp float;\nprecision highp sampler2D;\n" : "")
            : "#version 330 core\n";
        return header + body;
    }

    private static bool DetectGlEs(GlInterface gl)
    {
        IntPtr getStringPtr = gl.GetProcAddress("glGetString");
        if (getStringPtr == IntPtr.Zero) return false;
        const int GL_VERSION = 0x1F02;
        var getStr = System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer<GlGetStringDelegate>(getStringPtr);
        var p = getStr(GL_VERSION);
        if (p == IntPtr.Zero) return false;
        var s = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(p) ?? "";
        return s.Contains("OpenGL ES", StringComparison.OrdinalIgnoreCase);
    }

    private static void LogGlInfo(GlInterface gl)
    {
        // Probe GL_VERSION / GL_RENDERER / GL_VENDOR / GL_SHADING_LANGUAGE_VERSION so we know
        // whether ANGLE or native desktop GL is in use (Windows-only surprises happen with ANGLE).
        const int GL_VENDOR = 0x1F00;
        const int GL_RENDERER = 0x1F01;
        const int GL_VERSION = 0x1F02;
        const int GL_SHADING_LANGUAGE_VERSION = 0x8B8C;
        IntPtr getStringPtr = gl.GetProcAddress("glGetString");
        if (getStringPtr == IntPtr.Zero) { SupremeCommanderEditor.Core.Services.DebugLog.Write("[GL] glGetString not available"); return; }
        var getStr = System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer<GlGetStringDelegate>(getStringPtr);
        string Read(int e)
        {
            try
            {
                var p = getStr(e);
                return p == IntPtr.Zero ? "(null)" : System.Runtime.InteropServices.Marshal.PtrToStringAnsi(p) ?? "";
            }
            catch (Exception ex) { return $"(error {ex.Message})"; }
        }
        SupremeCommanderEditor.Core.Services.DebugLog.Write($"[GL] VENDOR    = {Read(GL_VENDOR)}");
        SupremeCommanderEditor.Core.Services.DebugLog.Write($"[GL] RENDERER  = {Read(GL_RENDERER)}");
        SupremeCommanderEditor.Core.Services.DebugLog.Write($"[GL] VERSION   = {Read(GL_VERSION)}");
        SupremeCommanderEditor.Core.Services.DebugLog.Write($"[GL] GLSL      = {Read(GL_SHADING_LANGUAGE_VERSION)}");
    }

    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
    private delegate IntPtr GlGetStringDelegate(int name);

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        Terrain?.Dispose();
        Terrain = null;
        Water?.Dispose();
        Water = null;
        Markers?.Dispose();
        Markers = null;
        IsGlInitialized = false;
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        // Get pixel dimensions accounting for HiDPI scaling
        double scaling = GetScaling();
        int w = Math.Max(1, (int)(Bounds.Width * scaling));
        int h = Math.Max(1, (int)(Bounds.Height * scaling));

        gl.Viewport(0, 0, w, h);
        gl.ClearColor(0.08f, 0.08f, 0.14f, 1f);
        gl.Clear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);
        gl.Enable(GL_DEPTH_TEST);

        if (IsGlInitialized && Terrain != null && _map != null)
        {
            if (_meshDirty)
            {
                int step = _map.Heightmap.Width > 512 ? 4 : (_map.Heightmap.Width > 256 ? 2 : 1);
                Terrain.BuildMesh(_map.Heightmap, step);
                if (_recenterCamera)
                {
                    Camera.FitToMap(_map.Heightmap.Width, _map.Heightmap.Height);
                    _recenterCamera = false;
                }
                _meshDirty = false;
            }

            if (_texturesDirty)
            {
                Terrain.LoadTextures(_map, GameData);
                _texturesDirty = false;
                SupremeCommanderEditor.Core.Services.DebugLog.Write($"[TEX] Loaded: count={_map.TerrainTextures.Length}, gameData={GameData?.IsInitialized}, texLoaded={Terrain.TexturesLoaded}");
            }

            float aspect = (float)w / h;
            Terrain.Render(Camera, aspect, _map.Lighting, BrushPos, BrushRadius);

            // Water plane
            if (Water != null && _map.Water.HasWater)
            {
                float elev = _map.Water.Elevation;
                if (elev != _lastWaterElev)
                {
                    Water.BuildQuad(_map.Heightmap.Width, _map.Heightmap.Height, elev);
                    _lastWaterElev = elev;
                }
                Water.Render(Camera, aspect, _map.Water);
            }

            // Markers
            if (Markers != null && _map.Markers.Count > 0)
            {
                if (_markersDirty)
                {
                    Markers.UpdateMarkers(_map.Markers, SelectedMarker, _map.Heightmap, MarkerFilter);
                    _markersDirty = false;
                }
                Markers.Render(Camera, aspect);
            }

            if (_snapshotPending)
            {
                _snapshotPending = false;
                CaptureTopDownSnapshot(gl, fb, w, h);
            }
        }

        RequestNextFrameRendering();
    }

    /// <summary>
    /// Render terrain + water to an off-screen FBO with an orthographic top-down camera,
    /// read pixels, and fire TopDownReady. The original framebuffer and viewport are restored.
    /// Markers and brush cursor are intentionally excluded — they're drawn by the 2D control on top.
    /// </summary>
    private void CaptureTopDownSnapshot(GlInterface gl, int prevFb, int prevW, int prevH)
    {
        if (_map == null || Terrain == null) return;

        int mapW = _map.Heightmap.Width;
        int mapH = _map.Heightmap.Height;

        // Pick capture size: longest side = _snapshotMaxSize, preserve map aspect
        int capW, capH;
        if (mapW >= mapH)
        {
            capW = _snapshotMaxSize;
            capH = Math.Max(1, _snapshotMaxSize * mapH / mapW);
        }
        else
        {
            capH = _snapshotMaxSize;
            capW = Math.Max(1, _snapshotMaxSize * mapW / mapH);
        }

        int fbo = 0, colorRb = 0, depthRb = 0;
        try
        {
            fbo = GlFbo.GenFramebuffer(gl);
            colorRb = GlFbo.GenRenderbuffer(gl);
            depthRb = GlFbo.GenRenderbuffer(gl);

            GlFbo.BindFramebuffer(gl, fbo);

            GlFbo.BindRenderbuffer(gl, colorRb);
            GlFbo.RenderbufferStorage(gl, GlExtra.GL_RGBA8, capW, capH);
            GlFbo.FramebufferRenderbuffer(gl, GlExtra.GL_COLOR_ATTACHMENT0, colorRb);

            GlFbo.BindRenderbuffer(gl, depthRb);
            GlFbo.RenderbufferStorage(gl, GlExtra.GL_DEPTH_COMPONENT24, capW, capH);
            GlFbo.FramebufferRenderbuffer(gl, GlExtra.GL_DEPTH_ATTACHMENT, depthRb);

            int status = GlFbo.CheckFramebufferStatus(gl);
            if (status != GlExtra.GL_FRAMEBUFFER_COMPLETE)
            {
                SupremeCommanderEditor.Core.Services.DebugLog.Write($"[Snapshot] FBO incomplete: 0x{status:X}");
                return;
            }

            gl.Viewport(0, 0, capW, capH);
            gl.ClearColor(0f, 0f, 0f, 1f);
            gl.Clear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);
            gl.Enable(GL_DEPTH_TEST);

            // Ortho top-down camera that exactly frames the map.
            // Cap at 1024 world units of headroom: SupCom heightmap max is 65535/128 ≈ 512.
            var orthoCam = Camera.CreateTopDown(mapW, mapH, maxHeight: 1024f);
            float aspect = (float)capW / capH;

            Terrain.Render(orthoCam, aspect, _map.Lighting, brushPos: null, brushRadius: 0f);

            if (Water != null && _map.Water.HasWater)
                Water.Render(orthoCam, aspect, _map.Water);

            // Read back pixels (RGBA, bottom-left origin)
            int byteCount = capW * capH * 4;
            byte[] raw = new byte[byteCount];
            GlFbo.ReadPixels(gl, capW, capH, raw);

            // Flip vertically: glReadPixels origin is bottom-left, image consumers expect top-left
            byte[] flipped = new byte[byteCount];
            int stride = capW * 4;
            for (int y = 0; y < capH; y++)
                Buffer.BlockCopy(raw, y * stride, flipped, (capH - 1 - y) * stride, stride);

            TopDownReady?.Invoke(flipped, capW, capH);
        }
        catch (Exception ex)
        {
            SupremeCommanderEditor.Core.Services.DebugLog.Write($"[Snapshot] failed: {ex.Message}");
        }
        finally
        {
            // Restore default framebuffer and viewport for subsequent rendering
            GlFbo.BindFramebuffer(gl, prevFb);
            gl.Viewport(0, 0, prevW, prevH);
            GlFbo.DeleteRenderbuffer(gl, colorRb);
            GlFbo.DeleteRenderbuffer(gl, depthRb);
            GlFbo.DeleteFramebuffer(gl, fbo);
        }
    }

    private double GetScaling()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        return topLevel?.RenderScaling ?? 1.0;
    }

    private static string LoadShader(string fileName)
    {
        // Shaders are embedded resources in the Rendering assembly (logical name "Shaders/<file>").
        var asm = typeof(TerrainRenderer).Assembly;
        string resourceName = "Shaders/" + fileName;
        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded shader not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>
/// Wrapper panel that handles mouse input and contains the GL control.
/// Left-click = brush paint, Right-click = orbit, Middle = pan, Wheel = zoom.
/// </summary>
public class OpenGlMapControl : Panel
{
    private readonly GlTerrainControl _glControl;
    private Point _lastMouse;
    private bool _isDragging;
    private int _dragButton; // 1=pan, 2=orbit, 3=brush

    public static readonly StyledProperty<ScMap?> MapProperty =
        AvaloniaProperty.Register<OpenGlMapControl, ScMap?>(nameof(Map));

    public ScMap? Map
    {
        get => GetValue(MapProperty);
        set => SetValue(MapProperty, value);
    }

    /// <summary>ViewModel reference for brush tool access.</summary>
    public ViewModels.MainWindowViewModel? ViewModel { get; set; }

    public void SetGameData(GameDataService gameData)
    {
        _glControl.GameData = gameData;
    }

    /// <summary>Fires when the GL control captures a top-down snapshot (rgba bytes, w, h).</summary>
    public event Action<byte[], int, int>? TopDownReady
    {
        add => _glControl.TopDownReady += value;
        remove => _glControl.TopDownReady -= value;
    }

    /// <summary>Request a top-down snapshot to be rendered on the next 3D frame.</summary>
    public void RequestTopDownSnapshot(int maxSize = 1024) => _glControl.RequestTopDownSnapshot(maxSize);

    /// <summary>Wake the render loop — call after IsVisible becomes true to flush pending dirty flags.</summary>
    public void RequestRender() => _glControl.RequestRender();

    private RotateTransform? _compassRotation;

    public OpenGlMapControl()
    {
        Background = Avalonia.Media.Brushes.Transparent;
        Focusable = true;
        IsHitTestVisible = true;

        _glControl = new GlTerrainControl();
        Children.Add(_glControl);

        Children.Add(BuildCompass());
        UpdateCompass();
    }

    /// <summary>
    /// Small compass widget pinned to the top-right corner of the viewport. The arrow + "N" rotate
    /// so they always point toward world -Z (north). Doesn't intercept input → won't bother edits.
    /// </summary>
    private Avalonia.Controls.Control BuildCompass()
    {
        const double size = 56;
        _compassRotation = new RotateTransform { Angle = 0 };

        var background = new Avalonia.Controls.Shapes.Ellipse
        {
            Width = size,
            Height = size,
            Fill = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(140, 0, 0, 0)),
            Stroke = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(180, 255, 255, 255)),
            StrokeThickness = 1,
            IsHitTestVisible = false,
        };

        var arrow = new Avalonia.Controls.Shapes.Polygon
        {
            Points = new Avalonia.Collections.AvaloniaList<Avalonia.Point>
            {
                new(size / 2,        4),               // tip (N)
                new(size / 2 + 6,    size / 2),        // right
                new(size / 2,        size / 2 + 3),    // center
                new(size / 2 - 6,    size / 2),        // left
            },
            Fill = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(230, 70, 70)),
            Stroke = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White),
            StrokeThickness = 1,
            IsHitTestVisible = false,
        };

        var southTail = new Avalonia.Controls.Shapes.Polygon
        {
            Points = new Avalonia.Collections.AvaloniaList<Avalonia.Point>
            {
                new(size / 2,        size - 4),
                new(size / 2 + 6,    size / 2),
                new(size / 2,        size / 2 - 3),
                new(size / 2 - 6,    size / 2),
            },
            Fill = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(180, 220, 220, 220)),
            Stroke = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(200, 30, 30, 30)),
            StrokeThickness = 1,
            IsHitTestVisible = false,
        };

        var n = new TextBlock
        {
            Text = "N",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            FontSize = 12,
            Foreground = Avalonia.Media.Brushes.White,
            Width = size,
            Height = 14,
            TextAlignment = Avalonia.Media.TextAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(n, 0);
        Canvas.SetTop(n, -14);

        var rotor = new Canvas
        {
            Width = size, Height = size,
            RenderTransformOrigin = new Avalonia.RelativePoint(0.5, 0.5, Avalonia.RelativeUnit.Relative),
            RenderTransform = _compassRotation,
            IsHitTestVisible = false,
        };
        rotor.Children.Add(southTail);
        rotor.Children.Add(arrow);
        rotor.Children.Add(n);

        var host = new Grid
        {
            Width = size,
            Height = size,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Avalonia.Thickness(0, 12, 12, 0),
            // Compass IS clickable now: tap it to snap the camera back to north.
            // Background must be a fill (transparent counts) so the Grid registers hits.
            Background = Avalonia.Media.Brushes.Transparent,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        host.Children.Add(background);
        host.Children.Add(rotor);
        host.PointerPressed += (_, e) =>
        {
            // Snap yaw back to north (90°). Pitch is left alone so the user keeps their tilt.
            _glControl.Camera.Yaw = 90f;
            UpdateCompass();
            _glControl.RequestRender();
            e.Handled = true;
        };
        return host;
    }

    private void UpdateCompass()
    {
        if (_compassRotation == null) return;
        // Map yaw → screen-CW rotation so the arrow points to world -Z (north) regardless of
        // the orbit angle: yaw=90 looks north → arrow up (0°); yaw=0 looks west → arrow right (90°).
        _compassRotation.Angle = 90 - _glControl.Camera.Yaw;
    }

    static OpenGlMapControl()
    {
        MapProperty.Changed.AddClassHandler<OpenGlMapControl>((c, e) =>
        {
            c._glControl.SetMap(e.NewValue as ScMap);
            if (e.NewValue != null)
                c._glControl.RequestTopDownSnapshot();
            c.UpdateCompass(); // camera yaw was reset by FitToMap
        });
    }

    /// <summary>
    /// Notify the GL control that the heightmap changed (e.g. after brush stroke or undo).
    /// </summary>
    public void InvalidateMesh()
    {
        _glControl.MarkMeshDirty();
    }

    /// <summary>Force re-upload of splatmaps to GPU on next render (after a symmetry/texture edit).</summary>
    public void InvalidateTextures()
    {
        _glControl.MarkTexturesDirty();
    }

    /// <summary>Recenter the camera on the next frame — used after the map dimensions change.</summary>
    public void RecenterCamera()
    {
        _glControl.RecenterOnNextFrame();
    }

    /// <summary>
    /// Notify the GL control that markers changed (added/deleted/moved/selected).
    /// </summary>
    public void InvalidateMarkers(Marker? selected)
    {
        _glControl.SelectedMarker = selected;
        _glControl.MarkMarkersDirty();
    }

    public void SetMarkerFilter(Func<Marker, bool> filter)
    {
        _glControl.MarkerFilter = filter;
        _glControl.MarkMarkersDirty();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        e.Pointer.Capture(this);
        _isDragging = true;
        _lastMouse = e.GetPosition(this);

        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsMiddleButtonPressed)
            _dragButton = 1; // pan
        else if (props.IsRightButtonPressed)
            _dragButton = 2; // orbit
        else if (props.IsLeftButtonPressed)
        {
            // Left click acts as PAN when no palette tool is selected (so the user can recenter the
            // map without picking up the middle button). With a tool active, it's the brush.
            bool brushActive = ViewModel?.IsBrush3DActive ?? false;
            if (brushActive)
            {
                _dragButton = 3; // brush
                var worldPos = ScreenToTerrain(e.GetPosition(this));
                if (worldPos.HasValue)
                    ViewModel?.BeginBrushStroke(worldPos.Value.X, worldPos.Value.Y);
            }
            else
            {
                _dragButton = 1; // pan (mirror of middle-click)
            }
        }

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);

        // Brush cursor follows the pointer ONLY when a palette tool is selected. Without an active
        // tool, left-click is inert (VM.BeginBrushStroke guards too) so showing the cursor would mislead.
        BrushWorldPos = ScreenToTerrain(pos);
        bool brushActive = ViewModel?.IsBrush3DActive ?? false;
        _glControl.BrushPos = brushActive ? BrushWorldPos : null;
        _glControl.BrushRadius = brushActive ? (float)(ViewModel?.BrushRadius ?? 0) : 0f;

        if (!_isDragging) { e.Handled = true; return; }

        if (_dragButton == 3) // brush
        {
            if (BrushWorldPos.HasValue)
            {
                ViewModel?.ApplyBrush(BrushWorldPos.Value.X, BrushWorldPos.Value.Y);
                _glControl.MarkMeshDirty();
            }
        }
        else
        {
            float dx = (float)(pos.X - _lastMouse.X);
            float dy = (float)(pos.Y - _lastMouse.Y);

            if (_dragButton == 2)
            {
                _glControl.Camera.Orbit(-dx * 0.3f, dy * 0.3f);
                UpdateCompass();
            }
            else if (_dragButton == 1)
                _glControl.Camera.Pan(-dx, dy);
        }

        _lastMouse = pos;
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragButton == 3)
        {
            ViewModel?.EndBrushStroke();
            _glControl.MarkMeshDirty();
        }
        e.Pointer.Capture(null);
        _isDragging = false;
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        // Zoom toward mouse position
        var worldPos = ScreenToTerrain(e.GetPosition(this));
        var camera = _glControl.Camera;
        float oldDist = camera.Distance;
        camera.Zoom((float)e.Delta.Y);

        if (worldPos.HasValue)
        {
            // Move target toward mouse world position proportionally to zoom change
            float ratio = 1f - camera.Distance / oldDist;
            var target = camera.Target;
            target.X += (worldPos.Value.X - target.X) * ratio;
            target.Z += (worldPos.Value.Y - target.Z) * ratio;
            camera.Target = target;
        }

        e.Handled = true;
    }

    /// <summary>
    /// Screen-to-terrain raycast: intersect camera ray with the Y=terrainHeight plane.
    /// </summary>
    private System.Numerics.Vector2? ScreenToTerrain(Point screenPos)
    {
        var map = Map;
        if (map == null) return null;

        float w = (float)Bounds.Width;
        float h = (float)Bounds.Height;
        if (w < 1 || h < 1) return null;

        var camera = _glControl.Camera;
        float aspect = w / h;
        var proj = camera.GetProjectionMatrix(aspect);
        var view = camera.GetViewMatrix();

        // Combined view-projection and its inverse
        var vp = view * proj;
        if (!System.Numerics.Matrix4x4.Invert(vp, out var invVP))
            return null;

        // NDC coordinates from screen position
        float nx = (float)(2.0 * screenPos.X / w - 1.0);
        float ny = (float)(1.0 - 2.0 * screenPos.Y / h);

        // Unproject near and far points
        var nearPoint = System.Numerics.Vector4.Transform(new System.Numerics.Vector4(nx, ny, -1, 1), invVP);
        var farPoint = System.Numerics.Vector4.Transform(new System.Numerics.Vector4(nx, ny, 1, 1), invVP);

        if (MathF.Abs(nearPoint.W) < 1e-6f || MathF.Abs(farPoint.W) < 1e-6f)
            return null;

        var near3 = new System.Numerics.Vector3(nearPoint.X, nearPoint.Y, nearPoint.Z) / nearPoint.W;
        var far3 = new System.Numerics.Vector3(farPoint.X, farPoint.Y, farPoint.Z) / farPoint.W;
        var dir = far3 - near3;

        // Use actual terrain height at map center as the intersection plane
        int cx = map.Heightmap.Width / 2;
        int cz = map.Heightmap.Height / 2;
        float planeY = map.Heightmap.GetWorldHeight(
            Math.Clamp(cx, 0, map.Heightmap.Width),
            Math.Clamp(cz, 0, map.Heightmap.Height));

        if (MathF.Abs(dir.Y) < 1e-6f) return null;

        float t = (planeY - near3.Y) / dir.Y;
        if (t < 0) return null;

        var hit = near3 + dir * t;

        // Clamp to map bounds
        float hx = Math.Clamp(hit.X, 0, map.Heightmap.Width);
        float hz = Math.Clamp(hit.Z, 0, map.Heightmap.Height);

        return new System.Numerics.Vector2(hx, hz);
    }

    /// <summary>Current brush position for rendering the cursor circle.</summary>
    public System.Numerics.Vector2? BrushWorldPos { get; private set; }
}
