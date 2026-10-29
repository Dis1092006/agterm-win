using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

public sealed class CloseSessionShortcutTests : IDisposable
{
    private readonly string _stateDir = Directory.CreateTempSubdirectory("agterm-close-shortcut-").FullName;
    private readonly HeadlessHost _host;
    private readonly LiveStoreCoordinator _coordinator;
    private readonly CloseSessionShortcut _shortcut;
    private int _closed;

    public CloseSessionShortcutTests()
    {
        _host = new HeadlessHost(new HeadlessHostOptions
        {
            StateDir = _stateDir,
            PipeName = "agterm-close-shortcut-" + Guid.NewGuid().ToString("N"),
            HomeDirectory = _stateDir,
        });
        _coordinator = new LiveStoreCoordinator(_host, isUiThread: () => false);
        _shortcut = new CloseSessionShortcut(_coordinator, _host.Actions);
    }

    public void Dispose()
    {
        _coordinator.Dispose();
        _host.Dispose();
        Directory.Delete(_stateDir, recursive: true);
    }

    private Task PressAsync() => _shortcut.ExecuteAsync(id =>
    {
        _closed++;
        return _coordinator.MutateAsync(store => store.CloseSession(id));
    });

    private Task<T> ReadAsync<T>(Func<AppStore, T> read) => _coordinator.ReadAsync(read);

    private Task MutateAsync(Action<AppStore> action) => _coordinator.MutateAsync(action);

    private async Task<Guid> ActiveIdAsync() =>
        await ReadAsync(store => store.ActiveSession!.Id);

    private async Task AssertSessionSurvivedAsync(Guid id)
    {
        Assert.True(await ReadAsync(store => store.SessionWithId(id) is not null));
        Assert.Equal(0, _closed);
    }

    // Ported from upstream ControlOverlaySplitUITests.swift testCloseSessionShortcutHidesScratchInsteadOfClosingSession
    [Fact]
    public async Task CloseSessionShortcutHidesScratchInsteadOfClosingSession()
    {
        var id = await ActiveIdAsync();
        await MutateAsync(store => store.ToggleScratch(id));

        await PressAsync();

        Assert.False(await ReadAsync(store => store.SessionWithId(id)!.ScratchActive));
        await AssertSessionSurvivedAsync(id);
    }

    // Ported from upstream ControlOverlaySplitUITests.swift testCloseSessionShortcutClosesOverlayInsteadOfClosingSession
    [Fact]
    public async Task CloseSessionShortcutClosesOverlayInsteadOfClosingSession()
    {
        var id = await ActiveIdAsync();
        await MutateAsync(store => Assert.True(store.OpenOverlay(id, "cat")));

        await PressAsync();

        Assert.False(await ReadAsync(store => store.SessionWithId(id)!.OverlayActive));
        await AssertSessionSurvivedAsync(id);
    }

    // Ported from upstream ControlOverlaySplitUITests.swift testCloseSessionShortcutClosesFloatingOverlayInsteadOfClosingSession
    [Fact]
    public async Task CloseSessionShortcutClosesFloatingOverlayInsteadOfClosingSession()
    {
        var id = await ActiveIdAsync();
        await MutateAsync(store => Assert.True(store.OpenOverlay(id, "cat", sizePercent: 70)));

        await PressAsync();

        Assert.False(await ReadAsync(store => store.SessionWithId(id)!.OverlayActive));
        await AssertSessionSurvivedAsync(id);
    }

