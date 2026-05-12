using System.Runtime.InteropServices;
using System.Numerics;
using Avalonia.OpenGL;
using SupremeCommanderEditor.Core.Models;
using SupremeCommanderEditor.Core.Services;
using static Avalonia.OpenGL.GlConsts;

namespace SupremeCommanderEditor.Rendering;

public class TerrainRenderer : IDisposable
{
    private readonly GlInterface _gl;
    private ShaderProgram? _shader;
    private int _vao, _vbo, _ebo;
    private int _indexCount;
    private float _heightMin, _heightMax;
    private bool _initialized;

    // Texture state
    private GlTextureCache? _textureCache;
    private int[] _stratumTextureIds = new int[10];
    private float[] _stratumScales = new float[10];
    private int _stratumCount;
    private int _splatLowId, _splatHighId;
    private bool _texturesLoaded;
    private bool _loggedOnce;
    public bool TexturesLoaded => _texturesLoaded;
    private int _terrainShaderType;
    private int _upperLayerIndex = 9; // 0=TTerrain, 1=TTerrainXP/Terrain250

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GlActiveTextureDelegate(int texture);
    private const int GL_TEXTURE0 = 0x84C0;

    public TerrainRenderer(GlInterface gl)
    {
        _gl = gl;
    }

    public void Initialize(string vertSource, string fragSource)
    {
        _shader = new ShaderProgram(_gl, vertSource, fragSource);
        _textureCache = new GlTextureCache(_gl);
        _initialized = true;
    }

    /// <summary>
    /// Load stratum textures from game data and splatmaps from the ScMap.
    /// </summary>
    public void LoadTextures(ScMap map, GameDataService? gameData)
    {
        if (!_initialized || _textureCache == null) return;

        _stratumCount = map.TerrainTextures.Length;

        for (int i = 0; i < Math.Min(_stratumCount, 10); i++)
        {
            _stratumTextureIds[i] = _textureCache.GetOrLoad(map.TerrainTextures[i].AlbedoPath, gameData);
            _stratumScales[i] = map.TerrainTextures[i].AlbedoScale;
        }

        // Detect shader type for half-range transform
        _terrainShaderType = map.TerrainShader.Contains("XP") || map.TerrainShader.Contains("250") ? 1 : 0;

        // All maps reach this renderer normalised to 10 slots (MapStrataNormalizer.EnsureTenSlots
        // runs at load time and the generator always produces 10), so the macro overlay is always
        // at slot 9.
        _upperLayerIndex = 9;

        // Upload splatmaps
        if (map.TextureMaskLow.DdsData.Length > 128 && map.TextureMaskLow.Width > 0)
        {
            _splatLowId = _textureCache.UploadSplatMap(
                map.TextureMaskLow.DdsData, map.TextureMaskLow.Width, map.TextureMaskLow.Height);
        }
        // High splatmap: only use for maps with >5 overlay layers (v56+ with 10 strata)
        // For v53 maps with ≤6 layers, the "high" DDS is not a real splatmap
        if (_stratumCount > 5 + 1 && map.TextureMaskHigh.DdsData.Length > 128 && map.TextureMaskHigh.Width > 0)
        {
            _splatHighId = _textureCache.UploadSplatMap(
                map.TextureMaskHigh.DdsData, map.TextureMaskHigh.Width, map.TextureMaskHigh.Height);
        }
        else
        {
            // Empty splatmap (all zeros) — no strata 5-8 blending
            var empty = new byte[4 * 4 * 4]; // 4x4 black RGBA
            _splatHighId = _textureCache.UploadTexture(empty, 4, 4);
        }

        _texturesLoaded = _stratumCount > 0;
    }

