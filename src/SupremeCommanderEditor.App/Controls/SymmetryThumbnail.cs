using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SupremeCommanderEditor.Core.Services;

namespace SupremeCommanderEditor.App.Controls;

/// <summary>
/// One symmetry pattern as a clickable preview. Shows the current top-down map bitmap with
/// pattern dividers drawn on top, highlights the region under the cursor, and fires
/// <see cref="RegionSelected"/> when the user clicks a region (which becomes the symmetry source).
///
/// On hover, the thumbnail also shows a live preview of what the map would look like after the
/// symmetry is applied: the source bitmap is pixel-warped through <see cref="SymmetryService.SourceOf"/>
/// with the hovered region as source and the current mode. Results are cached per
/// (region, mode) so re-hovering an already-warped region is instant.
/// </summary>
public class SymmetryThumbnail : Control
{
    public SymmetryPattern Pattern { get; set; }

    public static readonly StyledProperty<IImage?> SourceProperty =
        AvaloniaProperty.Register<SymmetryThumbnail, IImage?>(nameof(Source));

    public IImage? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public static readonly StyledProperty<SymmetryMode> ModeProperty =
        AvaloniaProperty.Register<SymmetryThumbnail, SymmetryMode>(nameof(Mode));

    /// <summary>Current symmetry mode (Mirror or Rotational). Bound from the VM. Triggers a
    /// re-render and invalidates the warped-bitmap cache.</summary>
    public SymmetryMode Mode
    {
        get => GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public event Action<SymmetryPattern, SymmetryRegion>? RegionSelected;

    private SymmetryRegion? _hoverRegion;

    // Cache of warped bitmaps keyed by (region, mode). Invalidated when Source changes — we hold
    // a reference to the source we built the cache from and drop the cache if it changes.
    private readonly Dictionary<(SymmetryRegion region, SymmetryMode mode), WriteableBitmap> _previewCache = new();
    private IImage? _cacheSource;

    static SymmetryThumbnail()
    {
        AffectsRender<SymmetryThumbnail>(SourceProperty);
        AffectsRender<SymmetryThumbnail>(ModeProperty);
        SourceProperty.Changed.AddClassHandler<SymmetryThumbnail>((c, _) => c.InvalidateCacheIfSourceChanged());
        ModeProperty.Changed.AddClassHandler<SymmetryThumbnail>((c, _) => c.InvalidateVisual());
    }

    public SymmetryThumbnail()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
        ClipToBounds = true;
    }

    private void InvalidateCacheIfSourceChanged()
    {
        if (!ReferenceEquals(_cacheSource, Source))
        {
            foreach (var bmp in _previewCache.Values) bmp.Dispose();
            _previewCache.Clear();
            _cacheSource = Source;
        }
    }

