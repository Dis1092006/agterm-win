using System.Buffers.Binary;

namespace AgTerm.App;

/// <summary>One image inside an .ico file: its side in pixels and where its bytes lie.</summary>
public readonly record struct IconFrame(int Size, int Offset, int Length);

/// <summary>Reads the directory of an .ico file, the format <c>tools/app_icon.py</c> writes.</summary>
public static class IconDirectory
{
    private const int HeaderLength = 6;
    private const int EntryLength = 16;

    /// <summary>The frame Windows would draw at <paramref name="size"/>: the exact size, else the smallest
    /// larger frame, else the largest frame. Null when the data is not a well-formed icon.</summary>
    public static IconFrame? FrameFor(ReadOnlySpan<byte> ico, int size)
    {
        var frames = Frames(ico);
        if (frames is null || frames.Count == 0)
            return null;

        var larger = frames.Where(f => f.Size >= size).OrderBy(f => f.Size).ToList();
        return larger.Count > 0 ? larger[0] : frames.MaxBy(f => f.Size);
    }

    private static List<IconFrame>? Frames(ReadOnlySpan<byte> ico)
    {
        if (ico.Length < HeaderLength
            || BinaryPrimitives.ReadUInt16LittleEndian(ico) != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(ico[2..]) != 1)
            return null;

        var count = BinaryPrimitives.ReadUInt16LittleEndian(ico[4..]);
        if (ico.Length < HeaderLength + EntryLength * count)
            return null;

        var frames = new List<IconFrame>(count);
        for (var i = 0; i < count; i++)
        {
            var entry = ico.Slice(HeaderLength + EntryLength * i, EntryLength);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            if ((ulong)offset + length > (ulong)ico.Length)
                return null;
            // the entry has one byte for the width, so 256 is written as 0
            var size = entry[0] == 0 ? 256 : entry[0];
            frames.Add(new IconFrame(size, (int)offset, (int)length));
        }
        return frames;
    }
}
