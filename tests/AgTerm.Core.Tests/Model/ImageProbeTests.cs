using System.Buffers.Binary;
using System.Text;
using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

// Reads a picture for an image page (upstream ImageProbe, #844). The port parses the header itself,
// where upstream asks CGImageSource.
public class ImageProbeTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("agt-image-probe-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    internal static byte[] Png(int width, int height)
    {
        var bytes = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }

    private static byte[] Gif(ushort width, ushort height)
    {
        var bytes = new byte[13];
        "GIF89a"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), width);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), height);
        return bytes;
    }

    private static byte[] Segment(byte marker, byte[] body)
    {
        var bytes = new byte[4 + body.Length];
        bytes[0] = 0xFF;
        bytes[1] = marker;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), (ushort)(body.Length + 2));
        body.CopyTo(bytes, 4);
        return bytes;
    }

    private static byte[] Exif(int orientation, bool bigEndian)
    {
        var tiff = new byte[26];
        Encoding.ASCII.GetBytes(bigEndian ? "MM" : "II").CopyTo(tiff, 0);
        void U16(int at, int value)
        {
            if (bigEndian) BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(at), (ushort)value);
            else BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(at), (ushort)value);
        }
        void U32(int at, int value)
        {
            if (bigEndian) BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(at), (uint)value);
            else BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(at), (uint)value);
        }
        U16(2, 42);
        U32(4, 8);
        U16(8, 1);
        U16(10, 0x0112);
        U16(12, 3);
        U32(14, 1);
        U16(18, orientation);
        return [.. "Exif\0\0"u8.ToArray(), .. tiff];
    }

    private static byte[] Jpeg(int width, int height, int? orientation = null, bool bigEndian = false, byte sof = 0xC0)
    {
        var frame = new byte[6];
        frame[0] = 8;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(1), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(3), (ushort)width);
        byte[] exif = orientation is { } o ? Segment(0xE1, Exif(o, bigEndian)) : [];
        return [0xFF, 0xD8, .. Segment(0xE0, "JFIF\0\x01\x01"u8.ToArray()), .. exif,
                .. Segment(0xC4, new byte[4]), .. Segment(sof, frame), 0xFF, 0xDA];
    }

    private static byte[] Riff(string chunk, byte[] data) =>
        [.. "RIFF\0\0\0\0WEBP"u8.ToArray(), .. Encoding.ASCII.GetBytes(chunk), .. new byte[4], .. data];

    private static byte[] WebpLossy(int width, int height)
    {
        var data = new byte[10];
        data[3] = 0x9D;
        data[4] = 0x01;
        data[5] = 0x2A;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), (ushort)height);
        return Riff("VP8 ", data);
    }

    private static byte[] WebpLossless(int width, int height)
    {
        var data = new byte[5];
        data[0] = 0x2F;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(1), (uint)((width - 1) | ((height - 1) << 14)));
        return Riff("VP8L", data);
    }

    private static byte[] WebpExtended(int width, int height)
    {
        var data = new byte[10];
        data[4] = (byte)(width - 1);
        data[5] = (byte)((width - 1) >> 8);
        data[6] = (byte)((width - 1) >> 16);
        data[7] = (byte)(height - 1);
        data[8] = (byte)((height - 1) >> 8);
        data[9] = (byte)((height - 1) >> 16);
        return Riff("VP8X", data);
    }

    public static TheoryData<byte[], int, int, int> Headers() => new()
    {
        { Png(1200, 800), 1200, 800, 1 },
        { Gif(320, 200), 320, 200, 1 },
        { Jpeg(640, 480), 640, 480, 1 },
        { Jpeg(640, 480, orientation: 6), 640, 480, 6 },
        { Jpeg(640, 480, orientation: 8, bigEndian: true), 640, 480, 8 },
        { Jpeg(640, 480, orientation: 0), 640, 480, 1 },
        { Jpeg(640, 480, sof: 0xC2), 640, 480, 1 },
        { WebpLossy(300, 150), 300, 150, 1 },
        { WebpLossless(300, 150), 300, 150, 1 },
        { WebpExtended(70000, 3), 70000, 3, 1 },
    };

    [Theory]
    [MemberData(nameof(Headers))]
    public void TheHeaderGivesThePixelSizeAndOrientation(byte[] bytes, int width, int height, int orientation)
    {
        var path = Write("picture", bytes);
        var probe = ImageProbe.Load(path, out var failure);
        Assert.Null(failure);
        Assert.NotNull(probe);
        Assert.Equal((width, height, orientation), (probe.PixelWidth, probe.PixelHeight, probe.Orientation));
        Assert.Equal(bytes, probe.Bytes);
        Assert.Equal(ImagePage.MimeType(bytes), probe.MimeType);
    }

    public static TheoryData<byte[]> NotImages() => new()
    {
        Encoding.UTF8.GetBytes("<html><body>not a picture</body></html>"),
        Png(1200, 800)[..20],
        Png(0, 800),
        Jpeg(640, 480)[..^14],
        new byte[] { 0xFF, 0xD8, 0xFF, 0xDA },
        Riff("VP8 ", new byte[10]),
        Riff("ALPH", new byte[10]),
        Array.Empty<byte>(),
    };

    [Theory]
    [MemberData(nameof(NotImages))]
    public void AFileWithoutAReadableHeaderIsNotAnImage(byte[] bytes)
    {
        var path = Write("fake.png", bytes);
        Assert.Null(ImageProbe.Load(path, out var failure));
        Assert.Equal("not a png, jpg, gif or webp image: " + path, failure);
    }

    [Fact]
    public void APictureOverTheMaxPixelsIsTooLarge()
    {
        var path = Write("bomb.png", Png(12_000, 12_000));
        Assert.Null(ImageProbe.Load(path, out var failure));
        Assert.Equal("image too large (over 100 megapixels): " + path, failure);
    }

    [Fact]
    public void AFileOverTheByteCapIsRefusedBeforeARead()
    {
        var path = Write("big.png", Png(10, 10));
        Assert.Null(ImageProbe.Load(path, out var failure, maxBytes: 32));
        Assert.Equal("image too large (over 64 MB): " + path, failure);
    }

    [Fact]
    public void AMissingFileOrADirectoryIsUnreadable()
    {
        var missing = Path.Combine(_dir, "missing.png");
        Assert.Null(ImageProbe.Load(missing, out var failure));
        Assert.Equal("unreadable image: " + missing, failure);
        Assert.Null(ImageProbe.Load(_dir, out failure));
        Assert.Equal("unreadable image: " + _dir, failure);
    }

    [Fact]
    public void NaturalUsesTheProbedRotation()
    {
        var probe = ImageProbe.Load(Write("turned.jpg", Jpeg(1200, 800, orientation: 6)), out _);
        Assert.Equal((400.0, 600.0), probe!.Natural(2));
    }

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