    // Ported from upstream ControlOverlaySplitUITests.swift testCloseSessionShortcutPeelsStackedCoversInPrecedenceOrder
    [Fact]
    public async Task CloseSessionShortcutPeelsStackedCoversInPrecedenceOrder()
    {
        var id = await ActiveIdAsync();
        await MutateAsync(store =>
        {
            store.ToggleScratch(id);
            Assert.True(store.OpenOverlay(id, "cat"));
        });

        await PressAsync();
        Assert.Equal((false, true), await ReadAsync(store =>
            (store.SessionWithId(id)!.OverlayActive, store.SessionWithId(id)!.ScratchActive)));
        await AssertSessionSurvivedAsync(id);

        await PressAsync();
        Assert.Equal((false, false), await ReadAsync(store =>
            (store.SessionWithId(id)!.OverlayActive, store.SessionWithId(id)!.ScratchActive)));
        await AssertSessionSurvivedAsync(id);

        await PressAsync();
        Assert.Equal(1, _closed);
        Assert.Null(await ReadAsync(store => store.SessionWithId(id)));
    }

    [Fact]
    public async Task CloseSessionShortcutPeelsWindowCoversBeforeSessionCovers()
    {
        var id = await ActiveIdAsync();
        await MutateAsync(store =>
        {
            store.ToggleScratch(id);
            Assert.True(store.OpenOverlay(id, "cat"));
        });
        await MutateAsync(_ =>
        {
            Assert.True(_host.Actions.SetQuickTerminal("show").Ok);
            Assert.True(_host.Actions.SetSurfaceZoom("active", null, ControlToggleMode.On).Ok);
            _host.Actions.Quick.SetKeyOwnership(true);
            Assert.True(_host.Actions.SetSurfaceZoom("quick", null, ControlToggleMode.On).Ok);
        });

        await PressAsync();
        Assert.True(_host.Actions.Quick.Visible);
        Assert.False(_host.Actions.Quick.IsZoomed);
        Assert.NotNull(_host.Actions.Zoom.Target);

        await PressAsync();
        Assert.False(_host.Actions.Quick.Visible);
        Assert.True(await ReadAsync(store => store.SessionWithId(id)!.OverlayActive));
        await AssertSessionSurvivedAsync(id);
    }

    [Fact]
    public async Task CloseSessionShortcutDismissesQuickWithoutAnActiveSession()
    {
        var id = await ActiveIdAsync();
        await MutateAsync(store => store.CloseSession(id));
        await MutateAsync(_ =>
        {
            Assert.True(_host.Actions.SetQuickTerminal("show").Ok);
            _host.Actions.Quick.SetKeyOwnership(true);
        });

        await PressAsync();

        Assert.False(_host.Actions.Quick.Visible);
        Assert.Equal(0, _closed);
    }

    [Fact]
    public async Task UnfocusedPinnedQuickDoesNotTakeTheNormalWindowsCloseShortcut()
    {
        await MutateAsync(_ => _host.Actions.SetQuickTerminal("show"));
        Assert.False(_host.Actions.Quick.HoldsKey);
        await PressAsync();
        Assert.True(_host.Actions.Quick.Visible);
        Assert.Equal(1, _closed);
    }

    [Fact]
    public async Task CloseSessionShortcutDismissesDashboardBeforeOverlay()
    {
        var id = await ActiveIdAsync();
        await MutateAsync(store => Assert.True(store.OpenOverlay(id, "cat")));
        await MutateAsync(_ => Assert.True(_host.Actions.SetDashboard(
            Array.Empty<string>(), null, close: false, new DashboardFontMode.Auto(), mru: true).Ok));

        var focused = 0;
        await _shortcut.ExecuteAsync(_ => throw new InvalidOperationException("Session was closed"),
            focusActiveSession: () => focused++);

        Assert.False(_host.Actions.Dashboard.IsOpen);
        Assert.Equal(1, focused);
        Assert.True(await ReadAsync(store => store.SessionWithId(id)!.OverlayActive));
        await AssertSessionSurvivedAsync(id);
    }

