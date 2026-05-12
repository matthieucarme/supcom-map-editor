using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.OpenGL;
using static Avalonia.OpenGL.GlConsts;

namespace SupremeCommanderEditor.Rendering;

// GL constants not in Avalonia's GlConsts
public static class GlExtra
{
    public const int GL_UNSIGNED_INT = 0x1405;
    public const int GL_FRAMEBUFFER = 0x8D40;
    public const int GL_RENDERBUFFER = 0x8D41;
    public const int GL_COLOR_ATTACHMENT0 = 0x8CE0;
    public const int GL_DEPTH_ATTACHMENT = 0x8D00;
    public const int GL_DEPTH_COMPONENT24 = 0x81A6;
    public const int GL_RGBA8 = 0x8058;
    public const int GL_RGBA = 0x1908;
    public const int GL_UNSIGNED_BYTE = 0x1401;
    public const int GL_FRAMEBUFFER_COMPLETE = 0x8CD5;
    public const int GL_PACK_ALIGNMENT = 0x0D05;
}

/// <summary>
/// Helpers for off-screen rendering via FBO/RBO, used by the 2D top-down snapshot.
/// All operations go through GetProcAddress since Avalonia's GlInterface doesn't expose them.
/// </summary>
public static class GlFbo
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GlGenDelegate(int n, IntPtr ids);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GlBindDelegate(int target, int id);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GlRenderbufferStorageDelegate(int target, int internalformat, int width, int height);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GlFramebufferRenderbufferDelegate(int target, int attachment, int rbTarget, int rbId);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GlCheckFramebufferStatusDelegate(int target);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GlReadPixelsDelegate(int x, int y, int w, int h, int format, int type, IntPtr data);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GlPixelStoreiDelegate(int pname, int param);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GlDeleteDelegate(int n, IntPtr ids);

    public static int GenFramebuffer(GlInterface gl)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<GlGenDelegate>(gl.GetProcAddress("glGenFramebuffers"));
        int id = 0;
        unsafe { fn(1, (IntPtr)(&id)); }
        return id;
    }

    public static int GenRenderbuffer(GlInterface gl)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<GlGenDelegate>(gl.GetProcAddress("glGenRenderbuffers"));
        int id = 0;
        unsafe { fn(1, (IntPtr)(&id)); }
        return id;
    }

    public static void BindFramebuffer(GlInterface gl, int id)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<GlBindDelegate>(gl.GetProcAddress("glBindFramebuffer"));
        fn(GlExtra.GL_FRAMEBUFFER, id);
    }

    public static void BindRenderbuffer(GlInterface gl, int id)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<GlBindDelegate>(gl.GetProcAddress("glBindRenderbuffer"));
        fn(GlExtra.GL_RENDERBUFFER, id);
    }

    public static void RenderbufferStorage(GlInterface gl, int internalFormat, int w, int h)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<GlRenderbufferStorageDelegate>(gl.GetProcAddress("glRenderbufferStorage"));
        fn(GlExtra.GL_RENDERBUFFER, internalFormat, w, h);
    }

    public static void FramebufferRenderbuffer(GlInterface gl, int attachment, int rbId)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<GlFramebufferRenderbufferDelegate>(gl.GetProcAddress("glFramebufferRenderbuffer"));
        fn(GlExtra.GL_FRAMEBUFFER, attachment, GlExtra.GL_RENDERBUFFER, rbId);
    }

    public static int CheckFramebufferStatus(GlInterface gl)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<GlCheckFramebufferStatusDelegate>(gl.GetProcAddress("glCheckFramebufferStatus"));
        return fn(GlExtra.GL_FRAMEBUFFER);
    }

    public static void ReadPixels(GlInterface gl, int w, int h, byte[] dst)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<GlReadPixelsDelegate>(gl.GetProcAddress("glReadPixels"));
        var pixStore = Marshal.GetDelegateForFunctionPointer<GlPixelStoreiDelegate>(gl.GetProcAddress("glPixelStorei"));
        pixStore(GlExtra.GL_PACK_ALIGNMENT, 1);
        unsafe
        {
            fixed (byte* p = dst)
            {
                fn(0, 0, w, h, GlExtra.GL_RGBA, GlExtra.GL_UNSIGNED_BYTE, (IntPtr)p);
            }
        }
    }

    public static void DeleteFramebuffer(GlInterface gl, int id)
    {
        if (id == 0) return;
        var fn = Marshal.GetDelegateForFunctionPointer<GlDeleteDelegate>(gl.GetProcAddress("glDeleteFramebuffers"));
        unsafe { fn(1, (IntPtr)(&id)); }
    }

    public static void DeleteRenderbuffer(GlInterface gl, int id)
    {
        if (id == 0) return;
        var fn = Marshal.GetDelegateForFunctionPointer<GlDeleteDelegate>(gl.GetProcAddress("glDeleteRenderbuffers"));
        unsafe { fn(1, (IntPtr)(&id)); }
    }
}

