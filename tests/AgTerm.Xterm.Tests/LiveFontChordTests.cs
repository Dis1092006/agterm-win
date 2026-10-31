using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Xterm;
using Xunit;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// upstream AppActions.increaseFontSize / decreaseFontSize / resetFontSize act on focusedSurface():
/// the terminal that has keyboard focus, else the selected session's active pane. A chord from the
/// page names the pane that received the key, so that pane is the focused surface.
/// </summary>
public sealed class LiveFontChordTests
{
    [Fact]
    public async Task Chord_without_a_source_pane_changes_the_active_pane()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-live-font-chord-fallback-").FullName;
        try
        {
            using var host = CreateHost(stateDir);
            var panes = Attach(host, scratchActive: true, overlayActive: true);

            await host.PerformFontActionAsync("increase_font_size:1");
            Assert.Equal(["increase_font_size:1"], panes.Main.BindingActions);

            host.Host.RunLocked(() => host.Host.Store.ActiveSession!.SplitFocused = true);
            await host.PerformFontActionAsync("decrease_font_size:1", sourcePaneId: "unknown-pane");
            Assert.Equal(["decrease_font_size:1"], panes.Split.BindingActions);
            Assert.Empty(panes.Scratch.BindingActions);
            Assert.Empty(panes.Overlay.BindingActions);
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Chord_from_a_pane_changes_that_pane()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-live-font-chord-source-").FullName;
        try
        {
            using var host = CreateHost(stateDir);
            var panes = Attach(host, scratchActive: true, overlayActive: true);

            await host.PerformFontActionAsync("increase_font_size:1", sourcePaneId: "overlay");
            await host.PerformFontActionAsync("decrease_font_size:1", sourcePaneId: "scratch");
            await host.PerformFontActionAsync("reset_font_size", sourcePaneId: "split");

            Assert.Equal(["increase_font_size:1"], panes.Overlay.BindingActions);
            Assert.Equal(["decrease_font_size:1"], panes.Scratch.BindingActions);
            Assert.Equal(["reset_font_size"], panes.Split.BindingActions);
            Assert.Empty(panes.Main.BindingActions);
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
        }
    }

    // the scratch is open, but the key came from the main pane: the main pane changes, not the
    // surface the model puts on top.
    [Fact]
    public async Task Chord_from_the_main_pane_under_an_open_scratch_changes_the_main_pane()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-live-font-chord-scratch-").FullName;
        try
        {
            using var host = CreateHost(stateDir);
            var panes = Attach(host, scratchActive: true, overlayActive: false);

            await host.PerformFontActionAsync("increase_font_size:1", sourcePaneId: "main");

            Assert.Equal(["increase_font_size:1"], panes.Main.BindingActions);
            Assert.Empty(panes.Scratch.BindingActions);
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
        }
    }

    // upstream: "deliberately NOT zoom-gated: font commands act on the FOCUSED surface — while zoomed
    // that is the zoomed terminal".
    [Fact]
    public async Task Chord_from_the_zoomed_pane_changes_it()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-live-font-chord-zoom-").FullName;
        try
        {
            using var host = CreateHost(stateDir);
            var panes = Attach(host, scratchActive: false, overlayActive: false);
            host.Host.RunLocked(() => host.Host.Actions.Zoom.Set(
                ControlToggleMode.On,
                new TerminalZoomTarget.Session(host.Host.Store.ActiveSession!.Id, TerminalZoomSurface.Split)));

            await host.PerformFontActionAsync("increase_font_size:1", sourcePaneId: "split");

            Assert.Equal(["increase_font_size:1"], panes.Split.BindingActions);
            Assert.Empty(panes.Main.BindingActions);
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
        }
    }

    // a key in another session's pane (a zoom of a session that is not selected) still changes that pane.
    [Fact]
    public async Task Chord_from_a_pane_of_another_session_changes_that_pane()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-live-font-chord-other-").FullName;
        try
        {
            using var host = CreateHost(stateDir);
            var panes = Attach(host, scratchActive: false, overlayActive: false);
            var other = new FakeSurface { PaneToken = "other-main" };
            host.Host.RunLocked(() =>
            {
                var workspace = Assert.Single(host.Host.Store.Workspaces);
                host.Host.Store.AddSession(workspace.Id, stateDir, select: false)!.Surface = other;
            });

            await host.PerformFontActionAsync("increase_font_size:1", sourcePaneId: "other-main");

            Assert.Equal(["increase_font_size:1"], other.BindingActions);
            Assert.Empty(panes.Main.BindingActions);
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
        }
    }

    private sealed record Panes(FakeSurface Main, FakeSurface Split, FakeSurface Scratch, FakeSurface Overlay);

    private static Panes Attach(LiveControlHost host, bool scratchActive, bool overlayActive)
    {
        Panes panes = null!;
        host.Host.RunLocked(() =>
        {
            var session = Assert.IsType<Session>(host.Host.Store.ActiveSession);
            panes = new Panes(new FakeSurface { PaneToken = "main" }, new FakeSurface { PaneToken = "split" },
                new FakeSurface { PaneToken = "scratch" }, new FakeSurface { PaneToken = "overlay" });
            session.Surface = panes.Main;
            session.SplitSurface = panes.Split;
            session.HasSplit = true;
            session.ScratchSurface = panes.Scratch;
            session.ScratchActive = scratchActive;
            session.OverlaySurface = panes.Overlay;
            session.OverlayActive = overlayActive;
        });
        return panes;
    }

    private static LiveControlHost CreateHost(string stateDir) => new(new LiveControlHostOptions
    {
        StateDir = stateDir,
        ConfigDir = stateDir,
        PipeName = "agterm-live-font-chord-" + Guid.NewGuid().ToString("N"),
        OpenSurface = (request, _) => Task.FromResult<IHeadlessSurface>(
            new FakeSurface { PaneToken = $"{request.Kind}-{request.SessionId:N}" }),
    });

    // MainWindow is a WinUI type the test project does not compile, so the chord's source pane
    // reaching the font handlers is pinned in its source.
    [Fact]
    public void Main_window_passes_the_chord_source_pane_to_the_font_handlers()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AgTerm.App", "MainWindow.xaml.cs"));

        Assert.Contains("HandleChord(hit.Chord, hit.Generation, hit.PaneId)", source, StringComparison.Ordinal);
        Assert.Contains("return ExecuteBuiltinHotkey(action, sourcePaneId);", source, StringComparison.Ordinal);
        Assert.Contains("_hotkeySourcePaneId = sourcePaneId;", source, StringComparison.Ordinal);
        Assert.Contains("host.PerformFontActionAsync(\"increase_font_size:1\", _hotkeySourcePaneId)", source, StringComparison.Ordinal);
        Assert.Contains("host.PerformFontActionAsync(\"decrease_font_size:1\", _hotkeySourcePaneId)", source, StringComparison.Ordinal);
        Assert.Contains("host.PerformFontActionAsync(\"reset_font_size\", _hotkeySourcePaneId)", source, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AgTerm.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