    public unsafe void BuildMesh(Heightmap heightmap, int step = 1)
    {
        if (!_initialized) return;

        int w = heightmap.Width;
        int h = heightmap.Height;

        // Calculate grid dimensions with LOD step
        int gridW = w / step;
        int gridH = h / step;
        int vertCount = (gridW + 1) * (gridH + 1);

        var vertices = new TerrainVertex[vertCount];
        _heightMin = float.MaxValue;
        _heightMax = float.MinValue;

        // Build vertices
        for (int z = 0; z <= gridH; z++)
        {
            for (int x = 0; x <= gridW; x++)
            {
                int hx = Math.Min(x * step, w);
                int hz = Math.Min(z * step, h);
                float height = heightmap.GetWorldHeight(hx, hz);

                _heightMin = Math.Min(_heightMin, height);
                _heightMax = Math.Max(_heightMax, height);

                // Compute normal from neighboring heights
                float hL = hx > 0 ? heightmap.GetWorldHeight(hx - 1, hz) : height;
                float hR = hx < w ? heightmap.GetWorldHeight(hx + 1, hz) : height;
                float hD = hz > 0 ? heightmap.GetWorldHeight(hx, hz - 1) : height;
                float hU = hz < h ? heightmap.GetWorldHeight(hx, hz + 1) : height;

                var normal = Vector3.Normalize(new Vector3(hL - hR, 2f * step, hD - hU));

                int idx = z * (gridW + 1) + x;
                vertices[idx] = new TerrainVertex
                {
                    Position = new Vector3(hx, height, hz),
                    Normal = normal,
                    TexCoord = new Vector2((float)x / gridW, (float)z / gridH)
                };
            }
        }

        // Build indices (two triangles per quad)
        int quadCount = gridW * gridH;
        var indices = new int[quadCount * 6];
        int idx2 = 0;
        for (int z = 0; z < gridH; z++)
        {
            for (int x = 0; x < gridW; x++)
            {
                int tl = z * (gridW + 1) + x;
                int tr = tl + 1;
                int bl = (z + 1) * (gridW + 1) + x;
                int br = bl + 1;

                indices[idx2++] = tl;
                indices[idx2++] = bl;
                indices[idx2++] = tr;
                indices[idx2++] = tr;
                indices[idx2++] = bl;
                indices[idx2++] = br;
            }
        }
        _indexCount = indices.Length;

        // Upload to GPU
        DeleteBuffers();

        _vao = _gl.GenVertexArray();
        _gl.BindVertexArray(_vao);

        _vbo = _gl.GenBuffer();
        _gl.BindBuffer(GL_ARRAY_BUFFER, _vbo);
        fixed (TerrainVertex* ptr = vertices)
        {
            _gl.BufferData(GL_ARRAY_BUFFER, new IntPtr(vertices.Length * TerrainVertex.SizeInBytes),
                new IntPtr(ptr), GL_STATIC_DRAW);
        }

        _ebo = _gl.GenBuffer();
        _gl.BindBuffer(GL_ELEMENT_ARRAY_BUFFER, _ebo);
        fixed (int* ptr = indices)
        {
            _gl.BufferData(GL_ELEMENT_ARRAY_BUFFER, new IntPtr(indices.Length * sizeof(int)),
                new IntPtr(ptr), GL_STATIC_DRAW);
        }

        // Position (location 0)
        _gl.VertexAttribPointer(0, 3, GL_FLOAT, 0, TerrainVertex.SizeInBytes, IntPtr.Zero);
        _gl.EnableVertexAttribArray(0);

        // Normal (location 1)
        _gl.VertexAttribPointer(1, 3, GL_FLOAT, 0, TerrainVertex.SizeInBytes, new IntPtr(12));
        _gl.EnableVertexAttribArray(1);

        // TexCoord (location 2)
        _gl.VertexAttribPointer(2, 2, GL_FLOAT, 0, TerrainVertex.SizeInBytes, new IntPtr(24));
        _gl.EnableVertexAttribArray(2);

        _gl.BindVertexArray(0);
    }