/// <summary>
/// Extension methods for Avalonia's GlInterface to provide convenient GL wrappers.
/// Uses GetProcAddress for functions not directly exposed.
/// </summary>
public static class GlExtensions
{
    // Delegate types for GL functions accessed via GetProcAddress
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GlUniform3fDelegate(int location, float v0, float v1, float v2);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GlShaderSourceDelegate(int shader, int count, IntPtr strings, IntPtr lengths);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GlGetShaderInfoLogDelegate(int shader, int maxLength, out int length, IntPtr infoLog);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GlGetProgramInfoLogDelegate(int program, int maxLength, out int length, IntPtr infoLog);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GlGetShaderivDelegate(int shader, int pname, out int param);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GlGetProgramivDelegate(int program, int pname, out int param);

    private const int GL_COMPILE_STATUS = 0x8B81;
    private const int GL_LINK_STATUS = 0x8B82;
    private const int GL_INFO_LOG_LENGTH = 0x8B84;

    public static void ShaderSourceString(this GlInterface gl, int shader, string source)
    {
        var sourceBytes = Encoding.UTF8.GetBytes(source + '\0');
        var fn = Marshal.GetDelegateForFunctionPointer<GlShaderSourceDelegate>(gl.GetProcAddress("glShaderSource"));
        unsafe
        {
            fixed (byte* pSource = sourceBytes)
            {
                IntPtr pStr = (IntPtr)pSource;
                fn(shader, 1, (IntPtr)(&pStr), IntPtr.Zero);
            }
        }
    }

