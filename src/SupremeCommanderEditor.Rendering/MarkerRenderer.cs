using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia.OpenGL;
using SupremeCommanderEditor.Core.Models;
using static Avalonia.OpenGL.GlConsts;

namespace SupremeCommanderEditor.Rendering;

public class MarkerRenderer : IDisposable
{
    private readonly GlInterface _gl;
    private ShaderProgram? _shader;
    private int _vao, _vbo;
    private int _vertexCount;
    private bool _initialized;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GlEnableDelegate(int cap);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GlDisableDelegate(int cap);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GlBlendFuncDelegate(int sfactor, int dfactor);

    private const int GL_PROGRAM_POINT_SIZE = 0x8642;
    private const int GL_BLEND = 0x0BE2;
    private const int GL_SRC_ALPHA = 0x0302;
    private const int GL_ONE_MINUS_SRC_ALPHA = 0x0303;
    private const int GL_POINTS = 0x0000;
    private const int GL_FLOAT = 0x1406;
    private const int GL_DYNAMIC_DRAW = 0x88E8;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct MarkerVertex
    {
        public Vector3 Position;
        public Vector4 Color;
        public const int SizeInBytes = 28; // 3 + 4 floats = 28 bytes
    }

    public MarkerRenderer(GlInterface gl)
    {
        _gl = gl;
    }

    public void Initialize(string vertSource, string fragSource)
    {
        _shader = new ShaderProgram(_gl, vertSource, fragSource);
        _initialized = true;
    }

    public unsafe void UpdateMarkers(List<Marker> markers, Marker? selected, Heightmap heightmap,
        Func<Marker, bool>? filter = null)
    {
        if (!_initialized) return;

        var filtered = filter != null ? markers.Where(filter).ToList() : markers;
        var vertices = new MarkerVertex[filtered.Count];
        for (int i = 0; i < filtered.Count; i++)
        {
            var m = filtered[i];
            // Use marker Y position, offset slightly above terrain to avoid z-fighting
            float y = m.Position.Y + 1.5f;

            var color = GetMarkerColor(m);
            // Brighten selected marker
            if (m == selected)
                color = new Vector4(1f, 1f, 0.3f, 1f);

            vertices[i] = new MarkerVertex
            {
                Position = new Vector3(m.Position.X, y, m.Position.Z),
                Color = color
            };
        }

        _vertexCount = vertices.Length;
        if (_vertexCount == 0) return;

        if (_vbo == 0)
        {
            _vao = _gl.GenVertexArray();
            _vbo = _gl.GenBuffer();
        }

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(GL_ARRAY_BUFFER, _vbo);

        fixed (MarkerVertex* ptr = vertices)
        {
            _gl.BufferData(GL_ARRAY_BUFFER,
                new IntPtr(vertices.Length * MarkerVertex.SizeInBytes),
                new IntPtr(ptr), GL_DYNAMIC_DRAW);
        }

        // Position (location 0)
        _gl.VertexAttribPointer(0, 3, GL_FLOAT, 0, MarkerVertex.SizeInBytes, IntPtr.Zero);
        _gl.EnableVertexAttribArray(0);

        // Color (location 1)
        _gl.VertexAttribPointer(1, 4, GL_FLOAT, 0, MarkerVertex.SizeInBytes, new IntPtr(12));
        _gl.EnableVertexAttribArray(1);

        _gl.BindVertexArray(0);
    }

    public void Render(Camera camera, float aspectRatio)
    {
        if (!_initialized || _shader == null || _vertexCount == 0) return;

        var enable = Marshal.GetDelegateForFunctionPointer<GlEnableDelegate>(
            _gl.GetProcAddress("glEnable"));
        var disable = Marshal.GetDelegateForFunctionPointer<GlDisableDelegate>(
            _gl.GetProcAddress("glDisable"));
        var blendFunc = Marshal.GetDelegateForFunctionPointer<GlBlendFuncDelegate>(
            _gl.GetProcAddress("glBlendFunc"));

        _shader.Use();

        var vp = camera.GetViewMatrix() * camera.GetProjectionMatrix(aspectRatio);
        _shader.SetUniform("uViewProjection", vp);
        _shader.SetUniform("uPointSize", 300f);

        enable(GL_PROGRAM_POINT_SIZE);
        enable(GL_BLEND);
        blendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);

        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(GL_POINTS, 0, _vertexCount);
        _gl.BindVertexArray(0);

        disable(GL_PROGRAM_POINT_SIZE);
        disable(GL_BLEND);
    }

    private static Vector4 GetMarkerColor(Marker m)
    {
        return m.Type switch
        {
            MarkerType.Mass => new Vector4(0.2f, 0.9f, 0.2f, 1f),
            MarkerType.Hydrocarbon => new Vector4(0.1f, 0.8f, 0.8f, 1f),
            MarkerType.BlankMarker when m.Name.StartsWith("ARMY_") => new Vector4(1f, 0.2f, 0.2f, 1f),
            MarkerType.ExpansionArea or MarkerType.LargeExpansionArea => new Vector4(0.1f, 0.7f, 0.7f, 0.7f),
            MarkerType.NavalArea => new Vector4(0.2f, 0.2f, 1f, 0.7f),
            MarkerType.DefensePoint => new Vector4(0.1f, 0.7f, 0.1f, 0.7f),
            MarkerType.RallyPoint or MarkerType.NavalRallyPoint => new Vector4(0.8f, 0.8f, 0.1f, 0.7f),
            MarkerType.LandPathNode or MarkerType.AirPathNode or MarkerType.WaterPathNode
                or MarkerType.AmphibiousPathNode => new Vector4(0.5f, 0.5f, 0.5f, 0.5f),
            _ => new Vector4(0.6f, 0.6f, 0.6f, 0.5f)
        };
    }

    public void Dispose()
    {
        if (_vbo != 0) { _gl.DeleteBuffer(_vbo); _vbo = 0; }
        if (_vao != 0) { _gl.DeleteVertexArray(_vao); _vao = 0; }
        _shader?.Dispose();
    }
}