    private Rect MapRect()
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return default;
        if (Source == null) return new Rect(0, 0, w, h);
        double srcAspect = Source.Size.Width / Source.Size.Height;
        double rw, rh;
        if (w / h > srcAspect) { rh = h; rw = h * srcAspect; }
        else { rw = w; rh = w / srcAspect; }
        return new Rect((w - rw) / 2, (h - rh) / 2, rw, rh);
    }

    private SymmetryRegion? RegionAt(Point p)
    {
        var r = MapRect();
        if (r.Width <= 0 || !r.Contains(p)) return null;
        float u = (float)((p.X - r.X) / r.Width);
        float v = (float)((p.Y - r.Y) / r.Height);
        return SymmetryService.RegionOf(Pattern, u, v);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var newRegion = RegionAt(e.GetPosition(this));
        if (newRegion != _hoverRegion)
        {
            _hoverRegion = newRegion;
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoverRegion != null) { _hoverRegion = null; InvalidateVisual(); }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var region = RegionAt(e.GetPosition(this));
        if (region != null)
            RegionSelected?.Invoke(Pattern, region.Value);
    }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);
        var rect = MapRect();
        if (rect.Width <= 1) return;

        // Background
        ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(20, 20, 28)), new Rect(Bounds.Size));

        // Choose what to draw under the dividers: the warped preview if we're hovering, else the
        // raw source. Built lazily; ResolveBitmap returns null on the first frame after Source
        // arrives (compute happens off the render path).
        IImage? bg = Source;
        if (_hoverRegion is { } hr && Source is Bitmap sourceBitmap)
        {
            var warped = GetOrBuildPreview(sourceBitmap, hr);
            if (warped != null) bg = warped;
        }

        if (bg != null)
            ctx.DrawImage(bg, new Rect(0, 0, bg.Size.Width, bg.Size.Height), rect);

        // Slight dimming so dividers + highlight read clearly
        ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)), rect);

        // Region highlight under the cursor (always on top of the preview)
        if (_hoverRegion is { } hr2)
        {
            var poly = RegionPolygon(Pattern, hr2, rect);
            if (poly.Count >= 3)
            {
                var brush = new SolidColorBrush(Color.FromArgb(110, 90, 160, 255));
                var geom = new PolylineGeometry(poly, isFilled: true);
                ctx.DrawGeometry(brush, null, geom);
            }
        }

        // Pattern dividers
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), 2);
        var dim = new Pen(new SolidColorBrush(Color.FromArgb(220, 0, 0, 0)), 4); // outline
        DrawDividers(ctx, dim, rect);
        DrawDividers(ctx, pen, rect);
    }

    /// <summary>
    /// Pixel-warp the source bitmap through <see cref="SymmetryService.SourceOf"/> so the result
    /// shows exactly what the map would look like after the symmetry is applied with the given
    /// region as source. Cached by (region, mode) — first hover of a fresh combination pays the
    /// O(W*H) warp, subsequent hovers are O(1).
    /// </summary>
    private WriteableBitmap? GetOrBuildPreview(Bitmap source, SymmetryRegion region)
    {
        var key = (region, Mode);
        if (_previewCache.TryGetValue(key, out var cached)) return cached;

        int w = source.PixelSize.Width;
        int h = source.PixelSize.Height;
        if (w <= 0 || h <= 0) return null;

        // Read source pixels into a managed buffer.
        var srcBytes = new byte[w * h * 4];
        unsafe
        {
            fixed (byte* p = srcBytes)
            {
                source.CopyPixels(new PixelRect(0, 0, w, h), (IntPtr)p, srcBytes.Length, w * 4);
            }
        }

        var dstBytes = new byte[w * h * 4];
        var pattern = Pattern;
        var mode = Mode;
        for (int y = 0; y < h; y++)
        {
            float v = (y + 0.5f) / h;
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;
                var (su, sv) = SymmetryService.SourceOf(pattern, region, u, v, mode);
                int sx = Math.Clamp((int)MathF.Floor(su * w), 0, w - 1);
                int sy = Math.Clamp((int)MathF.Floor(sv * h), 0, h - 1);
                int srcIdx = (sy * w + sx) * 4;
                int dstIdx = (y * w + x) * 4;
                dstBytes[dstIdx]     = srcBytes[srcIdx];
                dstBytes[dstIdx + 1] = srcBytes[srcIdx + 1];
                dstBytes[dstIdx + 2] = srcBytes[srcIdx + 2];
                dstBytes[dstIdx + 3] = srcBytes[srcIdx + 3];
            }
        }

        var bmp = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96),
            PixelFormat.Rgba8888, AlphaFormat.Opaque);
        using (var locked = bmp.Lock())
        {
            Marshal.Copy(dstBytes, 0, locked.Address, dstBytes.Length);
        }
        _previewCache[key] = bmp;
        return bmp;
    }

    private void DrawDividers(DrawingContext ctx, IPen pen, Rect r)
    {
        double cx = r.X + r.Width / 2;
        double cy = r.Y + r.Height / 2;
        switch (Pattern)
        {
            case SymmetryPattern.Vertical:
                ctx.DrawLine(pen, new Point(cx, r.Y), new Point(cx, r.Bottom));
                break;
            case SymmetryPattern.Horizontal:
                ctx.DrawLine(pen, new Point(r.X, cy), new Point(r.Right, cy));
                break;
            case SymmetryPattern.DiagonalTLBR:
                ctx.DrawLine(pen, r.TopLeft, r.BottomRight);
                break;
            case SymmetryPattern.DiagonalTRBL:
                ctx.DrawLine(pen, r.TopRight, r.BottomLeft);
                break;
            case SymmetryPattern.QuadCross:
                ctx.DrawLine(pen, new Point(cx, r.Y), new Point(cx, r.Bottom));
                ctx.DrawLine(pen, new Point(r.X, cy), new Point(r.Right, cy));
                break;
            case SymmetryPattern.QuadDiagonals:
                ctx.DrawLine(pen, r.TopLeft, r.BottomRight);
                ctx.DrawLine(pen, r.TopRight, r.BottomLeft);
                break;
        }
    }

    private static IReadOnlyList<Point> RegionPolygon(SymmetryPattern p, SymmetryRegion region, Rect r)
    {
        double cx = r.X + r.Width / 2;
        double cy = r.Y + r.Height / 2;
        var tl = r.TopLeft;
        var tr = r.TopRight;
        var bl = r.BottomLeft;
        var br = r.BottomRight;
        var c = new Point(cx, cy);

        return (p, region) switch
        {
            (SymmetryPattern.Vertical, SymmetryRegion.R0) => [tl, new Point(cx, r.Y), new Point(cx, r.Bottom), bl],
            (SymmetryPattern.Vertical, _)                 => [new Point(cx, r.Y), tr, br, new Point(cx, r.Bottom)],
            (SymmetryPattern.Horizontal, SymmetryRegion.R0) => [tl, tr, new Point(r.Right, cy), new Point(r.X, cy)],
            (SymmetryPattern.Horizontal, _)                 => [new Point(r.X, cy), new Point(r.Right, cy), br, bl],
            (SymmetryPattern.DiagonalTLBR, SymmetryRegion.R0) => [tl, tr, br], // top-right triangle (v<u)
            (SymmetryPattern.DiagonalTLBR, _)                 => [tl, br, bl],
            (SymmetryPattern.DiagonalTRBL, SymmetryRegion.R0) => [tl, tr, bl], // top-left triangle (u+v<1)
            (SymmetryPattern.DiagonalTRBL, _)                 => [tr, br, bl],
            (SymmetryPattern.QuadCross, SymmetryRegion.R0) => [tl, new Point(cx, r.Y), c, new Point(r.X, cy)],
            (SymmetryPattern.QuadCross, SymmetryRegion.R1) => [new Point(cx, r.Y), tr, new Point(r.Right, cy), c],
            (SymmetryPattern.QuadCross, SymmetryRegion.R2) => [new Point(r.X, cy), c, new Point(cx, r.Bottom), bl],
            (SymmetryPattern.QuadCross, _)                 => [c, new Point(r.Right, cy), br, new Point(cx, r.Bottom)],
            (SymmetryPattern.QuadDiagonals, SymmetryRegion.R0) => [tl, tr, c], // N
            (SymmetryPattern.QuadDiagonals, SymmetryRegion.R1) => [tr, br, c], // E
            (SymmetryPattern.QuadDiagonals, SymmetryRegion.R2) => [br, bl, c], // S
            (SymmetryPattern.QuadDiagonals, _)                 => [bl, tl, c], // W
        };
    }
}
