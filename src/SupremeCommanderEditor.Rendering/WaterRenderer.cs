using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia.OpenGL;
using SupremeCommanderEditor.Core.Models;
using static Avalonia.OpenGL.GlConsts;

namespace SupremeCommanderEditor.Rendering;

public class WaterRenderer : IDisposable
{
    private readonly GlInterface _gl;
    private ShaderProgram? _shader;
    private int _vao, _vbo;
    private bool _initialized;

    public WaterRenderer(GlInterface gl)
    {
        _gl = gl;
    }

    public void Initialize(string vertSource, string fragSource)
    {
        _shader = new ShaderProgram(_gl, vertSource, fragSource);
        _initialized = true;
    }

    public unsafe void BuildQuad(float mapWidth, float mapHeight, float waterElevation)
    {
        if (!_initialized) return;

        DeleteBuffers();

        // Simple quad at water elevation
        float[] vertices =
        [
            0, waterElevation, 0,
            mapWidth, waterElevation, 0,
            mapWidth, waterElevation, mapHeight,
            0, waterElevation, 0,
            mapWidth, waterElevation, mapHeight,
            0, waterElevation, mapHeight
        ];

        _vao = _gl.GenVertexArray();
        _gl.BindVertexArray(_vao);

        _vbo = _gl.GenBuffer();
        _gl.BindBuffer(GL_ARRAY_BUFFER, _vbo);
        fixed (float* ptr = vertices)
        {
            _gl.BufferData(GL_ARRAY_BUFFER, new IntPtr(vertices.Length * sizeof(float)),
                new IntPtr(ptr), GL_STATIC_DRAW);
        }

        _gl.VertexAttribPointer(0, 3, GL_FLOAT, 0, 3 * sizeof(float), IntPtr.Zero);
        _gl.EnableVertexAttribArray(0);
        _gl.BindVertexArray(0);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GlBlendFuncDelegate(int sfactor, int dfactor);

    private const int GL_BLEND = 0x0BE2;
    private const int GL_SRC_ALPHA = 0x0302;
    private const int GL_ONE_MINUS_SRC_ALPHA = 0x0303;

    public void Render(Camera camera, float aspectRatio, WaterSettings water)
    {
        if (!_initialized || _shader == null || !water.HasWater || _vao == 0) return;

        _shader.Use();
        _shader.SetUniform("uView", camera.GetViewMatrix());
        _shader.SetUniform("uProjection", camera.GetProjectionMatrix(aspectRatio));
        _shader.SetUniform("uWaterColor", water.SurfaceColor);
        _shader.SetUniform("uAlpha", 0.7f);

        _gl.Enable(GL_BLEND);
        var blendFunc = Marshal.GetDelegateForFunctionPointer<GlBlendFuncDelegate>(
            _gl.GetProcAddress("glBlendFunc"));
        blendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);

        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(GL_TRIANGLES, 0, 6);
        _gl.BindVertexArray(0);

        _gl.Disable(GL_BLEND);
    }

    private void DeleteBuffers()
    {
        if (_vbo != 0) { _gl.DeleteBuffer(_vbo); _vbo = 0; }
        if (_vao != 0) { _gl.DeleteVertexArray(_vao); _vao = 0; }
    }

    public void Dispose()
    {
        DeleteBuffers();
        _shader?.Dispose();
    }
}
