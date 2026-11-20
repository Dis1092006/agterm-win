using System.Buffers.Binary;
using Microsoft.Win32.SafeHandles;

namespace AgTerm.Core.Model;

/// <summary>A picture read for an image page (upstream ImageProbe, #844): its bytes, its format by
/// signature, and its pixel size and EXIF orientation from the header alone. A clicked path comes from
/// untrusted terminal output, so the file is opened once and judged by what that handle names: a link
/// is measured at its target, and a directory, device, cloud placeholder or file over the byte cap is
/// refused before a read. Upstream reads the header with CGImageSource; the port parses it itself.</summary>
public sealed record ImageProbe(int PixelWidth, int PixelHeight, int Orientation, string MimeType, byte[] Bytes)
{
    // FILE_ATTRIBUTE_RECALL_ON_OPEN and FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS: a placeholder whose data
    // lives in the cloud, which a read would download
    private const FileAttributes Placeholder = FileAttributes.Offline | (FileAttributes)0x40000 | (FileAttributes)0x400000;

    /// <summary>The picture's drawn size in points on a display of scale.</summary>
    public (double Width, double Height) Natural(double scale) =>
        ImagePage.Natural(PixelWidth, PixelHeight, Orientation, scale);

    /// <summary>Reads the file at path and probes the bytes it read, or gives why it cannot. A file over
    /// maxBytes is refused before a read.</summary>
    public static ImageProbe? Load(string path, out string? failure, long maxBytes = ImagePage.MaxBytes)
    {
        failure = null;
        byte[] bytes;
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!IsRegularFile(handle))
            {
                failure = Unreadable(path);
                return null;
            }
            var length = RandomAccess.GetLength(handle);
            if (length > maxBytes)
            {
                failure = $"image too large (over {ImagePage.MaxBytes >> 20} MB): {path}";
                return null;
            }
            bytes = new byte[length];
            var filled = 0;
            while (filled < bytes.Length)
            {
                var count = RandomAccess.Read(handle, bytes.AsSpan(filled), filled);
                if (count <= 0) break;
                filled += count;
            }
            if (filled != bytes.Length)
            {
                failure = Unreadable(path);
                return null;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            failure = Unreadable(path);
            return null;
        }
        if (ImagePage.MimeType(bytes) is not { } mime || Header(bytes, mime) is not { } header
            || header.Width <= 0 || header.Height <= 0)
        {
            failure = $"not a png, jpg, gif or webp image: {path}";
            return null;
        }
        if (!ImagePage.AllowsPixels(header.Width, header.Height))
        {
            failure = $"image too large (over {ImagePage.MaxPixels / 1_000_000} megapixels): {path}";
            return null;
        }
        return new ImageProbe(header.Width, header.Height, header.Orientation, mime, bytes);
    }

    private static string Unreadable(string path) => $"unreadable image: {path}";

    // a pipe or a character device fails RandomAccess, which takes only seekable files
    private static bool IsRegularFile(SafeFileHandle handle) =>
        (File.GetAttributes(handle) & (FileAttributes.Directory | FileAttributes.Device | Placeholder)) == 0;

    private static (int Width, int Height, int Orientation)? Header(ReadOnlySpan<byte> data, string mime) => mime switch
    {
        "image/png" => data.Length >= 24 && data[12..16].SequenceEqual("IHDR"u8)
            ? (BinaryPrimitives.ReadInt32BigEndian(data[16..]), BinaryPrimitives.ReadInt32BigEndian(data[20..]), 1)
            : null,
        "image/gif" => data.Length >= 10
            ? (BinaryPrimitives.ReadUInt16LittleEndian(data[6..]), BinaryPrimitives.ReadUInt16LittleEndian(data[8..]), 1)
            : null,
        "image/jpeg" => Jpeg(data),
        "image/webp" => Webp(data),
        _ => null,
    };

    // walks the segments to the first frame header; the orientation comes from an Exif segment before it
    private static (int, int, int)? Jpeg(ReadOnlySpan<byte> data)
    {
        var orientation = 1;
        var at = 2;
        while (at + 1 < data.Length)
        {
            if (data[at] != 0xFF) return null;
            var marker = data[at + 1];
            if (marker == 0xFF)
            {
                at++;
                continue;
            }
            at += 2;
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7)) continue;
            if (marker is 0xD9 or 0xDA) return null;
            if (at + 2 > data.Length) return null;
            var length = BinaryPrimitives.ReadUInt16BigEndian(data[at..]);
            if (length < 2 || at + length > data.Length) return null;
            var body = data.Slice(at + 2, length - 2);
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                if (body.Length < 5) return null;
                return (BinaryPrimitives.ReadUInt16BigEndian(body[3..]), BinaryPrimitives.ReadUInt16BigEndian(body[1..]),
                        orientation);
            }
            if (marker == 0xE1 && body.StartsWith("Exif\0\0"u8)) orientation = ExifOrientation(body[6..]) ?? orientation;
            at += length;
        }
        return null;
    }

    private static int? ExifOrientation(ReadOnlySpan<byte> tiff)
    {
        if (tiff.Length < 8) return null;
        bool big;
        if (tiff.StartsWith("MM"u8)) big = true;
        else if (tiff.StartsWith("II"u8)) big = false;
        else return null;
        int U16(ReadOnlySpan<byte> s) => big ? BinaryPrimitives.ReadUInt16BigEndian(s) : BinaryPrimitives.ReadUInt16LittleEndian(s);
        long U32(ReadOnlySpan<byte> s) => big ? BinaryPrimitives.ReadUInt32BigEndian(s) : BinaryPrimitives.ReadUInt32LittleEndian(s);
        if (U16(tiff[2..]) != 42) return null;
        var ifd = U32(tiff[4..]);
        if (ifd + 2 > tiff.Length) return null;
        var count = U16(tiff[(int)ifd..]);
        for (var i = 0; i < count; i++)
        {
            var entry = (int)ifd + 2 + i * 12;
            if (entry + 12 > tiff.Length) return null;
            if (U16(tiff[entry..]) != 0x0112) continue;
            var value = U16(tiff[(entry + 8)..]);
            return value is >= 1 and <= 8 ? value : null;
        }
        return null;
    }

    // the first chunk after the RIFF header names the encoding: lossy, lossless or extended
    private static (int, int, int)? Webp(ReadOnlySpan<byte> data)
    {
        if (data.Length < 20) return null;
        var chunk = data[12..16];
        var body = data[20..];
        if (chunk.SequenceEqual("VP8 "u8))
        {
            if (body.Length < 10 || body[3] != 0x9D || body[4] != 0x01 || body[5] != 0x2A) return null;
            return (BinaryPrimitives.ReadUInt16LittleEndian(body[6..]) & 0x3FFF,
                    BinaryPrimitives.ReadUInt16LittleEndian(body[8..]) & 0x3FFF, 1);
        }
        if (chunk.SequenceEqual("VP8L"u8))
        {
            if (body.Length < 5 || body[0] != 0x2F) return null;
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(body[1..]);
            return ((int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1, 1);
        }
        if (chunk.SequenceEqual("VP8X"u8))
        {
            if (body.Length < 10) return null;
            return ((body[4] | body[5] << 8 | body[6] << 16) + 1, (body[7] | body[8] << 8 | body[9] << 16) + 1, 1);
        }
        return null;
    }
}
