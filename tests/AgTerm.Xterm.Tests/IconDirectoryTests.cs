using AgTerm.App;

namespace AgTerm.Xterm.Tests;

public sealed class IconDirectoryTests
{
    [Fact]
    public void The_exact_size_is_taken()
    {
        var ico = Ico(16, 24, 32);

        var frame = IconDirectory.FrameFor(ico, 24);

        Assert.NotNull(frame);
        Assert.Equal(24, frame.Value.Size);
        Assert.Equal(ImageOf(ico, 1), ico.AsSpan(frame.Value.Offset, frame.Value.Length).ToArray());
    }

    [Fact]
    public void A_size_between_frames_takes_the_next_larger_frame()
    {
        Assert.Equal(32, IconDirectory.FrameFor(Ico(16, 32, 48), 20)?.Size);
    }

    [Fact]
    public void A_size_above_every_frame_takes_the_largest_frame()
    {
        Assert.Equal(48, IconDirectory.FrameFor(Ico(48, 16, 32), 64)?.Size);
    }

    [Fact]
    public void A_width_byte_of_zero_is_256()
    {
        Assert.Equal(256, IconDirectory.FrameFor(Ico(32, 256), 200)?.Size);
    }

    [Fact]
    public void Malformed_data_gives_no_frame()
    {
        var ico = Ico(16, 32);
        Assert.Null(IconDirectory.FrameFor(ico.AsSpan(0, 5), 16));

        var cursor = (byte[])ico.Clone();
        cursor[2] = 2;
        Assert.Null(IconDirectory.FrameFor(cursor, 16));

        var shortDirectory = ico.AsSpan(0, 6 + 16).ToArray();
        Assert.Null(IconDirectory.FrameFor(shortDirectory, 16));

        var pastTheEnd = (byte[])ico.Clone();
        BitConverter.TryWriteBytes(pastTheEnd.AsSpan(6 + 8), ico.Length);
        Assert.Null(IconDirectory.FrameFor(pastTheEnd, 16));
    }

    [Theory]
    [InlineData("AppIcon.ico")]
    [InlineData("AppIcon-dark-taskbar.ico")]
    public void The_app_icons_hold_every_size_windows_asks_for(string file)
    {
        var ico = File.ReadAllBytes(Path.Combine(FindRepoRoot(), "src", "AgTerm.App", "Assets", file));

        foreach (var size in new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 })
            Assert.Equal(size, IconDirectory.FrameFor(ico, size)?.Size);
    }

    // each image is filler bytes derived from its size, so a test can tell which image a frame points at
    private static byte[] Ico(params int[] sizes)
    {
        var images = sizes.Select(size => Enumerable.Repeat((byte)(size % 251), 10 + size % 7).ToArray()).ToArray();
        var data = new List<byte>();
        data.AddRange(BitConverter.GetBytes((ushort)0));
        data.AddRange(BitConverter.GetBytes((ushort)1));
        data.AddRange(BitConverter.GetBytes((ushort)sizes.Length));
        var offset = 6 + 16 * sizes.Length;
        for (var i = 0; i < sizes.Length; i++)
        {
            var side = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
            data.AddRange(new byte[] { side, side, 0, 0 });
            data.AddRange(BitConverter.GetBytes((ushort)1));
            data.AddRange(BitConverter.GetBytes((ushort)32));
            data.AddRange(BitConverter.GetBytes(images[i].Length));
            data.AddRange(BitConverter.GetBytes(offset));
            offset += images[i].Length;
        }
        foreach (var image in images)
            data.AddRange(image);
        return data.ToArray();
    }

    private static byte[] ImageOf(byte[] ico, int index)
    {
        var entry = 6 + 16 * index;
        var length = BitConverter.ToInt32(ico, entry + 8);
        var offset = BitConverter.ToInt32(ico, entry + 12);
        return ico.AsSpan(offset, length).ToArray();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AgTerm.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("AgTerm.slnx not found above the test output");
    }
}
