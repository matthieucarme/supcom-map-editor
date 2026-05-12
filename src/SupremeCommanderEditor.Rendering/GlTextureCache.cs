using System.Runtime.InteropServices;
using Avalonia.OpenGL;
using SupremeCommanderEditor.Core.Services;
using static Avalonia.OpenGL.GlConsts;
using Pfim;

namespace SupremeCommanderEditor.Rendering;

/// <summary>
/// Caches OpenGL textures loaded from game data or raw pixel arrays.
/// </summary>
public class GlTextureCache : IDisposable
{
    private readonly GlInterface _gl;
    private readonly Dictionary<string, int> _textureIds = new();
    private int _fallbackTexture;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GlTexParameteriDelegate(int target, int pname, int param);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GlGenerateMipmapDelegate(int target);

    private const int GL_TEXTURE_2D = 0x0DE1;
    private const int GL_TEXTURE_MIN_FILTER = 0x2801;
    private const int GL_TEXTURE_MAG_FILTER = 0x2800;
    private const int GL_TEXTURE_WRAP_S = 0x2802;
    private const int GL_TEXTURE_WRAP_T = 0x2803;
    private const int GL_LINEAR = 0x2601;
    private const int GL_LINEAR_MIPMAP_LINEAR = 0x2703;
    private const int GL_REPEAT = 0x2901;
    private const int GL_CLAMP_TO_EDGE = 0x812F;
    private const int GL_RGBA = 0x1908;
    private const int GL_BGRA = 0x80E1;
    private const int GL_SRGB8_ALPHA8 = 0x8C43; // sRGB color space for texture storage
    private const int GL_UNSIGNED_BYTE = 0x1401;

    // S3TC compressed texture formats
    private const int GL_COMPRESSED_RGB_S3TC_DXT1 = 0x83F0;
    private const int GL_COMPRESSED_RGBA_S3TC_DXT1 = 0x83F1;
    private const int GL_COMPRESSED_RGBA_S3TC_DXT3 = 0x83F2;
    private const int GL_COMPRESSED_RGBA_S3TC_DXT5 = 0x83F3;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GlCompressedTexImage2DDelegate(
        int target, int level, int internalformat, int width, int height,
        int border, int imageSize, IntPtr data);

    public GlTextureCache(GlInterface gl)
    {
        _gl = gl;
        CreateFallbackTexture();
    }

    /// <summary>
    /// Get or load a texture by game path. Returns GL texture ID.
    /// Uses Pfim to decompress DDS, swaps BGRA→RGBA, uploads as GL_RGBA.
    /// </summary>
    public int GetOrLoad(string gamePath, GameDataService? gameData)
    {
        if (string.IsNullOrEmpty(gamePath))
        {
            SupremeCommanderEditor.Core.Services.DebugLog.Write("[TexCache] empty path → fallback");
            return _fallbackTexture;
        }

        if (_textureIds.TryGetValue(gamePath, out int cached))
            return cached;

        if (gameData == null)
        {
            SupremeCommanderEditor.Core.Services.DebugLog.Write($"[TexCache] {gamePath}: no GameData → fallback");
            return _fallbackTexture;
        }

        var tex = gameData.LoadTextureDds(gamePath);
        if (tex == null)
        {
            SupremeCommanderEditor.Core.Services.DebugLog.Write($"[TexCache] {gamePath}: DECODE failed → fallback");
            return _fallbackTexture;
        }

        // GameDataService already handles BGRA→RGBA swap
        int id = UploadTexture(tex.Value.Pixels, tex.Value.Width, tex.Value.Height, GL_RGBA);
        _textureIds[gamePath] = id;
        SupremeCommanderEditor.Core.Services.DebugLog.Write($"[TexCache] {gamePath}: uploaded {tex.Value.Width}x{tex.Value.Height} → id={id}");
        return id;
    }

    /// <summary>
    /// Upload a DDS file directly to GL, keeping compressed formats on the GPU.
    /// </summary>
    private unsafe int UploadDdsDirectly(byte[] ddsData)
    {
        if (ddsData.Length < 128 || ddsData[0] != 'D' || ddsData[1] != 'D' || ddsData[2] != 'S')
            return 0;

        int height = BitConverter.ToInt32(ddsData, 12);
        int width = BitConverter.ToInt32(ddsData, 16);
        int pfFlags = BitConverter.ToInt32(ddsData, 80);
        var fourcc = System.Text.Encoding.ASCII.GetString(ddsData, 84, 4);

        var texParam = Marshal.GetDelegateForFunctionPointer<GlTexParameteriDelegate>(
            _gl.GetProcAddress("glTexParameteri"));
        var genMipmap = Marshal.GetDelegateForFunctionPointer<GlGenerateMipmapDelegate>(
            _gl.GetProcAddress("glGenerateMipmap"));

        int id = _gl.GenTexture();
        _gl.BindTexture(GL_TEXTURE_2D, id);

        int headerSize = 128;
        int dataLen = ddsData.Length - headerSize;

        fixed (byte* ptr = &ddsData[headerSize])
        {
            if (fourcc == "DXT1")
            {
                int blockSize = 8;
                int mainLevelSize = Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * blockSize;
                var compressedTexImage = Marshal.GetDelegateForFunctionPointer<GlCompressedTexImage2DDelegate>(
                    _gl.GetProcAddress("glCompressedTexImage2D"));
                compressedTexImage(GL_TEXTURE_2D, 0, GL_COMPRESSED_RGBA_S3TC_DXT1,
                    width, height, 0, Math.Min(mainLevelSize, dataLen), new IntPtr(ptr));
            }
            else if (fourcc == "DXT5")
            {
                int blockSize = 16;
                int mainLevelSize = Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * blockSize;
                var compressedTexImage = Marshal.GetDelegateForFunctionPointer<GlCompressedTexImage2DDelegate>(
                    _gl.GetProcAddress("glCompressedTexImage2D"));
                compressedTexImage(GL_TEXTURE_2D, 0, GL_COMPRESSED_RGBA_S3TC_DXT5,
                    width, height, 0, Math.Min(mainLevelSize, dataLen), new IntPtr(ptr));
            }
            else if (fourcc == "DXT3")
            {
                int blockSize = 16;
                int mainLevelSize = Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * blockSize;
                var compressedTexImage = Marshal.GetDelegateForFunctionPointer<GlCompressedTexImage2DDelegate>(
                    _gl.GetProcAddress("glCompressedTexImage2D"));
                compressedTexImage(GL_TEXTURE_2D, 0, GL_COMPRESSED_RGBA_S3TC_DXT3,
                    width, height, 0, Math.Min(mainLevelSize, dataLen), new IntPtr(ptr));
            }
            else
            {
                // Uncompressed ARGB DDS: bytes are B,G,R,A in memory
                // Swap to RGBA before upload
                int pixCount = width * height;
                var rgba = new byte[pixCount * 4];
                for (int i = 0; i < pixCount * 4; i += 4)
                {
                    rgba[i]     = ptr[i + 2]; // R ← B
                    rgba[i + 1] = ptr[i + 1]; // G
                    rgba[i + 2] = ptr[i];     // B ← R
                    rgba[i + 3] = ptr[i + 3]; // A
                }
                fixed (byte* rgbaPtr = rgba)
                {
                    _gl.TexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, width, height, 0,
                        GL_RGBA, GL_UNSIGNED_BYTE, new IntPtr(rgbaPtr));
                }
            }
        }