    public void Render(Camera camera, float aspectRatio, LightingSettings lighting,
        Vector2? brushPos = null, float brushRadius = 0, int renderMode = 0)
    {
        if (!_initialized || _shader == null || _indexCount == 0) return;

        var activeTexture = Marshal.GetDelegateForFunctionPointer<GlActiveTextureDelegate>(
            _gl.GetProcAddress("glActiveTexture"));

        _shader.Use();
        _shader.SetUniform("uModel", Matrix4x4.Identity);
        _shader.SetUniform("uView", camera.GetViewMatrix());
        _shader.SetUniform("uProjection", camera.GetProjectionMatrix(aspectRatio));
        _shader.SetUniform("uSunDirection", lighting.SunDirection);
        _shader.SetUniform("uSunColor", lighting.SunColor);
        _shader.SetUniform("uAmbientColor", lighting.SunAmbience);
        _shader.SetUniform("uLightingMultiplier", lighting.LightingMultiplier);
        _shader.SetUniform("uHeightMin", _heightMin);
        _shader.SetUniform("uHeightMax", _heightMax);
        _shader.SetUniform("uBrushPos", brushPos ?? Vector2.Zero);
        _shader.SetUniform("uBrushRadius", brushRadius);

        // Bind textures if available
        int mode = _texturesLoaded ? 1 : 0;
        int locRM = _gl.GetUniformLocationString(_shader.Handle, "uRenderMode");
        int locHT = _gl.GetUniformLocationString(_shader.Handle, "uHasTextures");
        int locSC = _gl.GetUniformLocationString(_shader.Handle, "uStratumCount");
        if (!_loggedOnce)
        {
            SupremeCommanderEditor.Core.Services.DebugLog.Write($"[SHADER] uRenderMode loc={locRM}, uHasTextures loc={locHT}, uStratumCount loc={locSC}, mode={mode}");
            for (int i = 0; i < Math.Min(_stratumCount, 10); i++)
            {
                int locS = _gl.GetUniformLocationString(_shader.Handle, $"uStratum{i}");
                int locSc = _gl.GetUniformLocationString(_shader.Handle, $"uScale{i}");
                SupremeCommanderEditor.Core.Services.DebugLog.Write($"  Stratum{i}: texId={_stratumTextureIds[i]}, scale={_stratumScales[i]}, samplerLoc={locS}, scaleLoc={locSc}");
            }
            int locSL = _gl.GetUniformLocationString(_shader.Handle, "uSplatLow");
            int locSH = _gl.GetUniformLocationString(_shader.Handle, "uSplatHigh");
            SupremeCommanderEditor.Core.Services.DebugLog.Write($"  SplatLow loc={locSL} id={_splatLowId}, SplatHigh loc={locSH} id={_splatHighId}");
            _loggedOnce = true;
        }
        _shader.SetUniform("uRenderMode", mode);
        _shader.SetUniform("uHasTextures", _texturesLoaded ? 1 : 0);
        _shader.SetUniform("uStratumCount", _stratumCount);
        _shader.SetUniform("uTerrainShaderType", _terrainShaderType);
        _shader.SetUniform("uUpperLayerIndex", _upperLayerIndex);

        if (_texturesLoaded)
        {
            string[] stratumNames = ["uStratum0","uStratum1","uStratum2","uStratum3","uStratum4",
                                     "uStratum5","uStratum6","uStratum7","uStratum8","uStratum9"];
            string[] scaleNames = ["uScale0","uScale1","uScale2","uScale3","uScale4",
                                   "uScale5","uScale6","uScale7","uScale8","uScale9"];

            for (int i = 0; i < Math.Min(_stratumCount, 10); i++)
            {
                activeTexture(GL_TEXTURE0 + i);
                _gl.BindTexture(0x0DE1, _stratumTextureIds[i]);
                _shader.SetUniform(stratumNames[i], i);
                _shader.SetUniform(scaleNames[i], _stratumScales[i]);
            }

            // Splatmaps on units 10 and 11
            activeTexture(GL_TEXTURE0 + 10);
            _gl.BindTexture(0x0DE1, _splatLowId);
            _shader.SetUniform("uSplatLow", 10);

            activeTexture(GL_TEXTURE0 + 11);
            _gl.BindTexture(0x0DE1, _splatHighId);
            _shader.SetUniform("uSplatHigh", 11);

            activeTexture(GL_TEXTURE0);
        }

        _gl.BindVertexArray(_vao);
        _gl.DrawElements(GL_TRIANGLES, _indexCount, GlExtra.GL_UNSIGNED_INT, IntPtr.Zero);
        _gl.BindVertexArray(0);
    }

    private void DeleteBuffers()
    {
        if (_vbo != 0) { _gl.DeleteBuffer(_vbo); _vbo = 0; }
        if (_ebo != 0) { _gl.DeleteBuffer(_ebo); _ebo = 0; }
        if (_vao != 0) { _gl.DeleteVertexArray(_vao); _vao = 0; }
    }

    public void Dispose()
    {
        DeleteBuffers();
        _shader?.Dispose();
        _textureCache?.Dispose();
    }
}
