using System.Buffers.Binary;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Xterm.Tests;

// The page engine of a picture in the overlay slot (upstream HtmlOverlayRegistry image source, #844),
// driven through a scripted web view.
public sealed class HtmlImagePageTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("agterm-image-page-").FullName;
    private readonly AppStore _store;
    private readonly Session _session;
    private readonly FakeHtmlBrowser _browser = new();
    private readonly HtmlOverlayTheme _theme = new("#101010", "#e0e0e0", dark: true);
    private Func<string, Task<(ImageProbe? Probe, string? Failure)>>? _read;
    private readonly HtmlOverlayRegistry _registry;

    public HtmlImagePageTests()
    {
        _store = new AppStore(persistence: new PersistenceStore(Path.Combine(_dir, "state")),
                              stateDirectory: Path.Combine(_dir, "state"));
        _session = _store.AddSession(_store.AddWorkspace("work").Id, _dir)!;
        _registry = new HtmlOverlayRegistry(new DirectHtmlPageStore(_store), _browser, _ => _theme,
                                            path => _read?.Invoke(path) ?? HtmlOverlayPage.ReadImage(path));
        _store.HtmlOverlayReleased = _registry.Release;
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static byte[] Png(int width, int height)
    {
        var bytes = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }

    private string Picture(byte[] bytes, string name = "shot.png")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private HtmlOverlay Open(string path)
    {
        var overlay = new HtmlOverlay(new HtmlSource.Image(path));
        Assert.Null(_store.OpenHtmlOverlay(_session.Id, overlay, sizePercent: ImagePage.BasePercent));
        return overlay;
    }

    private async Task<(HtmlOverlayPage Page, FakeHtmlPageView View)> Realize(HtmlOverlay overlay)
    {
        var view = new FakeHtmlPageView();
        var page = await _registry.RealizeAsync(overlay, () => Task.FromResult<IHtmlPageView>(view));
        await page.ImageRead;
        return (page, view);
    }

    private static string Address(HtmlOverlay overlay, int load) =>
        $"https://{HtmlOverlayPage.ImageHost(overlay.Id)}/image?{load}";

    [Fact]
    public async Task APictureIsServedFromItsOwnHostToAPageLoadedFromText()
    {
        var bytes = Png(600, 400);
        var path = Picture(bytes);
        var overlay = Open(path);
        var (_, view) = await Realize(overlay);

        Assert.False(view.Javascript);
        Assert.Empty(view.Mapped);
        var served = Assert.Single(view.Served);
        Assert.Equal(HtmlOverlayPage.ImageHost(overlay.Id), served.Host);
        Assert.Equal(bytes, served.Bytes);
        Assert.Equal("image/png", served.MimeType);
        Assert.Equal(["text " + ImagePage.Html(Address(overlay, 1))], view.Calls);
        Assert.NotEqual(HtmlOverlayPage.ImageHost(overlay.Id), HtmlOverlayPage.VirtualHost(overlay.Id));

        Assert.False(view.Start("data:text/html;base64,QQ=="));
        view.Commit("about:blank", null);
        view.Complete();
        Assert.Equal(HtmlLoadState.Loaded, overlay.LoadState);
        Assert.Equal(new HtmlPageInfo(path, null, false, false), overlay.Current);
        Assert.True(view.Start("data:text/html,x"), "only the app's own text load is allowed");
        Assert.True(view.Start("https://example.com/", user: true));
        Assert.Empty(_browser.Prompts);
    }

    [Fact]
    public async Task AFileThatIsNoLongerAPictureFailsTheLoad()
    {
        var path = Picture(Png(600, 400));
        var overlay = Open(path);
        File.WriteAllText(path, "not a picture");

        var (_, view) = await Realize(overlay);

        Assert.Equal(HtmlLoadState.Failed, overlay.LoadState);
        Assert.Equal($"not a png, jpg, gif or webp image: {path}", overlay.LoadError);
        Assert.Empty(view.Served);
        Assert.Empty(view.Calls);
    }

    [Fact]
    public async Task AReloadReadsThePictureAgain()
    {
        var path = Picture(Png(600, 400));
        var overlay = Open(path);
        var (page, view) = await Realize(overlay);
        var changed = Png(300, 200);
        File.WriteAllBytes(path, changed);

        Assert.Null(_registry.Reload(_store, _session.Id, HtmlReloadTarget.Current));
        await page.ImageRead;

        Assert.Equal(changed, view.Served[^1].Bytes);
        Assert.Equal("text " + ImagePage.Html(Address(overlay, 2)), view.Calls[^1]);
    }

    [Fact]
    public async Task AReadThatEndsAfterTheCloseOrANewerLoadIsDropped()
    {
        var path = Picture(Png(600, 400));
        var overlay = Open(path);
        var reads = new List<TaskCompletionSource<(ImageProbe?, string?)>>();
        _read = _ =>
        {
            reads.Add(new TaskCompletionSource<(ImageProbe?, string?)>());
            return reads[^1].Task;
        };
        var view = new FakeHtmlPageView();
        var page = await _registry.RealizeAsync(overlay, () => Task.FromResult<IHtmlPageView>(view));
        Assert.Null(_registry.Reload(_store, _session.Id, HtmlReloadTarget.Original));
        var probe = ImageProbe.Load(path, out _);

        reads[0].SetResult((probe, null));
        Assert.Empty(view.Served);
        reads[1].SetResult((probe, null));
        Assert.Single(view.Served);

        Assert.Null(_registry.Reload(_store, _session.Id, HtmlReloadTarget.Original));
        Assert.True(_store.CloseOverlay(_session.Id));
        reads[2].SetResult((null, "unreadable image: " + path));
        Assert.Single(view.Served);
        Assert.NotEqual(HtmlLoadState.Failed, page.LoadState);
    }

    [Fact]
    public async Task APictureOpensInTheBrowserAsItsFileAndHasNoExplorerOrLinkButtons()
    {
        var path = Picture(Png(600, 400));
        var overlay = Open(path);
        var (page, _) = await Realize(overlay);

        Assert.Null(page.Navigate(HtmlNavigation.Browser));
        Assert.Equal([new Uri(path)], _browser.Opened);
        Assert.Equal(OverlayHtmlError.FinderRequiresFile, page.Navigate(HtmlNavigation.Finder));

        var strip = HtmlOverlayStrip.From(new HtmlOverlay(new HtmlSource.Image(path)) { Navigation = true }, HtmlLoadState.Loaded, null, null);
        Assert.False(strip.Explorer);
        Assert.False(strip.CopyLink);
        Assert.Equal("shot.png", strip.Identity);
        Assert.Equal(path, strip.IdentityTip);
    }
}