        genMipmap(GL_TEXTURE_2D);
        texParam(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR_MIPMAP_LINEAR);
        texParam(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        texParam(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_REPEAT);
        texParam(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_REPEAT);

        _gl.BindTexture(GL_TEXTURE_2D, 0);
        return id;
    }

    /// <summary>
    /// Upload raw pixel data as a GL texture.
    /// </summary>
    /// <param name="srgb">If true, use GL_SRGB8_ALPHA8 internal format (for albedo textures).</param>
    public int UploadTexture(byte[] pixels, int width, int height, int format = GL_RGBA, bool srgb = false)
    {
        var texParam = Marshal.GetDelegateForFunctionPointer<GlTexParameteriDelegate>(
            _gl.GetProcAddress("glTexParameteri"));
        var genMipmap = Marshal.GetDelegateForFunctionPointer<GlGenerateMipmapDelegate>(
            _gl.GetProcAddress("glGenerateMipmap"));

        int id = _gl.GenTexture();
        _gl.BindTexture(GL_TEXTURE_2D, id);

        unsafe
        {
            int internalFormat = srgb ? GL_SRGB8_ALPHA8 : GL_RGBA;
            fixed (byte* ptr = pixels)
            {
                _gl.TexImage2D(GL_TEXTURE_2D, 0, internalFormat, width, height, 0,
                    format, GL_UNSIGNED_BYTE, new IntPtr(ptr));
            }
        }

        genMipmap(GL_TEXTURE_2D);
        texParam(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR_MIPMAP_LINEAR);
        texParam(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        texParam(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_REPEAT);
        texParam(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_REPEAT);

        _gl.BindTexture(GL_TEXTURE_2D, 0);
        return id;
    }

    /// <summary>
    /// Upload a DDS splatmap (from .scmap) as a GL texture.
    /// Uses Pfim for decompression, manually swaps BGRA→RGBA, uploads as GL_RGBA.
    /// </summary>
    public int UploadSplatMap(byte[] ddsData, int width, int height)
    {
        try
        {
            using var image = Pfim.Pfimage.FromStream(new MemoryStream(ddsData));
            if (image.Data == null) return _fallbackTexture;

            int w = image.Width, h = image.Height;
            int pixelCount = w * h;
            if (image.Data.Length < pixelCount * 4) return _fallbackTexture;

            // Pfim BGRA → manual swap to RGBA for GL_RGBA upload
            var rgba = new byte[pixelCount * 4];
            for (int i = 0; i < pixelCount * 4; i += 4)
            {
                rgba[i]     = image.Data[i + 2]; // R ← B
                rgba[i + 1] = image.Data[i + 1]; // G
                rgba[i + 2] = image.Data[i];     // B ← R
                rgba[i + 3] = image.Data[i + 3]; // A
            }

            int id = UploadTexture(rgba, w, h, GL_RGBA);

            // Splatmaps must use CLAMP_TO_EDGE
            var texParam2 = Marshal.GetDelegateForFunctionPointer<GlTexParameteriDelegate>(
                _gl.GetProcAddress("glTexParameteri"));
            _gl.BindTexture(GL_TEXTURE_2D, id);
            texParam2(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
            texParam2(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
            _gl.BindTexture(GL_TEXTURE_2D, 0);

            return id;
        }
        catch
        {
            return _fallbackTexture;
        }
    }

    private void CreateFallbackTexture()
    {
        // Solid magenta — easy to spot where textures fail to load
        byte[] pixels = [255, 0, 255, 255, 255, 0, 255, 255,
                         255, 0, 255, 255, 255, 0, 255, 255];
        _fallbackTexture = UploadTexture(pixels, 2, 2);
    }

    public void Dispose()
    {
        foreach (var id in _textureIds.Values)
            _gl.DeleteTexture(id);
        _textureIds.Clear();
        if (_fallbackTexture != 0)
        {
            _gl.DeleteTexture(_fallbackTexture);
            _fallbackTexture = 0;
        }
    }
}