    [Fact]
    public async Task CloseSessionShortcutDismissesHudBeforeSessionClose()
    {
        var id = await ActiveIdAsync();
        await MutateAsync(store =>
        {
            store.ToggleScratch(id);
            Assert.True(store.OpenHud(id, new HudSpec("message"), null));
        });

        await PressAsync();

        Assert.False(await ReadAsync(store => store.SessionWithId(id)!.HudActive));
        Assert.True(await ReadAsync(store => store.SessionWithId(id)!.ScratchActive));
        await AssertSessionSurvivedAsync(id);

        await PressAsync();
        Assert.False(await ReadAsync(store => store.SessionWithId(id)!.ScratchActive));
        await AssertSessionSurvivedAsync(id);
    }

    [Fact]
    public async Task CloseSessionShortcutEscapesFocusedAskBeforeSessionCover()
    {
        var id = await ActiveIdAsync();
        await MutateAsync(store => store.ToggleScratch(id));
        await MutateAsync(_ =>
        {
            Assert.True(_host.Actions.SetQuickTerminal("show").Ok);
            Assert.True(_host.Actions.SetSurfaceZoom("active", null, ControlToggleMode.On).Ok);
        });
        var escaped = 0;

        await _shortcut.ExecuteAsync(_ => throw new InvalidOperationException("Session was closed"),
            () => { escaped++; return true; });

        Assert.Equal(1, escaped);
        Assert.True(await ReadAsync(store => store.SessionWithId(id)!.ScratchActive));
        Assert.True(_host.Actions.Quick.Visible);
        Assert.NotNull(_host.Actions.Zoom.Target);
        await AssertSessionSurvivedAsync(id);
    }

    [Fact]
    public async Task CloseSessionShortcutResolvesPendingPickBeforeZoom()
    {
        var id = "pick-" + Guid.NewGuid().ToString("N");
        await MutateAsync(_ => Assert.True(_host.Actions.SetSurfaceZoom("active", null, ControlToggleMode.On).Ok));
        Assert.True(_host.Actions.Pick.Open(new PendingPick(id,
            Array.Empty<ControlPickItem>(), null, null, false, null)));

        await PressAsync();

        Assert.Null(_host.Actions.Pick.Pending);
        Assert.NotNull(_host.Actions.Zoom.Target);
        Assert.Equal(0, _closed);
    }

    [Fact]
    public async Task CloseSessionShortcutEscapesGuiAskBeforeQuickTerminal()
    {
        var id = "ask-" + Guid.NewGuid().ToString("N");
        var ask = new PendingAsk(id, "Choose", null, Array.Empty<ControlAskButton>(),
            null, null, ControlAskStyle.Gui, ControlAskAlignment.Right, null);
        await MutateAsync(_ => Assert.True(_host.Actions.SetQuickTerminal("show").Ok));
        Assert.True(_host.Actions.Pick.OpenAsk(ask, _host.WindowId));
        var sessionAskEscaped = 0;

        await _shortcut.ExecuteAsync(_ => throw new InvalidOperationException("Session was closed"),
            () => { sessionAskEscaped++; return false; });

        Assert.Null(_host.Actions.Pick.PendingAsk);
        Assert.True(_host.Actions.Quick.Visible);
        Assert.Equal(0, sessionAskEscaped);
        Assert.Equal(0, _closed);
    }

    [Fact]
    public async Task WinUiBindsCloseActionToCoverPeel()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AgTerm.slnx")))
            root = root.Parent;
        Assert.NotNull(root);
        var mainWindow = File.ReadAllText(Path.Combine(root.FullName,
            "src", "AgTerm.App", "MainWindow.xaml.cs"));

        Assert.Contains("[BuiltinAction.CloseSession] = Async(", mainWindow, StringComparison.Ordinal);
        Assert.Contains("new CloseSessionShortcut(host.StoreCoordinator, host.Host.Actions).ExecuteAsync(",
            mainWindow, StringComparison.Ordinal);
        Assert.Contains("EscapeFocusedTerminalAsk,", mainWindow, StringComparison.Ordinal);
        Assert.Contains("FocusActiveTerminal)),", mainWindow, StringComparison.Ordinal);
    }
}