    public static string GetShaderInfoLogString(this GlInterface gl, int shader)
    {
        var getiv = Marshal.GetDelegateForFunctionPointer<GlGetShaderivDelegate>(gl.GetProcAddress("glGetShaderiv"));
        getiv(shader, GL_INFO_LOG_LENGTH, out int logLen);
        if (logLen <= 0) return "";

        var getLog = Marshal.GetDelegateForFunctionPointer<GlGetShaderInfoLogDelegate>(gl.GetProcAddress("glGetShaderInfoLog"));
        var buf = Marshal.AllocHGlobal(logLen + 1);
        try
        {
            getLog(shader, logLen + 1, out int actualLen, buf);
            return Marshal.PtrToStringAnsi(buf, actualLen) ?? "";
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    public static bool GetShaderCompileStatus(this GlInterface gl, int shader)
    {
        var getiv = Marshal.GetDelegateForFunctionPointer<GlGetShaderivDelegate>(gl.GetProcAddress("glGetShaderiv"));
        getiv(shader, GL_COMPILE_STATUS, out int status);
        return status != 0;
    }

    public static string GetProgramInfoLogString(this GlInterface gl, int program)
    {
        var getiv = Marshal.GetDelegateForFunctionPointer<GlGetProgramivDelegate>(gl.GetProcAddress("glGetProgramiv"));
        getiv(program, GL_INFO_LOG_LENGTH, out int logLen);
        if (logLen <= 0) return "";

        var getLog = Marshal.GetDelegateForFunctionPointer<GlGetProgramInfoLogDelegate>(gl.GetProcAddress("glGetProgramInfoLog"));
        var buf = Marshal.AllocHGlobal(logLen + 1);
        try
        {
            getLog(program, logLen + 1, out int actualLen, buf);
            return Marshal.PtrToStringAnsi(buf, actualLen) ?? "";
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    public static bool GetProgramLinkStatus(this GlInterface gl, int program)
    {
        var getiv = Marshal.GetDelegateForFunctionPointer<GlGetProgramivDelegate>(gl.GetProcAddress("glGetProgramiv"));
        getiv(program, GL_LINK_STATUS, out int status);
        return status != 0;
    }

    public static void Uniform3f(this GlInterface gl, int location, float v0, float v1, float v2)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<GlUniform3fDelegate>(gl.GetProcAddress("glUniform3f"));
        fn(location, v0, v1, v2);
    }
}

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate void GlUniform2fDelegate(int location, float v0, float v1);

public class ShaderProgram : IDisposable
{
    private readonly GlInterface _gl;
    public int Handle { get; }

    public ShaderProgram(GlInterface gl, string vertexSource, string fragmentSource)
    {
        _gl = gl;

        int vert = CompileShader(gl, GL_VERTEX_SHADER, vertexSource);
        int frag = CompileShader(gl, GL_FRAGMENT_SHADER, fragmentSource);

        Handle = gl.CreateProgram();
        gl.AttachShader(Handle, vert);
        gl.AttachShader(Handle, frag);
        gl.LinkProgram(Handle);

        if (!gl.GetProgramLinkStatus(Handle))
        {
            string log = gl.GetProgramInfoLogString(Handle);
            throw new Exception($"Shader link error: {log}");
        }

        gl.DeleteShader(vert);
        gl.DeleteShader(frag);
    }

    private static int CompileShader(GlInterface gl, int type, string source)
    {
        int shader = gl.CreateShader(type);
        gl.ShaderSourceString(shader, source);
        gl.CompileShader(shader);

        if (!gl.GetShaderCompileStatus(shader))
        {
            string log = gl.GetShaderInfoLogString(shader);
            throw new Exception($"Shader compile error ({(type == GL_VERTEX_SHADER ? "vert" : "frag")}): {log}");
        }

        return shader;
    }

    public void Use() => _gl.UseProgram(Handle);

    public void SetUniform(string name, float value)
    {
        int loc = _gl.GetUniformLocationString(Handle, name);
        if (loc >= 0) _gl.Uniform1f(loc, value);
    }

    public void SetUniform(string name, int value)
    {
        int loc = _gl.GetUniformLocationString(Handle, name);
        if (loc >= 0) _gl.Uniform1i(loc, value);
    }

    public void SetUniform(string name, Vector2 value)
    {
        int loc = _gl.GetUniformLocationString(Handle, name);
        if (loc >= 0)
        {
            var fn = Marshal.GetDelegateForFunctionPointer<GlUniform2fDelegate>(
                _gl.GetProcAddress("glUniform2f"));
            fn(loc, value.X, value.Y);
        }
    }

    public void SetUniform(string name, Vector3 value)
    {
        int loc = _gl.GetUniformLocationString(Handle, name);
        if (loc >= 0) _gl.Uniform3f(loc, value.X, value.Y, value.Z);
    }

    public unsafe void SetUniform(string name, Matrix4x4 value)
    {
        int loc = _gl.GetUniformLocationString(Handle, name);
        if (loc >= 0)
        {
            float* ptr = (float*)&value;
            _gl.UniformMatrix4fv(loc, 1, false, ptr);
        }
    }

    public void Dispose()
    {
        _gl.DeleteProgram(Handle);
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TerrainVertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 TexCoord;

    public const int SizeInBytes = 32; // 3+3+2 floats * 4 bytes
}
