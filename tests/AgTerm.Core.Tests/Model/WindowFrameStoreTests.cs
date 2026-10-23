using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

public sealed class WindowFrameStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("agterm-window-frames-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void A_saved_frame_loads_back_for_its_window_only()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var store = new WindowFrameStore(_dir);

        store.Save(first, new SavedWindowFrame(new PixelRect(-1200, 40, 1000, 700), Maximized: true));
        store.Save(second, new SavedWindowFrame(new PixelRect(100, 80, 640, 480), Maximized: false));

        var reloaded = new WindowFrameStore(_dir);
        Assert.Equal(new SavedWindowFrame(new PixelRect(-1200, 40, 1000, 700), true), reloaded.Load(first));
        Assert.Equal(new SavedWindowFrame(new PixelRect(100, 80, 640, 480), false), reloaded.Load(second));
        Assert.Null(reloaded.Load(Guid.NewGuid()));
    }

    [Fact]
    public void Saving_a_window_again_replaces_its_frame()
    {
        var id = Guid.NewGuid();
        var store = new WindowFrameStore(_dir);

        store.Save(id, new SavedWindowFrame(new PixelRect(0, 0, 800, 600), false));
        store.Save(id, new SavedWindowFrame(new PixelRect(10, 20, 900, 650), true));

        Assert.Equal(new SavedWindowFrame(new PixelRect(10, 20, 900, 650), true), store.Load(id));
    }

    [Fact]
    public void Frames_live_in_their_own_file_in_the_state_directory()
    {
        new WindowFrameStore(_dir).Save(Guid.NewGuid(), new SavedWindowFrame(new PixelRect(0, 0, 800, 600), false));

        Assert.True(File.Exists(Path.Combine(_dir, "window-frames.json")));
        Assert.False(File.Exists(Path.Combine(_dir, "windows.json")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"version\":2,\"windows\":{}}")]
    [InlineData("{\"version\":1,\"windows\":{\"x\":{\"x\":0,\"y\":0,\"width\":0,\"height\":600}}}")]
    public void A_missing_unreadable_or_empty_frame_loads_as_none(string content)
    {
        File.WriteAllText(Path.Combine(_dir, "window-frames.json"), content);

        Assert.Null(new WindowFrameStore(_dir).Load(Guid.NewGuid()));
        Assert.Null(new WindowFrameStore(Path.Combine(_dir, "missing")).Load(Guid.NewGuid()));
    }

    [Fact]
    public void A_frame_with_no_area_is_not_returned()
    {
        var id = Guid.NewGuid();
        File.WriteAllText(
            Path.Combine(_dir, "window-frames.json"),
            $"{{\"version\":1,\"windows\":{{\"{id.ToString("D").ToUpperInvariant()}\":{{\"x\":0,\"y\":0,\"width\":0,\"height\":600,\"maximized\":false}}}}}}");

        Assert.Null(new WindowFrameStore(_dir).Load(id));
    }

    [Fact]
    public void Saving_over_an_unreadable_file_starts_a_fresh_one()
    {
        File.WriteAllText(Path.Combine(_dir, "window-frames.json"), "not json");
        var id = Guid.NewGuid();
        var store = new WindowFrameStore(_dir);

        store.Save(id, new SavedWindowFrame(new PixelRect(0, 0, 800, 600), false));

        Assert.Equal(new SavedWindowFrame(new PixelRect(0, 0, 800, 600), false), store.Load(id));
    }
}
