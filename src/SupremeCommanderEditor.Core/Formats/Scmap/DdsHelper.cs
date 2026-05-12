namespace SupremeCommanderEditor.Core.Formats.Scmap;

/// <summary>
/// Helper for reading/writing DDS data as embedded in .scmap files.
/// The .scmap format stores DDS images as: int32 length + raw DDS bytes (with 128-byte header).
/// </summary>
public static class DdsHelper
{
    public const int DdsHeaderSize = 128;
    public const uint DdsMagic = 0x20534444; // "DDS "

    // DDS header flags
    private const uint DdsdCaps = 0x1;
    private const uint DdsdHeight = 0x2;
    private const uint DdsdWidth = 0x4;
    private const uint DdsdPixelFormat = 0x1000;
    private const uint DdsdLinearSize = 0x80000;
    private const uint DdsdPitch = 0x8;

    // Pixel format flags
    private const uint DdpfAlphaPixels = 0x1;
    private const uint DdpfFourCc = 0x4;
    private const uint DdpfRgb = 0x40;

    // FourCC codes
    private const uint FourCcDxt5 = 0x35545844; // "DXT5"

    // Caps
    private const uint DdsCapsTexture = 0x1000;

    /// <summary>
    /// Read a length-prefixed DDS blob from the stream.
    /// Returns the complete DDS data including header.
    /// </summary>
    public static byte[] ReadDdsBlob(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        return reader.ReadBytes(length);
    }

    /// <summary>
    /// Write a length-prefixed DDS blob to the stream.
    /// </summary>
    public static void WriteDdsBlob(BinaryWriter writer, byte[] ddsData)
    {
        writer.Write(ddsData.Length);
        writer.Write(ddsData);
    }

    /// <summary>
    /// Create a DDS header + pixel data for an uncompressed ARGB image.
    /// </summary>
    public static byte[] CreateArgbDds(int width, int height, byte[] pixelData)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // DDS Magic
        bw.Write(DdsMagic);

        // DDS_HEADER (124 bytes)
        bw.Write(124); // dwSize
        bw.Write(DdsdCaps | DdsdHeight | DdsdWidth | DdsdPixelFormat | DdsdPitch); // dwFlags
        bw.Write(height); // dwHeight
        bw.Write(width); // dwWidth
        bw.Write(width * 4); // dwPitchOrLinearSize (pitch = width * 4 bytes per pixel)
        bw.Write(0); // dwDepth
        bw.Write(0); // dwMipMapCount
        for (int i = 0; i < 11; i++) bw.Write(0); // dwReserved1[11]

        // DDS_PIXELFORMAT (32 bytes)
        bw.Write(32); // dwSize
        bw.Write(DdpfRgb | DdpfAlphaPixels); // dwFlags
        bw.Write(0); // dwFourCC
        bw.Write(32); // dwRGBBitCount
        bw.Write(0x00FF0000); // dwRBitMask
        bw.Write(0x0000FF00); // dwGBitMask
        bw.Write(0x000000FF); // dwBBitMask
        bw.Write(unchecked((int)0xFF000000)); // dwABitMask

        bw.Write(DdsCapsTexture); // dwCaps
        bw.Write(0); // dwCaps2
        bw.Write(0); // dwCaps3
        bw.Write(0); // dwCaps4
        bw.Write(0); // dwReserved2

        // Pixel data
        bw.Write(pixelData);

        return ms.ToArray();
    }

    /// <summary>
    /// Create a DDS header + data for a DXT5 compressed image.
    /// </summary>
    public static byte[] CreateDxt5Dds(int width, int height, byte[] compressedData)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        int linearSize = Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 16;

        // DDS Magic
        bw.Write(DdsMagic);

        // DDS_HEADER (124 bytes)
        bw.Write(124); // dwSize
        bw.Write(DdsdCaps | DdsdHeight | DdsdWidth | DdsdPixelFormat | DdsdLinearSize); // dwFlags
        bw.Write(height); // dwHeight
        bw.Write(width); // dwWidth
        bw.Write(linearSize); // dwPitchOrLinearSize
        bw.Write(0); // dwDepth
        bw.Write(0); // dwMipMapCount
        for (int i = 0; i < 11; i++) bw.Write(0); // dwReserved1[11]

        // DDS_PIXELFORMAT (32 bytes)
        bw.Write(32); // dwSize
        bw.Write(DdpfFourCc); // dwFlags
        bw.Write(FourCcDxt5); // dwFourCC
        bw.Write(0); // dwRGBBitCount
        bw.Write(0); // dwRBitMask
        bw.Write(0); // dwGBitMask
        bw.Write(0); // dwBBitMask
        bw.Write(0); // dwABitMask

        bw.Write(DdsCapsTexture); // dwCaps
        bw.Write(0); // dwCaps2
        bw.Write(0); // dwCaps3
        bw.Write(0); // dwCaps4
        bw.Write(0); // dwReserved2

        // Compressed data
        bw.Write(compressedData);

        return ms.ToArray();
    }

    /// <summary>
    /// Extract width and height from a DDS header.
    /// </summary>
    public static (int Width, int Height) GetDdsDimensions(byte[] ddsData)
    {
        if (ddsData.Length < DdsHeaderSize)
            throw new InvalidDataException("DDS data too small for header");

        int height = BitConverter.ToInt32(ddsData, 12);
        int width = BitConverter.ToInt32(ddsData, 16);
        return (width, height);
    }
}
