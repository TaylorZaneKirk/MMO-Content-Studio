// Small lossless pixel adapter for wind masks and existing RGB/RGBA tile art.
// BrowserPngAssets validates PNG framing/CRC; this adapter owns pixel decoding only.
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using MMO.ContentStudio.AuthoringHost.Browser;
namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed record FoliagePixels(int Width, int Height, byte[] Rgba);
public static class FoliagePng
{
    public static FoliagePixels Decode(byte[] png)
    {
        if (!BrowserPngAssets.ValidPng(png)) throw new TileEditException("Invalid foliage PNG.");
        int width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        int channels = png[25] == 6 ? 4 : png[25] == 2 ? 3 : 0;
        if (width > 1024 || height > 1024 || png[24] != 8 || png[28] != 0 || channels == 0)
            throw new TileEditException("Wind editing supports non-interlaced 8-bit RGB/RGBA PNGs up to 1024 pixels per side. Convert a copy of other source art first.");
        using var packed = new MemoryStream();
        for (int offset = 8; offset < png.Length;)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            if (Encoding.ASCII.GetString(png, offset + 4, 4) == "tRNS") throw new TileEditException("Convert color-key transparency to RGBA before wind editing.");
            if (Encoding.ASCII.GetString(png, offset + 4, 4) == "IDAT") packed.Write(png, offset + 8, length);
            offset += length + 12;
        }
        packed.Position = 0;
        using var stream = new ZLibStream(packed, CompressionMode.Decompress);
        int stride = width * channels;
        var pixels = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            int filter = stream.ReadByte();
            var row = pixels.AsSpan(y * stride, stride);
            stream.ReadExactly(row);
            for (int x = 0; x < stride; x++)
            {
                int left = x >= channels ? row[x - channels] : 0;
                int up = y > 0 ? pixels[(y - 1) * stride + x] : 0;
                int corner = y > 0 && x >= channels ? pixels[(y - 1) * stride + x - channels] : 0;
                int predictor = filter switch { 0 => 0, 1 => left, 2 => up, 3 => (left + up) / 2, 4 => Paeth(left, up, corner), _ => throw new TileEditException("Invalid PNG row.") };
                row[x] = unchecked((byte)(row[x] + predictor));
            }
        }
        var rgba = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            pixels.AsSpan(i * channels, 3).CopyTo(rgba.AsSpan(i * 4));
            rgba[i * 4 + 3] = channels == 4 ? pixels[i * 4 + 3] : (byte)255;
        }
        return new(width, height, rgba);
    }
    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, da = Math.Abs(p - a), db = Math.Abs(p - b), dc = Math.Abs(p - c);
        return da <= db && da <= dc ? a : db <= dc ? b : c;
    }
    public static FoliagePixels Crop(FoliagePixels image, int x, int y, int width, int height)
    {
        if (x < 0 || y < 0 || width < 1 || height < 1 || x + width > image.Width || y + height > image.Height)
            throw new TileEditException("Tile crop exceeds the image.");
        var result = new byte[width * height * 4];
        for (int row = 0; row < height; row++) image.Rgba.AsSpan(((y + row) * image.Width + x) * 4, width * 4).CopyTo(result.AsSpan(row * width * 4));
        return new(width, height, result);
    }
    public static byte[] Encode(FoliagePixels image)
    {
        using var result = new MemoryStream();
        result.Write(new byte[] {137,80,78,71,13,10,26,10});
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0,4), image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4,4), image.Height);
        header[8] = 8; header[9] = 6; Chunk(result, "IHDR", header);
        using var packed = new MemoryStream();
        using (var zlib = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen:true))
            for (int row = 0; row < image.Height; row++) { zlib.WriteByte(0); zlib.Write(image.Rgba, row * image.Width * 4, image.Width * 4); }
        Chunk(result, "IDAT", packed.ToArray()); Chunk(result, "IEND", []);
        return result.ToArray();
    }
    private static void Chunk(Stream output, string type, byte[] bytes)
    {
        var data = Encoding.ASCII.GetBytes(type).Concat(bytes).ToArray();
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, bytes.Length); output.Write(number); output.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(number, BrowserPngAssets.Crc(data)); output.Write(number);
    }
}
