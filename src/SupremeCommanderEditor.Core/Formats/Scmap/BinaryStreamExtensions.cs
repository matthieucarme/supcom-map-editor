using System.Numerics;
using System.Text;

namespace SupremeCommanderEditor.Core.Formats.Scmap;

public static class BinaryStreamExtensions
{
    // === Reading ===

    public static string ReadNullTerminatedString(this BinaryReader reader)
    {
        var sb = new StringBuilder();
        byte b;
        while ((b = reader.ReadByte()) != 0)
        {
            sb.Append((char)b);
        }
        return sb.ToString();
    }

    public static string ReadLengthPrefixedString(this BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length <= 0)
            return string.Empty;
        var bytes = reader.ReadBytes(length);
        return Encoding.ASCII.GetString(bytes);
    }

    public static Vector2 ReadVector2(this BinaryReader reader)
    {
        float x = reader.ReadSingle();
        float y = reader.ReadSingle();
        return new Vector2(x, y);
    }

    public static Vector3 ReadVector3(this BinaryReader reader)
    {
        float x = reader.ReadSingle();
        float y = reader.ReadSingle();
        float z = reader.ReadSingle();
        return new Vector3(x, y, z);
    }

    public static Vector4 ReadVector4(this BinaryReader reader)
    {
        float x = reader.ReadSingle();
        float y = reader.ReadSingle();
        float z = reader.ReadSingle();
        float w = reader.ReadSingle();
        return new Vector4(x, y, z, w);
    }

    // === Writing ===

    public static void WriteNullTerminatedString(this BinaryWriter writer, string value)
    {
        writer.Write(Encoding.ASCII.GetBytes(value));
        writer.Write((byte)0);
    }

    public static void WriteLengthPrefixedString(this BinaryWriter writer, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    public static void WriteVector2(this BinaryWriter writer, Vector2 v)
    {
        writer.Write(v.X);
        writer.Write(v.Y);
    }

    public static void WriteVector3(this BinaryWriter writer, Vector3 v)
    {
        writer.Write(v.X);
        writer.Write(v.Y);
        writer.Write(v.Z);
    }

    public static void WriteVector4(this BinaryWriter writer, Vector4 v)
    {
        writer.Write(v.X);
        writer.Write(v.Y);
        writer.Write(v.Z);
        writer.Write(v.W);
    }
}
