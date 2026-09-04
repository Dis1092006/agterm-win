using System.Collections.Concurrent;
using System.Text.Json;
using AgTerm.App;
using AgTerm.ControlServer;
using AgTerm.Core.Keymap;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

public class Phase5ActionCoverageTests
{
    public enum ActionDisposition
    {
        Implemented,
    }

    public enum ChordDisposition
    {
        Bound,
        Keyless,
    }

    private sealed record ActionCoverageRow(
        BuiltinAction Action,
        ActionDisposition Disposition,
        ChordDisposition ChordDisposition,
        string? UpstreamDefaultChord,
        string? EffectiveWindowsChord);

    private static readonly ActionCoverageRow[] Coverage =
    [
        new(BuiltinAction.NewWindow, ActionDisposition.Implemented, ChordDisposition.Bound, "cmd+opt+n", "ctrl+alt+n"),
        new(BuiltinAction.RenameWindow, ActionDisposition.Implemented, ChordDisposition.Keyless, null, null),
        new(BuiltinAction.DeleteWindow, ActionDisposition.Implemented, ChordDisposition.Keyless, null, null),
        new(BuiltinAction.NewWorkspace, ActionDisposition.Implemented, ChordDisposition.Bound, "cmd+shift+n", "ctrl+shift+n"),
        new(BuiltinAction.RenameWorkspace, ActionDisposition.Implemented, ChordDisposition.Keyless, null, null),
        new(BuiltinAction.DeleteWorkspace, ActionDisposition.Implemented, ChordDisposition.Keyless, null, null),
        new(BuiltinAction.NewSession, ActionDisposition.Implemented, ChordDisposition.Bound, "cmd+n", "ctrl+n"),
        new(BuiltinAction.OpenDirectory, ActionDisposition.Implemented, ChordDisposition.Bound, "cmd+o", "ctrl+o"),
        new(BuiltinAction.RenameSession, ActionDisposition.Implemented, ChordDisposition.Keyless, null, null),
        new(BuiltinAction.DuplicateSession, ActionDisposition.Implemented, ChordDisposition.Keyless, null, null),
        new(BuiltinAction.CloseSession, ActionDisposition.Implemented, ChordDisposition.Bound, "cmd+w", "ctrl+w"),
        new(BuiltinAction.ReopenRecent, ActionDisposition.Implemented, ChordDisposition.Bound, "cmd+shift+t", "ctrl+shift+t"),
        new(BuiltinAction.UndoClose, ActionDisposition.Implemented, ChordDisposition.Bound, "cmd+z", "ctrl+z"),
        new(BuiltinAction.ClearStatus, ActionDisposition.Implemented, ChordDisposition.Keyless, null, null),
        new(BuiltinAction.IncreaseFontSize, ActionDisposition.Implemented, ChordDisposition.Bound, "cmd++", "ctrl++"),
        new(BuiltinAction.DecreaseFontSize, ActionDisposition.Implemented, ChordDisposition.Bound, "cmd+-", "ctrl+-"),
        new(BuiltinAction.ResetFontSize, ActionDisposition.Implemented, ChordDisposition.Bound, "cmd+0", "ctrl+0"),
        new(BuiltinAction.ToggleSplit, ActionDisposition.Implemented, ChordDisposition.Bound, "cmd+d", "ctrl+d"),
        new(BuiltinAction.ToggleScratch, ActionDisposition.Implemented, ChordDisposition.Bound, "cmd+j", "ctrl+j"),
        new(BuiltinAction.ToggleTerminalZoom, ActionDisposition.Implemented, ChordDisposition.Bound, "cmd+shift+return", "ctrl+shift+return"),
        new(BuiltinAction.ToggleSearch, ActionDisposition.Implemented, ChordDisposition.Bound, "cmd+f", "ctrl+f"),
        new(BuiltinAction.ToggleSidebar, ActionDisposition.Implemented, ChordDisposition.Bound, "ctrl+cmd+s", "ctrl+s"),
        new(BuiltinAction.SelectTheme, ActionDisposition.Implemented, ChordDisposition.Keyless, null, null),
        new(BuiltinAction.ToggleFullscreen, ActionDisposition.Implemented, ChordDisposition.Bound, "ctrl+cmd+f", "ctrl+f"),
        new(BuiltinAction.ToggleFlaggedView, ActionDisposition.Implemented, ChordDisposition.Keyless, null, null),
        new(BuiltinAction.ToggleFlag, ActionDisposition.Implemented, ChordDisposition.Bound, "cmd+shift+f", "ctrl+shift+f"),
        new(BuiltinAction.FocusWorkspace, ActionDisposition.Implemented, ChordDisposition.Keyless, null, null),
        new(BuiltinAction.FocusLeftPane, ActionDisposition.Implemented, ChordDisposition.Bound, "cmd+opt+left", "ctrl+alt+left"),
        new(BuiltinAction.FocusRightPane, ActionDisposition.Implemented, ChordDisposition.Bound, "cmd+opt+right", "ctrl+alt+right"),
        new(BuiltinAction.PreviousSession, ActionDisposition.Implemented, ChordDisposition.Bound, "cmd+opt+up", "ctrl+alt+up"),
        new(BuiltinAction.NextSession, ActionDisposition.Implemented, ChordDisposition.Bound, "cmd+opt+down", "ctrl+alt+down"),
        new(BuiltinAction.PreviousAttentionSession, ActionDisposition.Implemented, ChordDisposition.Bound, "ctrl+opt+up", "ctrl+alt+up"),
        new(BuiltinAction.NextAttentionSession, ActionDisposition.Implemented, ChordDisposition.Bound, "ctrl+opt+down", "ctrl+alt+down"),
        new(BuiltinAction.FirstSession, ActionDisposition.Implemented, ChordDisposition.Keyless, null, null),
        new(BuiltinAction.LastSession, ActionDisposition.Implemented, ChordDisposition.Keyless, null, null),
        new(BuiltinAction.QuickTerminal, ActionDisposition.Implemented, ChordDisposition.Bound, "ctrl+`", "ctrl+`"),
        new(BuiltinAction.SessionPalette, ActionDisposition.Implemented, ChordDisposition.Bound, "ctrl+p", "ctrl+p"),
        new(BuiltinAction.CommandPalette, ActionDisposition.Implemented, ChordDisposition.Bound, "ctrl+shift+p", "ctrl+shift+p"),
        new(BuiltinAction.CustomCommandPalette, ActionDisposition.Implemented, ChordDisposition.Bound, "ctrl+shift+o", "ctrl+shift+o"),
        new(BuiltinAction.ShowAttention, ActionDisposition.Implemented, ChordDisposition.Bound, "ctrl+shift+i", "ctrl+shift+i"),
        new(BuiltinAction.Dashboard, ActionDisposition.Implemented, ChordDisposition.Bound, "cmd+shift+d", "ctrl+shift+d"),
    ];

    public static IEnumerable<object?[]> ActionRows => Coverage.Select(row => new object?[]
    {
        row.Action,
        row.Disposition,
        row.ChordDisposition,
        row.UpstreamDefaultChord,
        row.EffectiveWindowsChord,
    });

    [Fact]
    public void Coverage_table_contains_each_builtin_action_once_with_an_explicit_disposition()
    {
        Assert.Equal(41, Coverage.Length);
        Assert.Equal(Coverage.Length, Coverage.Select(row => row.Action).Distinct().Count());
        Assert.Equal(BuiltinActions.All, Coverage.Select(row => row.Action).ToArray());
        Assert.All(Coverage, row => Assert.Equal(ActionDisposition.Implemented, row.Disposition));

        Assert.Equal(BuiltinActions.All.Count, WindowCommandRouter.ActionImplementations.Count);
        Assert.Equal(BuiltinActions.All.ToHashSet(), WindowCommandRouter.ActionImplementations.Keys.ToHashSet());
    }

    [Fact]
    public void Platform_notes_phase5_action_matrix_matches_coverage_table()
    {
        const string startMarker = "<!-- phase5-action-matrix:start -->";
        const string endMarker = "<!-- phase5-action-matrix:end -->";
        const string staleS3Text = "21 actions have handlers now and 20 phase-5 actions remain explicitly planned";

        var document = File.ReadAllText(Path.Combine(FindRepoRoot(), "docs", "PLATFORM-NOTES.md"));
        var start = document.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing Phase 5 action matrix start marker: {startMarker}");
        var end = document.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end >= 0, $"Missing Phase 5 action matrix end marker: {endMarker}");

        Assert.DoesNotContain(staleS3Text, document, StringComparison.Ordinal);

        var actualLines = document[(start + startMarker.Length)..end]
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(Coverage.Length + 2, actualLines.Length);
        Assert.Equal(
            ["Builtin action", "Wire name", "Disposition", "Upstream default", "Effective Windows"],
            TableCells(actualLines[0]));
        Assert.All(TableCells(actualLines[1]), cell => Assert.Matches("^-{3,}$", cell));
        for (var index = 0; index < Coverage.Length; index++)
        {
            var row = Coverage[index];
            Assert.Equal(
                [$"`{row.Action}`", $"`{row.Action.WireName()}`", $"`{row.Disposition}`", ChordCell(row.UpstreamDefaultChord), ChordCell(row.EffectiveWindowsChord)],
                TableCells(actualLines[index + 2]));
        }
    }

    [Theory]
    [MemberData(nameof(ActionRows))]
    public void Action_row_matches_the_supported_phase5_surface(
        BuiltinAction action,
        ActionDisposition disposition,
        ChordDisposition chordDisposition,
        string? expectedUpstreamDefaultChord,
        string? expectedEffectiveWindowsChord)
    {
        var shippedDefault = action.DefaultChord();
        var defaultKeymapChord = Keymap.Empty.EquivalentFor(action);
        Assert.Equal(shippedDefault, defaultKeymapChord);

        if (chordDisposition == ChordDisposition.Bound)
        {
            Assert.NotNull(expectedUpstreamDefaultChord);
            Assert.NotNull(expectedEffectiveWindowsChord);
            Assert.NotNull(defaultKeymapChord);
            Assert.Equal(expectedUpstreamDefaultChord, defaultKeymapChord.Value.DisplayString);
            Assert.Equal(
                expectedEffectiveWindowsChord,
                Display(WindowsChordDecoder.Effective(defaultKeymapChord.Value)));
        }
        else
        {
            Assert.Equal(ChordDisposition.Keyless, chordDisposition);
            Assert.Null(expectedUpstreamDefaultChord);
            Assert.Null(expectedEffectiveWindowsChord);
            Assert.Null(defaultKeymapChord);
        }

        Assert.Equal(ActionDisposition.Implemented, disposition);
        Assert.True(
            WindowCommandRouter.ActionImplementations.TryGetValue(action, out var implemented),
            $"ActionImplementations is missing {action}");
        Assert.True(implemented, $"{action} must be marked implemented");
    }

    private static string Display(PhysicalChord chord)
    {
        var parts = new List<string>();
        if (chord.Ctrl) parts.Add("ctrl");
        if (chord.Alt) parts.Add("alt");
        if (chord.Shift) parts.Add("shift");
        parts.Add(chord.Key);
        return string.Join("+", parts);
    }

    private static string ChordCell(string? chord) => chord switch
    {
        null => "`keyless`",
        _ when chord.Contains('`') => $"<code>{chord.Replace("`", "&#96;", StringComparison.Ordinal)}</code>",
        _ => $"`{chord}`",
    };

    private static string[] TableCells(string row) => row
        .Trim()
        .Trim('|')
        .Split('|')
        .Select(cell => cell.Trim())
        .ToArray();

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}

[Collection("webview2")]
public sealed class LivePhase5E2ETests
{
    private static readonly string CommandShell =
        Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";

    private readonly WebView2Fixture _webView;
    private readonly SemaphoreSlim _settingsSaveGate = new(1, 1);

    public LivePhase5E2ETests(WebView2Fixture webView)
    {
        _webView = webView;
    }

    [Fact]
    public async Task Two_live_windows_route_background_sessions_and_events_over_one_application_pipe()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-phase5-live-").FullName;
        LiveApplicationHost? application = null;
        LiveControlHost? firstAttachment = null;
        LiveControlHost? secondAttachment = null;
        TestPage? firstPage = null;
        TestPage? secondPage = null;
        Guid? firstWindowId = null;
        Guid? secondWindowId = null;
        PagePresentation? firstPresentation = null;
        PagePresentation? secondPresentation = null;
        var firstNotifications = new ConcurrentQueue<LiveBannerNotification>();
        var secondNotifications = new ConcurrentQueue<LiveBannerNotification>();
        var firstStatusSounds = new ConcurrentQueue<string>();
        var secondStatusSounds = new ConcurrentQueue<string>();
        var realizedSurfaces = new List<XtermPaneSurface>();
        LiveApplicationHost? restartApplication = null;
        LiveControlHost? restartFirstAttachment = null;
        LiveControlHost? restartSecondAttachment = null;
        TestPage? restartFirstPage = null;
        TestPage? restartSecondPage = null;
        PagePresentation? restartFirstPresentation = null;
        PagePresentation? restartSecondPresentation = null;
        var restartFirstNotifications = new ConcurrentQueue<LiveBannerNotification>();
        var restartSecondNotifications = new ConcurrentQueue<LiveBannerNotification>();
        var restartFirstStatusSounds = new ConcurrentQueue<string>();
        var restartSecondStatusSounds = new ConcurrentQueue<string>();
        var restartRealizedSurfaces = new List<XtermPaneSurface>();

        try
        {
            firstPage = await CreatePageAsync();
            secondPage = await CreatePageAsync();
            Assert.NotSame(firstPage.Page, secondPage.Page);
            Assert.NotSame(firstPage.View, secondPage.View);

            var controller = new AttachingWindowController(async (windowId, cancellationToken) =>
            {
                secondAttachment = await Task.Run(
                    () => application!.AttachWindow(
                        windowId,
                        CreateHostOptions(stateDir, secondPage.Page)),
                    cancellationToken);
                return secondAttachment;
            });
            application = new LiveApplicationHost(new LiveApplicationHostOptions
            {
                StateDir = stateDir,
                HomeDirectory = stateDir,
                PipeName = "agterm-phase5-live-" + Guid.NewGuid().ToString("N"),
                WindowController = controller,
            });

            firstWindowId = Assert.Single(application.LaunchWindowIds);
            firstAttachment = await Task.Run(() => application.AttachWindow(
                firstWindowId.Value,
                CreateHostOptions(stateDir, firstPage.Page)));
            var firstInitialSessionId = InitialSessionId(firstAttachment);
            var firstInitialWorkspaceId = ReadLocked(
                firstAttachment,
                () => Assert.Single(firstAttachment.Store.Workspaces).Id);
            var firstInitialSurface = await WaitForSurfaceAsync(firstAttachment, firstInitialSessionId);
            realizedSurfaces.Add(firstInitialSurface);
            firstPresentation = await AttachPagePresentationAsync(firstAttachment, firstPage, stateDir);
            firstAttachment.NotificationRequested += firstNotifications.Enqueue;
            firstAttachment.StatusNotificationRequested += firstStatusSounds.Enqueue;

            var newWindow = Ok(await application.CreateWindowAsync("phase5 second"));
            secondWindowId = Guid.Parse(newWindow.Result!.Id!);
            Assert.NotEqual(firstWindowId.Value, secondWindowId.Value);
            Assert.Equal(secondWindowId, controller.OpenedWindowId);
            Assert.NotNull(secondAttachment);
            var secondInitialSessionId = InitialSessionId(secondAttachment);
            var secondInitialWorkspaceId = ReadLocked(
                secondAttachment,
                () => Assert.Single(secondAttachment.Store.Workspaces).Id);
            var secondInitialSurface = await WaitForSurfaceAsync(secondAttachment, secondInitialSessionId);
            realizedSurfaces.Add(secondInitialSurface);
            secondPresentation = await AttachPagePresentationAsync(secondAttachment, secondPage, stateDir);
            secondAttachment.NotificationRequested += secondNotifications.Enqueue;
            secondAttachment.StatusNotificationRequested += secondStatusSounds.Enqueue;

            application.Start();
            Assert.True(application.IsListening);
            var client = new ControlPipeClient(application.PipeName);
            var initialCursor = Ok(client.Send(new ControlRequest
            {
                Cmd = Command.EventsRead,
            })).Result!.Events!;

            Assert.Equal(2, application.Windows.Count);
            Assert.All(application.Windows, window => Assert.True(window.IsOpen));
            Assert.Equal(
                new[] { firstWindowId.Value, secondWindowId.Value },
                application.Windows.Select(window => window.Id));
            Assert.NotSame(firstAttachment.Store, secondAttachment.Store);
            Assert.NotSame(firstAttachment, secondAttachment);
            Assert.Same(firstPage.Page, firstInitialSurface.Pane.Page);
            Assert.Same(secondPage.Page, secondInitialSurface.Pane.Page);
            Assert.NotEqual(firstInitialSurface.PaneToken, secondInitialSurface.PaneToken);

            var firstWorkspaceId = CreateWorkspace(
                client,
                firstWindowId.Value,
                "first background workspace");
            var secondWorkspaceId = CreateWorkspace(
                client,
                secondWindowId.Value,
                "second background workspace");
            var firstBackgroundSessionId = CreateBackgroundSession(
                client,
                firstWindowId.Value,
                firstWorkspaceId,
                stateDir,
                "first background session");
            var secondBackgroundSessionId = CreateBackgroundSession(
                client,
                secondWindowId.Value,
                secondWorkspaceId,
                stateDir,
                "second background session");

            var firstStoreBeforeMismatch = StableStoreState(firstAttachment);
            var secondStoreBeforeMismatch = StableStoreState(secondAttachment);
            var mismatchedWindow = client.Send(new ControlRequest
            {
                Cmd = Command.SessionRename,
                Target = firstBackgroundSessionId.ToString("D"),
                Args = new ControlArgs
                {
                    Window = secondWindowId.Value.ToString("D"),
                    Name = "must not be applied",
                },
            });
            Assert.False(mismatchedWindow.Ok);
            Assert.Equal(
                $"no such session: {firstBackgroundSessionId:D}",
                mismatchedWindow.Error);
            Assert.Equal(firstStoreBeforeMismatch, StableStoreState(firstAttachment));
            Assert.Equal(secondStoreBeforeMismatch, StableStoreState(secondAttachment));

            var firstBackgroundSurface = await WaitForSurfaceAsync(
                firstAttachment,
                firstBackgroundSessionId);
            var secondBackgroundSurface = await WaitForSurfaceAsync(
                secondAttachment,
                secondBackgroundSessionId);
            realizedSurfaces.Add(firstBackgroundSurface);
            realizedSurfaces.Add(secondBackgroundSurface);

            Assert.Same(firstPage.Page, firstBackgroundSurface.Pane.Page);
            Assert.Same(secondPage.Page, secondBackgroundSurface.Pane.Page);
            Assert.Equal(
                4,
                realizedSurfaces.Select(surface => surface.PaneToken).Distinct().Count());
            AssertWindowState(
                firstAttachment,
                firstWorkspaceId,
                firstBackgroundSessionId);
            AssertWindowState(
                secondAttachment,
                secondWorkspaceId,
                secondBackgroundSessionId);

            var firstBackgroundMarker = "PHASE5-BG-FIRST-" + Guid.NewGuid().ToString("N")[..8];
            var secondBackgroundMarker = "PHASE5-BG-SECOND-" + Guid.NewGuid().ToString("N")[..8];
            Ok(client.Send(new ControlRequest
            {
                Cmd = Command.SessionType,
                Target = firstBackgroundSessionId.ToString("D"),
                Args = new ControlArgs
                {
                    Window = firstWindowId.Value.ToString("D"),
                    Text = $"echo {firstBackgroundMarker}\n",
                },
            }));
            Ok(client.Send(new ControlRequest
            {
                Cmd = Command.SessionType,
                Target = secondBackgroundSessionId.ToString("D"),
                Args = new ControlArgs
                {
                    Window = secondWindowId.Value.ToString("D"),
                    Text = $"echo {secondBackgroundMarker}\n",
                },
            }));
            var firstBackgroundText = await WaitForSessionTextAsync(
                client,
                firstWindowId.Value,
                firstBackgroundSessionId,
                firstBackgroundMarker);
            var secondBackgroundText = await WaitForSessionTextAsync(
                client,
                secondWindowId.Value,
                secondBackgroundSessionId,
                secondBackgroundMarker);
            Assert.DoesNotContain(secondBackgroundMarker, firstBackgroundText);
            Assert.DoesNotContain(firstBackgroundMarker, secondBackgroundText);
            AssertWindowState(firstAttachment, firstWorkspaceId, firstBackgroundSessionId);
            AssertWindowState(secondAttachment, secondWorkspaceId, secondBackgroundSessionId);

            Ok(client.Send(new ControlRequest
            {
                Cmd = Command.SessionSplit,
                Target = firstInitialSessionId.ToString("D"),
                Args = new ControlArgs
                {
                    Window = firstWindowId.Value.ToString("D"),
                    Mode = "on",
                },
            }));
            var firstSplitSurface = await WaitForSurfaceSlotAsync(
                firstAttachment,
                () => firstAttachment.Store.SessionWithId(firstInitialSessionId)?.SplitSurface,
                $"split for session {firstInitialSessionId:D}");
            realizedSurfaces.Add(firstSplitSurface);
            Assert.Same(firstPage.Page, firstSplitSurface.Pane.Page);
            Assert.NotSame(firstInitialSurface.Pane, firstSplitSurface.Pane);
            Assert.NotEqual(firstInitialSurface.PaneToken, firstSplitSurface.PaneToken);

            var splitMarker = "PHASE5-SPLIT-" + Guid.NewGuid().ToString("N")[..8];
            Ok(client.Send(new ControlRequest
            {
                Cmd = Command.SessionType,
                Target = firstInitialSessionId.ToString("D"),
                Args = new ControlArgs
                {
                    Window = firstWindowId.Value.ToString("D"),
                    Pane = "right",
                    Text = $"echo {splitMarker}\n",
                },
            }));
            await WaitForSessionTextAsync(
                client,
                firstWindowId.Value,
                firstInitialSessionId,
                splitMarker,
                pane: "right");

            var resizedSplit = Ok(client.Send(new ControlRequest
            {
                Cmd = Command.SessionResize,
                Target = firstInitialSessionId.ToString("D"),
                Args = new ControlArgs
                {
                    Window = firstWindowId.Value.ToString("D"),
                    Ratio = 0.37,
                },
            }));
            Assert.InRange(resizedSplit.Result!.Ratio!.Value, 0.369, 0.371);
            Ok(client.Send(new ControlRequest
            {
                Cmd = Command.SessionFocus,
                Target = firstInitialSessionId.ToString("D"),
                Args = new ControlArgs
                {
                    Window = firstWindowId.Value.ToString("D"),
                    Pane = "right",
                },
            }));
            await WaitUntilLockedAsync(
                firstAttachment,
                () =>
                {
                    var session = firstAttachment.Store.SessionWithId(firstInitialSessionId);
                    return session is not null
                        && ReferenceEquals(session.SplitSurface, firstSplitSurface)
                        && session.SplitFocused
                        && session.SplitRatio is >= 0.369 and <= 0.371;
                },
                "first window split did not retain its surface, ratio, and right-pane focus");

            Ok(client.Send(new ControlRequest
            {
                Cmd = Command.SessionScratch,
                Target = firstInitialSessionId.ToString("D"),
                Args = new ControlArgs
                {
                    Window = firstWindowId.Value.ToString("D"),
                    Mode = "on",
                },
            }));
            var firstScratchSurface = await WaitForSurfaceSlotAsync(
                firstAttachment,
                () => firstAttachment.Store.SessionWithId(firstInitialSessionId)?.ScratchSurface,
                $"scratch for session {firstInitialSessionId:D}");
            realizedSurfaces.Add(firstScratchSurface);
            Assert.Same(firstPage.Page, firstScratchSurface.Pane.Page);
            Assert.NotEqual(firstInitialSurface.PaneToken, firstScratchSurface.PaneToken);
            Assert.NotEqual(firstSplitSurface.PaneToken, firstScratchSurface.PaneToken);

            var scratchMarker = "PHASE5-SCRATCH-" + Guid.NewGuid().ToString("N")[..8];
            Ok(client.Send(new ControlRequest
            {
                Cmd = Command.SessionType,
                Target = firstInitialSessionId.ToString("D"),
                Args = new ControlArgs
                {
                    Window = firstWindowId.Value.ToString("D"),
                    Pane = "scratch",
                    Text = $"echo {scratchMarker}\n",
                },
            }));
            await WaitForSessionTextAsync(
                client,
                firstWindowId.Value,
                firstInitialSessionId,
                scratchMarker,
                pane: "scratch");
            Ok(client.Send(new ControlRequest
            {
                Cmd = Command.SessionScratch,
                Target = firstInitialSessionId.ToString("D"),
                Args = new ControlArgs
                {
                    Window = firstWindowId.Value.ToString("D"),
                    Mode = "off",
                },
            }));
            await WaitUntilLockedAsync(
                firstAttachment,
                () =>
                {
                    var session = firstAttachment.Store.SessionWithId(firstInitialSessionId);
                    return session is not null
                        && !session.ScratchActive
                        && ReferenceEquals(session.ScratchSurface, firstScratchSurface);
                },
                "hidden scratch did not retain the same inactive surface");

            var overlayMarker = "PHASE5-OVERLAY-" + Guid.NewGuid().ToString("N")[..8];
            Ok(client.Send(new ControlRequest
            {
                Cmd = Command.SessionOverlayOpen,
                Target = secondInitialSessionId.ToString("D"),
                Args = new ControlArgs
                {
                    Window = secondWindowId.Value.ToString("D"),
                    Command = $"echo \"{overlayMarker}\" && ping -n 31 127.0.0.1 > nul",
                },
            }));
            var secondOverlaySurface = await WaitForSurfaceSlotAsync(
                secondAttachment,
                () => secondAttachment.Store.SessionWithId(secondInitialSessionId)?.OverlaySurface,
                $"overlay for session {secondInitialSessionId:D}");
            realizedSurfaces.Add(secondOverlaySurface);
            Assert.Same(secondPage.Page, secondOverlaySurface.Pane.Page);
            Assert.NotSame(secondInitialSurface.Pane, secondOverlaySurface.Pane);
            Assert.NotEqual(secondInitialSurface.PaneToken, secondOverlaySurface.PaneToken);
            await WaitForSurfaceTextAsync(secondOverlaySurface, overlayMarker, "overlay");

            Ok(client.Send(new ControlRequest
            {
                Cmd = Command.SessionOverlayResize,
                Target = secondInitialSessionId.ToString("D"),
                Args = new ControlArgs
                {
                    Window = secondWindowId.Value.ToString("D"),
                    SizePercent = 63,
                },
            }));
            await WaitUntilLockedAsync(
                secondAttachment,
                () =>
                {
                    var session = secondAttachment.Store.SessionWithId(secondInitialSessionId);
                    return session is not null
                        && session.OverlayActive
                        && session.OverlaySizePercent == 63
                        && ReferenceEquals(session.OverlaySurface, secondOverlaySurface);
                },
                "second window overlay did not retain its surface at 63 percent");
            Ok(client.Send(new ControlRequest
            {
                Cmd = Command.SessionOverlayClose,
                Target = secondInitialSessionId.ToString("D"),
                Args = new ControlArgs { Window = secondWindowId.Value.ToString("D") },
            }));
            await WaitUntilLockedAsync(
                secondAttachment,
                () =>
                {
                    var session = secondAttachment.Store.SessionWithId(secondInitialSessionId);
                    return session is not null
                        && !session.OverlayActive
                        && session.OverlaySurface is null;
                },
                "second window overlay stayed active after close");
            await WaitForPaneExitSettlementAsync(secondOverlaySurface);
            var overlayResult = client.Send(new ControlRequest
            {
                Cmd = Command.SessionOverlayResult,
                Target = secondInitialSessionId.ToString("D"),
                Args = new ControlArgs { Window = secondWindowId.Value.ToString("D") },
            });
            Assert.False(overlayResult.Ok);
            Assert.Equal(OverlayResultError.NoResult, overlayResult.Error);

            Ok(client.Send(new ControlRequest
            {
                Cmd = Command.Quick,
                Args = new ControlArgs
                {
                    Mode = "show",
                    Window = secondWindowId.Value.ToString("D"),
                },
            }));
            var secondQuickSurface = await WaitForSurfaceSlotAsync(
                secondAttachment,
                () => secondAttachment.Host.Actions.Quick.Surface,
                $"quick terminal for window {secondWindowId.Value:D}");
            realizedSurfaces.Add(secondQuickSurface);
            Assert.Same(secondPage.Page, secondQuickSurface.Pane.Page);
            Assert.Null(ReadLocked(
                firstAttachment,
                () => firstAttachment.Host.Actions.Quick.Surface));

            var quickMarker = "PHASE5-QUICK-" + Guid.NewGuid().ToString("N")[..8];
            Ok(client.Send(new ControlRequest
            {
                Cmd = Command.QuickType,
                Args = new ControlArgs
                {
                    Window = secondWindowId.Value.ToString("D"),
                    Text = $"echo {quickMarker}\n",
                },
            }));
            await WaitForQuickTextAsync(
                client,
                secondWindowId.Value,
                quickMarker);
            Ok(client.Send(new ControlRequest
            {
                Cmd = Command.Quick,
                Args = new ControlArgs
                {
                    Mode = "hide",
                    Window = secondWindowId.Value.ToString("D"),
                },
            }));
            await WaitUntilLockedAsync(
                secondAttachment,
                () => !secondAttachment.Host.Actions.Quick.Visible
                    && ReferenceEquals(
                        secondAttachment.Host.Actions.Quick.Surface,
                        secondQuickSurface),
                "hidden quick terminal did not retain the same inactive surface");

            Assert.Equal(
                8,
                realizedSurfaces.Select(surface => surface.PaneToken).Distinct().Count());

            var searchMarker = "PHASE5-SEARCH-" + Guid.NewGuid().ToString("N")[..8];
            Ok(client.Send(new ControlRequest
            {
                Cmd = Command.SessionType,
                Target = firstInitialSessionId.ToString("D"),
                Args = new ControlArgs
                {
                    Window = firstWindowId.Value.ToString("D"),
                    Pane = "right",
                    Text = $"echo {searchMarker} {searchMarker}\n",
                },
            }));
            await WaitForSessionTextAsync(
                client,
                firstWindowId.Value,
                firstInitialSessionId,
                $"{searchMarker} {searchMarker}",
                pane: "right");

            var searchOpened = Ok(client.Send(new ControlRequest
            {
                Cmd = Command.SessionSearch,
                Target = firstInitialSessionId.ToString("D"),
                Args = new ControlArgs
                {
                    Window = firstWindowId.Value.ToString("D"),
                    Text = searchMarker,
                },
            }));
            var searchModel = ReadLocked(firstAttachment, () =>
            {
                var session = firstAttachment.Store.SessionWithId(firstInitialSessionId)!;
                return new
                {
                    session.SearchActive,
                    session.SearchNeedle,
                    session.SearchTotal,
                    session.SearchSelected,
                    session.SearchDisplayText,
                    session.SearchSurface,
                };
            });
            Assert.True(searchModel.SearchActive);
            Assert.Equal(searchMarker, searchModel.SearchNeedle);
            Assert.True(searchModel.SearchTotal >= 2);
            Assert.Equal(searchOpened.Result!.Count, searchModel.SearchTotal);
            Assert.Null(searchModel.SearchSelected);
            Assert.Equal($"{searchModel.SearchTotal} matches", searchModel.SearchDisplayText);
            Assert.Same(firstSplitSurface, searchModel.SearchSurface);

            var openSearchState = await WaitForSearchStateAsync(
                firstPage,
                firstSplitSurface.PaneToken!,
                state => !state.Hidden
                    && state.OwnerPaneId == firstSplitSurface.PaneToken
                    && state.Needle == searchMarker
                    && state.AddonCount == searchModel.SearchTotal,
                "search bar/addon did not open on the focused split pane");
            Assert.Equal(searchModel.SearchDisplayText, openSearchState.Status);
            Assert.True(openSearchState.InputFocused);
            Assert.True(openSearchState.AddonCount >= 2);
            Assert.Null(openSearchState.AddonSelected);

            var searchNext = Ok(client.Send(new ControlRequest
            {
                Cmd = Command.SessionSearch,
                Target = firstInitialSessionId.ToString("D"),
                Args = new ControlArgs
                {
                    Window = firstWindowId.Value.ToString("D"),
                    To = "next",
                },
            }));
            Assert.Equal(1, ReadLocked(
                firstAttachment,
                () => firstAttachment.Store.SessionWithId(firstInitialSessionId)!.SearchSelected));
            Assert.Equal("1 of " + searchModel.SearchTotal, searchNext.Result!.Text);
            await WaitForSearchStateAsync(
                firstPage,
                firstSplitSurface.PaneToken!,
                state => state.AddonSelected == 1,
                "search next did not select the first decoration");

            var searchPrevious = Ok(client.Send(new ControlRequest
            {
                Cmd = Command.SessionSearch,
                Target = firstInitialSessionId.ToString("D"),
                Args = new ControlArgs
                {
                    Window = firstWindowId.Value.ToString("D"),
                    To = "prev",
                },
            }));
            Assert.Equal(searchModel.SearchTotal, ReadLocked(
                firstAttachment,
                () => firstAttachment.Store.SessionWithId(firstInitialSessionId)!.SearchSelected));
            Assert.Equal(
                $"{searchModel.SearchTotal} of {searchModel.SearchTotal}",
                searchPrevious.Result!.Text);
            await WaitForSearchStateAsync(
                firstPage,
                firstSplitSurface.PaneToken!,
                state => state.AddonSelected == searchModel.SearchTotal,
                "search previous did not wrap to the last decoration");

            Ok(client.Send(new ControlRequest
            {
                Cmd = Command.SessionSearch,
                Target = firstInitialSessionId.ToString("D"),
                Args = new ControlArgs
                {
                    Window = firstWindowId.Value.ToString("D"),
                    To = "close",
                },
            }));
            var closedSearchModel = ReadLocked(firstAttachment, () =>
            {
                var session = firstAttachment.Store.SessionWithId(firstInitialSessionId)!;
                return new
                {
                    session.SearchActive,
                    session.SearchNeedle,
                    session.SearchTotal,
                    session.SearchSelected,
                    session.SearchSurface,
                };
            });
            Assert.False(closedSearchModel.SearchActive);
            Assert.Equal(string.Empty, closedSearchModel.SearchNeedle);
            Assert.Null(closedSearchModel.SearchTotal);
            Assert.Null(closedSearchModel.SearchSelected);
            Assert.Null(closedSearchModel.SearchSurface);
            var closedSearchState = await WaitForSearchStateAsync(
                firstPage,
                firstSplitSurface.PaneToken!,
                state => state.Hidden && state.AddonCount == 0,
                "search close did not clear the bar and addon decorations");
            Assert.Null(closedSearchState.OwnerPaneId);
            Assert.Equal(string.Empty, closedSearchState.Needle);
            Assert.Null(closedSearchState.AddonSelected);
            Assert.Equal(firstSplitSurface.PaneToken, closedSearchState.FocusedPaneId);

            var firstSelectionBeforePalette = ReadLocked(
                firstAttachment,
                () => firstAttachment.Store.SelectedSessionId);
            var secondSelectionBeforePalette = ReadLocked(
                secondAttachment,
                () => secondAttachment.Store.SelectedSessionId);
            Assert.Equal(firstInitialSessionId, firstSelectionBeforePalette);
            Assert.NotEqual(firstBackgroundSessionId, firstSelectionBeforePalette);

            var sessionPaletteSnapshot = await firstAttachment.StoreCoordinator.ReadAsync(store =>
                WindowPaletteProjection.Create(
                    WindowPaletteMode.Sessions,
                    store,
                    Keymap.Empty,
                    static _ => new WindowCommandState(
                        Implemented: false,
                        CanExecute: false)));
            var sessionPalette = new PaletteController();
            sessionPalette.Open(
                sessionPaletteSnapshot.Items,
                sessionPaletteSnapshot.PreserveInputOrderForEmptyQuery);
            sessionPalette.SetQuery("first background session");
            var expectedSessionItem = Assert.Single(sessionPalette.FilteredItems);
            Assert.Equal(firstBackgroundSessionId.ToString("D"), expectedSessionItem.Id);
            Assert.Equal("first background session", expectedSessionItem.Title);

            var acceptedSessionItem = Assert.IsType<PaletteItemViewModel>(
                sessionPalette.AcceptSelected());
            Assert.Same(expectedSessionItem, acceptedSessionItem);
            Assert.False(sessionPalette.IsOpen);
            var sessionAcceptance = await WindowPaletteAcceptance.AcceptAsync(
                WindowPaletteMode.Sessions,
                acceptedSessionItem,
                async (sessionId, cancellationToken) =>
                {
                    var selectionChanged = false;
                    await firstAttachment.StoreCoordinator.MutateAsync(
                        store => selectionChanged = WindowPaletteActions.SelectSession(
                            store,
                            sessionId),
                        cancellationToken);
                    return selectionChanged;
                },
                (_, _) => throw new InvalidOperationException(
                    "Session palette acceptance must not execute an action."),
                (_, _) => throw new InvalidOperationException(
                    "Session palette acceptance must not execute a custom command."),
                (_, _) => throw new InvalidOperationException(
                    "Session palette acceptance must not set a theme."));
            Assert.False(sessionAcceptance.RestoreTerminalFocus);
            Assert.Equal(firstBackgroundSessionId, ReadLocked(
                firstAttachment,
                () => firstAttachment.Store.SelectedSessionId));
            Assert.Equal(secondSelectionBeforePalette, ReadLocked(
                secondAttachment,
                () => secondAttachment.Store.SelectedSessionId));
            Assert.Same(firstBackgroundSurface, ReadLocked(
                firstAttachment,
                () => firstAttachment.Store.SessionWithId(firstBackgroundSessionId)!.Surface));

            await WaitUntilAsync(
                () => firstPresentation.Selection?.SessionId == firstBackgroundSessionId,
                "session palette selection was not presented by the first window");
            var paletteSelection = Assert.IsType<LiveSelectionSnapshot>(firstPresentation.Selection);
            Assert.Equal(firstBackgroundSessionId, paletteSelection.SessionId);
            Assert.Equal(firstBackgroundSurface.PaneToken, paletteSelection.MainPaneId);
            await WaitForPageLayoutAsync(
                firstPage,
                state => state.VisiblePaneIds.SequenceEqual([firstBackgroundSurface.PaneToken!]),
                "session palette selection did not present the background live pane");

            var customCommandSuffix = Guid.NewGuid().ToString("N")[..8];
            var customCommandName = $"phase5 palette command {customCommandSuffix}";
            var customMarker = $"PHASE5-CUSTOM-{customCommandSuffix}";
            var customMarkerPath = Path.Combine(
                stateDir,
                $"phase5-custom-{customCommandSuffix}.txt");
            var (customKeymap, customDiagnostics) = KeymapParser.Parse(
                $"command \"{customCommandName}\" echo {customMarker}:{{AGT_SESSION_ID}}:{{AGT_PANE}} > \"{customMarkerPath}\"\n");
            Assert.Empty(customDiagnostics);
            var customCommand = Assert.Single(customKeymap.Commands);
            Assert.Equal(customCommandName, customCommand.Name);
            Assert.Equal(string.Empty, customCommand.Shortcut);

            var customPaletteSnapshot = await firstAttachment.StoreCoordinator.ReadAsync(store =>
                WindowPaletteProjection.Create(
                    WindowPaletteMode.CustomCommands,
                    store,
                    customKeymap,
                    static _ => new WindowCommandState(
                        Implemented: false,
                        CanExecute: false)));
            var customPalette = new PaletteController();
            customPalette.Open(
                customPaletteSnapshot.Items,
                customPaletteSnapshot.PreserveInputOrderForEmptyQuery);
            customPalette.SetQuery(customCommandName);
            var expectedCustomItem = Assert.Single(customPalette.FilteredItems);
            Assert.Equal(customCommandName, expectedCustomItem.Title);
            Assert.Null(expectedCustomItem.Shortcut);

            var acceptedCustomItem = Assert.IsType<PaletteItemViewModel>(
                customPalette.AcceptSelected());
            Assert.Same(expectedCustomItem, acceptedCustomItem);
            Assert.False(customPalette.IsOpen);
            string? acceptedCustomPaneId = null;
            string? customSelectionText = null;
            CustomCommandInvocation? customInvocation = null;
            CustomCommandProcessHandle? customProcess = null;
            var customAcceptance = await WindowPaletteAcceptance.AcceptAsync(
                WindowPaletteMode.CustomCommands,
                acceptedCustomItem,
                (_, _) => throw new InvalidOperationException(
                    "Custom-command palette acceptance must not select a session."),
                (_, _) => throw new InvalidOperationException(
                    "Custom-command palette acceptance must not execute an action."),
                async (commandId, cancellationToken) =>
                {
                    acceptedCustomPaneId = WindowPaletteActions.CustomCommandPaneId(
                        paletteSelection,
                        CustomCommandSource.Palette,
                        firedPaneId: null);
                    customSelectionText = await _webView.RunAsync(async _ =>
                        await firstBackgroundSurface.Pane.Terminal.GetSelectionAsync()
                            ?? string.Empty);
                    var customWindow = new CustomCommandWindowContext(
                        firstAttachment.Host.WindowId,
                        firstAttachment.Host.WindowName,
                        firstAttachment.PipeName,
                        HotkeyGeneration: 1);
                    var customRequest = new CustomCommandExecutionRequest(
                        commandId,
                        CustomCommandSource.Palette,
                        PaneId: acceptedCustomPaneId,
                        Selection: customSelectionText);
                    customInvocation = await firstAttachment.StoreCoordinator.ReadAsync(
                        store => CustomCommandInvocationResolver.Resolve(
                            store,
                            customKeymap,
                            customRequest,
                            customWindow),
                        cancellationToken);
                    if (customInvocation is not null)
                    {
                        customProcess = await new CustomCommandProcessRunner().StartAsync(
                            customInvocation,
                            cancellationToken);
                    }
                },
                (_, _) => throw new InvalidOperationException(
                    "Custom-command palette acceptance must not set a theme."));
            Assert.True(customAcceptance.RestoreTerminalFocus);
            Assert.Equal(firstBackgroundSurface.PaneToken, acceptedCustomPaneId);
            var acceptedCustomInvocation = Assert.IsType<CustomCommandInvocation>(customInvocation);
            Assert.Equal(customCommand.Id, acceptedCustomInvocation.CommandId);
            Assert.Equal(customCommandName, acceptedCustomInvocation.Name);
            Assert.Equal(stateDir, acceptedCustomInvocation.WorkingDirectory);
            Assert.Equal(
                firstBackgroundSessionId.ToString("D").ToUpperInvariant(),
                acceptedCustomInvocation.Environment["AGT_SESSION_ID"]);
            Assert.Equal("left", acceptedCustomInvocation.Environment["AGT_PANE"]);
            Assert.Equal(customSelectionText, acceptedCustomInvocation.Environment["AGT_SELECTION"]);

            var expectedCustomMarkerText =
                $"{customMarker}:{firstBackgroundSessionId.ToString("D").ToUpperInvariant()}:left";
            string? actualCustomMarkerText = null;
            await WaitUntilAsync(
                () =>
                {
                    try
                    {
                        if (!File.Exists(customMarkerPath))
                            return false;
                        actualCustomMarkerText = File.ReadAllText(customMarkerPath).Trim();
                        return actualCustomMarkerText == expectedCustomMarkerText;
                    }
                    catch (IOException)
                    {
                        return false;
                    }
                },
                "palette-only custom command did not write the active session context marker");
            Assert.Equal(expectedCustomMarkerText, actualCustomMarkerText);
            var acceptedCustomProcess = Assert.IsType<CustomCommandProcessHandle>(customProcess);
            Assert.True(acceptedCustomProcess.ProcessId > 0);
            await acceptedCustomProcess.Exited.WaitAsync(TimeSpan.FromSeconds(5));
            File.Delete(customMarkerPath);
            Assert.False(File.Exists(customMarkerPath));

            var initialSelectionRestored = false;
            await firstAttachment.StoreCoordinator.MutateAsync(store =>
                initialSelectionRestored = WindowPaletteActions.SelectSession(
                    store,
                    firstInitialSessionId));
            Assert.True(initialSelectionRestored);
            Assert.Equal(secondSelectionBeforePalette, ReadLocked(
                secondAttachment,
                () => secondAttachment.Store.SelectedSessionId));

            var preZoomLayout = await WaitForPageLayoutAsync(
                firstPage,
                state => SameSet(
                    state.VisiblePaneIds,
                    [firstInitialSurface.PaneToken!, firstSplitSurface.PaneToken!]),
                "first page did not settle on its existing split before zoom");
            var preZoomRegistered = preZoomLayout.RegisteredPaneIds.Order().ToArray();
            var splitSurfaceId = new TerminalSurfaceId(
                firstInitialSessionId,
                TerminalZoomSurface.Split).RawValue;
            var zoomOpened = Ok(client.Send(new ControlRequest
            {
                Cmd = Command.SurfaceZoom,
                Target = splitSurfaceId,
                Args = new ControlArgs
                {
                    Window = firstWindowId.Value.ToString("D"),
                    Mode = "show",
                },
            }));
            Assert.Equal(splitSurfaceId, zoomOpened.Result!.Id);
            Assert.True(firstAttachment.ZoomActive);
            Assert.Equal(
                new TerminalZoomTarget.Session(
                    firstInitialSessionId,
                    TerminalZoomSurface.Split),
                ReadLocked(firstAttachment, () => firstAttachment.Host.Actions.Zoom.Target));
            Assert.False(ReadLocked(
                firstAttachment,
                () => firstAttachment.Host.Actions.Dashboard.IsOpen));
            Assert.Same(firstSplitSurface, ReadLocked(
                firstAttachment,
                () => firstAttachment.Store.SessionWithId(firstInitialSessionId)!.SplitSurface));
            Assert.Equal(splitSurfaceId, Tree(client, firstWindowId.Value).ZoomedSurface);
            var zoomLayout = await WaitForPageLayoutAsync(
                firstPage,
                state => state.VisiblePaneIds.SequenceEqual([firstSplitSurface.PaneToken!]),
                "zoom did not show exactly the existing split pane");
            var zoomPresentation = Assert.IsType<LiveSelectionSnapshot>(firstPresentation.Selection);
            Assert.Equal(firstSplitSurface.PaneToken, zoomPresentation.ZoomPaneId);
            Assert.False(zoomPresentation.DashboardOpen);
            Assert.Equal(
                firstSplitSurface.PaneToken,
                Assert.IsType<TerminalLayout.Zoomed>(
                    zoomPresentation.ToTerminalLayout(1)).PaneId);
            Assert.Equal(preZoomRegistered, zoomLayout.RegisteredPaneIds.Order().ToArray());
            Assert.Empty(zoomLayout.DashboardControlRefs);

            Ok(client.Send(new ControlRequest
            {
                Cmd = Command.SurfaceZoom,
                Target = splitSurfaceId,
                Args = new ControlArgs
                {
                    Window = firstWindowId.Value.ToString("D"),
                    Mode = "hide",
                },
            }));
            await WaitUntilAsync(
                () => !firstAttachment.ZoomActive
                    && ReadLocked(firstAttachment, () => firstAttachment.Host.Actions.Zoom.Target) is null,
                "terminal zoom stayed active");
            Assert.Null(Tree(client, firstWindowId.Value).ZoomedSurface);
            await WaitForPageLayoutAsync(
                firstPage,
                state => SameSet(
                    state.VisiblePaneIds,
                    [firstInitialSurface.PaneToken!, firstSplitSurface.PaneToken!]),
                "split layout did not return after zoom closed");

            var dashboardRefs = new[]
            {
                ControlRef(firstInitialSessionId, "left"),
                ControlRef(firstInitialSessionId, "right"),
                ControlRef(firstBackgroundSessionId, "left"),
            };
            Ok(client.Send(new ControlRequest
            {
                Cmd = Command.Dashboard,
                Args = new ControlArgs
                {
                    Window = firstWindowId.Value.ToString("D"),
                    Targets =
                    [
                        firstInitialSessionId.ToString("D"),
                        firstBackgroundSessionId.ToString("D"),
                    ],
                    AutoSize = true,
                },
            }));
            Assert.False(firstAttachment.ZoomActive);
            Assert.Null(ReadLocked(firstAttachment, () => firstAttachment.Host.Actions.Zoom.Target));
            Assert.Equal(dashboardRefs, ReadLocked(
                firstAttachment,
                () => firstAttachment.Host.Actions.Dashboard.Members
                    .Select(member => member.ControlRef)
                    .ToArray()));
            Assert.Equal(dashboardRefs[0], ReadLocked(
                firstAttachment,
                () => firstAttachment.Host.Actions.Dashboard.Highlighted?.ControlRef));
            var dashboardTree = Tree(client, firstWindowId.Value);
            Assert.Equal(dashboardRefs, dashboardTree.DashboardMembers);
            Assert.Equal(dashboardRefs[0], dashboardTree.DashboardHighlighted);
            Assert.Equal("auto", dashboardTree.DashboardFontMode);
            Assert.NotNull(dashboardTree.DashboardFontSize);
            var dashboardLayout = await WaitForPageLayoutAsync(
                firstPage,
                state => SameSet(state.DashboardControlRefs, dashboardRefs)
                    && state.DashboardControlRefs.Length == dashboardRefs.Length
                    && state.DashboardHighlighted == dashboardRefs[0],
                "dashboard did not render the exact model cells");
            var dashboardPresentation = Assert.IsType<LiveSelectionSnapshot>(firstPresentation.Selection);
            Assert.True(dashboardPresentation.DashboardOpen);
            Assert.Null(dashboardPresentation.ZoomPaneId);
            Assert.Equal(
                dashboardRefs,
                dashboardPresentation.DashboardCells?.Select(cell => cell.ControlRef));
            Assert.Equal(dashboardRefs[0], dashboardPresentation.DashboardHighlighted);
            Assert.Equal("grid", dashboardLayout.ContainerDisplay);
            Assert.True(SameSet(
                dashboardLayout.VisiblePaneIds,
                [
                    firstInitialSurface.PaneToken!,
                    firstSplitSurface.PaneToken!,
                    firstBackgroundSurface.PaneToken!,
                ]));
            Assert.Equal(3, dashboardLayout.VisiblePaneIds.Length);

            await DispatchDocumentKeyAsync(firstPage, "ArrowRight");
            await WaitUntilAsync(
                () => ReadLocked(
                    firstAttachment,
                    () => firstAttachment.Host.Actions.Dashboard.Highlighted?.ControlRef)
                    == dashboardRefs[1],
                "dashboard highlight did not move right");
            var movedDashboard = await WaitForPageLayoutAsync(
                firstPage,
                state => state.DashboardHighlighted == dashboardRefs[1],
                "dashboard page did not render the moved highlight");
            Assert.Equal(dashboardRefs[1], movedDashboard.DashboardHighlighted);
            await DispatchDocumentKeyAsync(firstPage, "Enter");
            await WaitUntilAsync(
                () => !ReadLocked(
                    firstAttachment,
                    () => firstAttachment.Host.Actions.Dashboard.IsOpen),
                "dashboard activation did not close the model");
            Assert.Equal(firstInitialSessionId, ReadLocked(
                firstAttachment,
                () => firstAttachment.Store.SelectedSessionId));
            Assert.True(ReadLocked(
                firstAttachment,
                () => firstAttachment.Store.SessionWithId(firstInitialSessionId)!.SplitFocused));
            Ok(client.Send(new ControlRequest
            {
                Cmd = Command.Dashboard,
                Args = new ControlArgs
                {
                    Window = firstWindowId.Value.ToString("D"),
                    Close = true,
                },
            }));
            var dashboardClosedTree = Tree(client, firstWindowId.Value);
            Assert.Null(dashboardClosedTree.DashboardMembers);
            Assert.Null(dashboardClosedTree.DashboardHighlighted);
            Assert.Null(dashboardClosedTree.DashboardFontSize);
            Assert.Null(dashboardClosedTree.DashboardFontMode);
            await WaitForPageLayoutAsync(
                firstPage,
                state => state.DashboardControlRefs.Length == 0
                    && SameSet(
                        state.VisiblePaneIds,
                        [firstInitialSurface.PaneToken!, firstSplitSurface.PaneToken!]),
                "dashboard page did not restore the selected split");

            var themeSet = Ok(client.Send(new ControlRequest
            {
                Cmd = Command.ThemeSet,
                Args = new ControlArgs { Name = "Dracula" },
            }));
            Assert.Equal("Dracula", themeSet.Result!.Theme);
            AssertThemeState(firstAttachment, "Dracula");
            AssertThemeState(secondAttachment, "Dracula");
            await WaitUntilAsync(
                () => firstPresentation.Settings.Theme == "Dracula"
                    && secondPresentation.Settings.Theme == "Dracula",
                "theme callbacks did not update both page settings");
            await Task.WhenAll(
                firstPresentation.WhenThemeIdleAsync(),
                secondPresentation.WhenThemeIdleAsync());
            var dracula = Assert.IsType<TerminalTheme>(ThemeCatalog.Default.Resolve("Dracula"));
            foreach (var surface in new[]
                     {
                         firstInitialSurface,
                         firstBackgroundSurface,
                         firstSplitSurface,
                         firstScratchSurface,
                     })
            {
                await AssertRendererThemeAsync(firstPage, surface.PaneToken!, dracula);
            }
            foreach (var surface in new[]
                     {
                         secondInitialSurface,
                         secondBackgroundSurface,
                         secondQuickSurface,
                     })
            {
                await AssertRendererThemeAsync(secondPage, surface.PaneToken!, dracula);
            }

            var futureSessionId = CreateBackgroundSession(
                client,
                firstWindowId.Value,
                firstWorkspaceId,
                stateDir,
                "first post-theme session");
            var futureSurface = await WaitForSurfaceAsync(firstAttachment, futureSessionId);
            realizedSurfaces.Add(futureSurface);
            Assert.Same(firstPage.Page, futureSurface.Pane.Page);
            await AssertRendererThemeAsync(firstPage, futureSurface.PaneToken!, dracula);
            Assert.Equal(
                9,
                realizedSurfaces.Select(surface => surface.PaneToken).Distinct().Count());

            const string notificationTitle = "Phase 5 second window";
            const string notificationBody = "Notification routed to the owning attachment";
            var notificationResponse = Ok(client.Send(new ControlRequest
            {
                Cmd = Command.Notify,
                Target = secondBackgroundSessionId.ToString("D"),
                Args = new ControlArgs
                {
                    Window = secondWindowId.Value.ToString("D"),
                    Title = notificationTitle,
                    Body = notificationBody,
                },
            }));
            Assert.Equal(
                secondBackgroundSessionId.ToString("D").ToUpperInvariant(),
                notificationResponse.Result!.Id);
            Assert.Null(notificationResponse.Result.Text);
            await WaitUntilAsync(
                () => secondNotifications.Count == 1,
                "second attachment did not receive its notification banner");
            Assert.Empty(firstNotifications);
            var notification = Assert.Single(secondNotifications);
            Assert.Equal(secondBackgroundSessionId, notification.SessionId);
            Assert.Equal(notificationTitle, notification.Title);
            Assert.Equal(notificationBody, notification.Body);
            Assert.Equal("default", notification.SoundName);
            Assert.Empty(firstStatusSounds);
            Assert.Empty(secondStatusSounds);
            Assert.Equal(0, ReadLocked(
                firstAttachment,
                () => firstAttachment.Store.Workspaces
                    .SelectMany(workspace => workspace.Sessions)
                    .Sum(session => session.UnseenCount)));
            Assert.Equal(1, ReadLocked(
                secondAttachment,
                () => secondAttachment.Store.SessionWithId(secondBackgroundSessionId)!.UnseenCount));
            Assert.Equal(1, Tree(client, secondWindowId.Value).Workspaces
                .SelectMany(workspace => workspace.Sessions)
                .Single(session => session.Id == secondBackgroundSessionId.ToString("D").ToUpperInvariant())
                .Unseen);

            var events = Ok(client.Send(new ControlRequest
            {
                Cmd = Command.EventsRead,
                Args = new ControlArgs
                {
                    Run = initialCursor.Run.ToString("D"),
                    After = initialCursor.Next.ToString(),
                },
            })).Result!.Events!;
            AssertWindowEvents(events, firstWindowId.Value, firstBackgroundSessionId);
            AssertWindowEvents(events, secondWindowId.Value, secondBackgroundSessionId);
            AssertNotificationEvent(
                events,
                secondWindowId.Value,
                secondBackgroundSessionId,
                notificationTitle,
                notificationBody);

            var expectedWindows = new[]
            {
                new LiveWindowEntry(firstWindowId.Value, "window 1", IsOpen: true),
                new LiveWindowEntry(secondWindowId.Value, "phase5 second", IsOpen: true),
            };
            Assert.Equal(expectedWindows, application.Windows);
            var expectedLaunchWindowIds = application.LaunchWindowIds.ToArray();
            Assert.Equal(2, expectedLaunchWindowIds.Length);
            Assert.True(expectedLaunchWindowIds.ToHashSet().SetEquals(
                [firstWindowId.Value, secondWindowId.Value]));
            var expectedFirstSelection = ReadLocked(
                firstAttachment,
                () => firstAttachment.Store.SelectedSessionId);
            var expectedSecondSelection = ReadLocked(
                secondAttachment,
                () => secondAttachment.Store.SelectedSessionId);
            Assert.Equal(firstInitialSessionId, expectedFirstSelection);
            Assert.Equal(secondInitialSessionId, expectedSecondSelection);
            var expectedSplitState = ReadLocked(firstAttachment, () =>
            {
                var session = firstAttachment.Store.SessionWithId(firstInitialSessionId)!;
                return (session.IsSplit, session.HasSplit, session.SplitRatio, session.SplitFocused);
            });
            Assert.True(expectedSplitState.IsSplit);
            Assert.True(expectedSplitState.HasSplit);
            Assert.True(expectedSplitState.SplitFocused);
            Assert.InRange(expectedSplitState.SplitRatio!.Value, 0.369, 0.371);
            var originalPipeName = application.PipeName;

            secondPresentation.Dispose();
            secondPresentation = null;
            firstPresentation.Dispose();
            firstPresentation = null;
            var disposedApplication = application;
            application = null;
            firstAttachment = null;
            secondAttachment = null;
            disposedApplication.Dispose();

            foreach (var surface in realizedSurfaces)
            {
                Assert.True(
                    surface.Pane.Exited.IsCompleted,
                    $"pane {surface.PaneToken} must exit during window shutdown");
                await surface.Pane.Exited.WaitAsync(TimeSpan.FromSeconds(5));
            }
            await AssertPanesRemovedAsync(firstPage, realizedSurfaces
                .Where(surface => ReferenceEquals(surface.Pane.Page, firstPage.Page)));
            await AssertPanesRemovedAsync(secondPage, realizedSurfaces
                .Where(surface => ReferenceEquals(surface.Pane.Page, secondPage.Page)));

            await DisposePageAsync(secondPage);
            secondPage = null;
            await DisposePageAsync(firstPage);
            firstPage = null;

            restartFirstPage = await CreatePageAsync();
            restartSecondPage = await CreatePageAsync();
            restartApplication = new LiveApplicationHost(new LiveApplicationHostOptions
            {
                StateDir = stateDir,
                HomeDirectory = stateDir,
                PipeName = "agterm-phase5-live-restart-" + Guid.NewGuid().ToString("N"),
            });
            Assert.NotEqual(originalPipeName, restartApplication.PipeName);
            Assert.Equal(expectedWindows, restartApplication.Windows);
            Assert.Equal(expectedLaunchWindowIds, restartApplication.LaunchWindowIds);
            Assert.All(restartApplication.Windows, window => Assert.True(window.IsOpen));

            restartFirstAttachment = await Task.Run(() => restartApplication.AttachWindow(
                firstWindowId.Value,
                CreateHostOptions(stateDir, restartFirstPage.Page)));
            restartFirstAttachment.NotificationRequested += restartFirstNotifications.Enqueue;
            restartFirstAttachment.StatusNotificationRequested += restartFirstStatusSounds.Enqueue;
            restartSecondAttachment = await Task.Run(() => restartApplication.AttachWindow(
                secondWindowId.Value,
                CreateHostOptions(stateDir, restartSecondPage.Page)));
            restartSecondAttachment.NotificationRequested += restartSecondNotifications.Enqueue;
            restartSecondAttachment.StatusNotificationRequested += restartSecondStatusSounds.Enqueue;
            restartFirstPresentation = await AttachPagePresentationAsync(
                restartFirstAttachment,
                restartFirstPage,
                stateDir);
            restartSecondPresentation = await AttachPagePresentationAsync(
                restartSecondAttachment,
                restartSecondPage,
                stateDir);
            restartApplication.Start();
            Assert.True(restartApplication.IsListening);
            Assert.Equal(restartApplication.PipeName, restartFirstAttachment.PipeName);
            Assert.Equal(restartApplication.PipeName, restartSecondAttachment.PipeName);
            var restartClient = new ControlPipeClient(restartApplication.PipeName);
            var staleRestartEvents = restartClient.Send(new ControlRequest
            {
                Cmd = Command.EventsRead,
                Args = new ControlArgs
                {
                    Run = initialCursor.Run.ToString("D"),
                    After = initialCursor.Next.ToString(),
                },
            });
            Assert.False(staleRestartEvents.Ok);
            Assert.Equal("event run changed", staleRestartEvents.Error);
            var restartCursor = Assert.IsType<ControlEventBatch>(
                staleRestartEvents.Result?.Events);
            Assert.NotEqual(initialCursor.Run, restartCursor.Run);

            var restartFirstInitialSurface = await WaitForSurfaceAsync(
                restartFirstAttachment,
                firstInitialSessionId);
            var restartFirstSplitSurface = await WaitForSurfaceSlotAsync(
                restartFirstAttachment,
                () => restartFirstAttachment.Store.SessionWithId(firstInitialSessionId)?.SplitSurface,
                $"restored split for session {firstInitialSessionId:D}");
            var restartFirstBackgroundSurface = await WaitForSurfaceAsync(
                restartFirstAttachment,
                firstBackgroundSessionId);
            var restartFutureSurface = await WaitForSurfaceAsync(
                restartFirstAttachment,
                futureSessionId);
            var restartSecondInitialSurface = await WaitForSurfaceAsync(
                restartSecondAttachment,
                secondInitialSessionId);
            var restartSecondBackgroundSurface = await WaitForSurfaceAsync(
                restartSecondAttachment,
                secondBackgroundSessionId);
            restartRealizedSurfaces.AddRange(
            [
                restartFirstInitialSurface,
                restartFirstSplitSurface,
                restartFirstBackgroundSurface,
                restartFutureSurface,
                restartSecondInitialSurface,
                restartSecondBackgroundSurface,
            ]);

            Assert.NotSame(restartFirstAttachment.Store, restartSecondAttachment.Store);
            Assert.Equal(expectedWindows, restartApplication.Windows);
            Assert.Equal(expectedLaunchWindowIds, restartApplication.LaunchWindowIds);
            Assert.All(restartApplication.Windows, window => Assert.True(window.IsOpen));
            AssertRestoredWindowState(
                restartFirstAttachment,
                expectedFirstSelection!.Value,
                new RestoredWorkspaceExpectation(
                    firstInitialWorkspaceId,
                    "workspace 1",
                    [firstInitialSessionId]),
                new RestoredWorkspaceExpectation(
                    firstWorkspaceId,
                    "first background workspace",
                    [firstBackgroundSessionId, futureSessionId]));
            AssertRestoredWindowState(
                restartSecondAttachment,
                expectedSecondSelection!.Value,
                new RestoredWorkspaceExpectation(
                    secondInitialWorkspaceId,
                    "workspace 1",
                    [secondInitialSessionId]),
                new RestoredWorkspaceExpectation(
                    secondWorkspaceId,
                    "second background workspace",
                    [secondBackgroundSessionId]));
            var restoredSplitState = ReadLocked(restartFirstAttachment, () =>
            {
                var session = restartFirstAttachment.Store.SessionWithId(firstInitialSessionId)!;
                return (session.IsSplit, session.HasSplit, session.SplitRatio, session.SplitFocused);
            });
            Assert.Equal(expectedSplitState.IsSplit, restoredSplitState.IsSplit);
            Assert.Equal(expectedSplitState.HasSplit, restoredSplitState.HasSplit);
            Assert.Equal(expectedSplitState.SplitRatio, restoredSplitState.SplitRatio);
            Assert.InRange(restoredSplitState.SplitRatio!.Value, 0.369, 0.371);
            Assert.False(restoredSplitState.SplitFocused);

            Assert.Same(restartFirstPage.Page, restartFirstInitialSurface.Pane.Page);
            Assert.Same(restartFirstPage.Page, restartFirstSplitSurface.Pane.Page);
            Assert.Same(restartFirstPage.Page, restartFirstBackgroundSurface.Pane.Page);
            Assert.Same(restartFirstPage.Page, restartFutureSurface.Pane.Page);
            Assert.Same(restartSecondPage.Page, restartSecondInitialSurface.Pane.Page);
            Assert.Same(restartSecondPage.Page, restartSecondBackgroundSurface.Pane.Page);
            Assert.NotSame(firstInitialSurface.Pane, restartFirstInitialSurface.Pane);
            Assert.NotSame(firstSplitSurface.Pane, restartFirstSplitSurface.Pane);
            Assert.NotSame(firstBackgroundSurface.Pane, restartFirstBackgroundSurface.Pane);
            Assert.NotSame(futureSurface.Pane, restartFutureSurface.Pane);
            Assert.NotSame(secondInitialSurface.Pane, restartSecondInitialSurface.Pane);
            Assert.NotSame(secondBackgroundSurface.Pane, restartSecondBackgroundSurface.Pane);
            Assert.Equal(
                restartRealizedSurfaces.Count,
                restartRealizedSurfaces.Select(surface => surface.PaneToken).Distinct().Count());
            Assert.Empty(restartRealizedSurfaces
                .Select(surface => surface.PaneToken)
                .Intersect(realizedSurfaces.Select(surface => surface.PaneToken)));

            AssertThemeState(restartFirstAttachment, "Dracula");
            AssertThemeState(restartSecondAttachment, "Dracula");
            Assert.Equal("Dracula", restartFirstPresentation.Settings.Theme);
            Assert.Equal("Dracula", restartSecondPresentation.Settings.Theme);
            await Task.WhenAll(
                restartFirstPresentation.WhenThemeIdleAsync(),
                restartSecondPresentation.WhenThemeIdleAsync());
            foreach (var surface in restartRealizedSurfaces.Where(surface =>
                         ReferenceEquals(surface.Pane.Page, restartFirstPage.Page)))
            {
                await AssertRendererThemeAsync(restartFirstPage, surface.PaneToken!, dracula);
            }
            foreach (var surface in restartRealizedSurfaces.Where(surface =>
                         ReferenceEquals(surface.Pane.Page, restartSecondPage.Page)))
            {
                await AssertRendererThemeAsync(restartSecondPage, surface.PaneToken!, dracula);
            }

            var restartFirstPaneIds = new[]
            {
                restartFirstInitialSurface.PaneToken!,
                restartFirstSplitSurface.PaneToken!,
                restartFirstBackgroundSurface.PaneToken!,
                restartFutureSurface.PaneToken!,
            };
            var restartFirstLayout = await WaitForPageLayoutAsync(
                restartFirstPage,
                state => SameSet(
                        state.VisiblePaneIds,
                        [restartFirstInitialSurface.PaneToken!, restartFirstSplitSurface.PaneToken!])
                    && SameSet(state.RegisteredPaneIds, restartFirstPaneIds)
                    && state.DashboardControlRefs.Length == 0,
                "restored first page did not settle on the fresh split layout");
            Assert.Equal("grid", restartFirstLayout.ContainerDisplay);
            var restartSecondPaneIds = new[]
            {
                restartSecondInitialSurface.PaneToken!,
                restartSecondBackgroundSurface.PaneToken!,
            };
            await WaitForPageLayoutAsync(
                restartSecondPage,
                state => state.VisiblePaneIds.SequenceEqual([restartSecondInitialSurface.PaneToken!])
                    && SameSet(state.RegisteredPaneIds, restartSecondPaneIds)
                    && state.DashboardControlRefs.Length == 0,
                "restored second page did not settle on the fresh selected pane");

            var restartSearchState = await WaitForSearchStateAsync(
                restartFirstPage,
                restartFirstSplitSurface.PaneToken!,
                state => state.Hidden
                    && state.OwnerPaneId is null
                    && state.AddonCount == 0
                    && state.AddonSelected is null,
                "restored first page retained transient search presentation");
            Assert.Equal(string.Empty, restartSearchState.Needle);

            var restartFirstTree = Tree(restartClient, firstWindowId.Value);
            var restartSecondTree = Tree(restartClient, secondWindowId.Value);
            Assert.Null(restartFirstTree.ZoomedSurface);
            Assert.Null(restartSecondTree.ZoomedSurface);
            Assert.Null(restartFirstTree.DashboardMembers);
            Assert.Null(restartFirstTree.DashboardHighlighted);
            Assert.Null(restartSecondTree.DashboardMembers);
            Assert.Null(restartSecondTree.DashboardHighlighted);
            Assert.All(
                restartFirstTree.Workspaces.SelectMany(workspace => workspace.Sessions),
                session => Assert.Null(session.Unseen));
            Assert.All(
                restartSecondTree.Workspaces.SelectMany(workspace => workspace.Sessions),
                session => Assert.Null(session.Unseen));
            CreateWorkspace(
                restartClient,
                firstWindowId.Value,
                "restart first event workspace");
            CreateWorkspace(
                restartClient,
                secondWindowId.Value,
                "restart second event workspace");
            var restartEvents = Ok(restartClient.Send(new ControlRequest
            {
                Cmd = Command.EventsRead,
                Args = new ControlArgs
                {
                    Run = restartCursor.Run.ToString("D"),
                    After = restartCursor.Next.ToString(),
                },
            })).Result!.Events!;
            Assert.Equal(restartCursor.Run, restartEvents.Run);
            Assert.Contains(
                restartEvents.Items,
                item => item.Window == firstWindowId.Value.ToString("D").ToUpperInvariant());
            Assert.Contains(
                restartEvents.Items,
                item => item.Window == secondWindowId.Value.ToString("D").ToUpperInvariant());
            var restartOverlayResult = restartClient.Send(new ControlRequest
            {
                Cmd = Command.SessionOverlayResult,
                Target = secondInitialSessionId.ToString("D"),
                Args = new ControlArgs { Window = secondWindowId.Value.ToString("D") },
            });
            Assert.False(restartOverlayResult.Ok);
            Assert.Equal(OverlayResultError.NoResult, restartOverlayResult.Error);

            var restartFirstMarker = "PHASE5-RESTART-FIRST-" + Guid.NewGuid().ToString("N")[..8];
            var restartSecondMarker = "PHASE5-RESTART-SECOND-" + Guid.NewGuid().ToString("N")[..8];
            Ok(restartClient.Send(new ControlRequest
            {
                Cmd = Command.SessionType,
                Target = firstBackgroundSessionId.ToString("D"),
                Args = new ControlArgs
                {
                    Window = firstWindowId.Value.ToString("D"),
                    Text = $"echo {restartFirstMarker}\n",
                },
            }));
            Ok(restartClient.Send(new ControlRequest
            {
                Cmd = Command.SessionType,
                Target = secondBackgroundSessionId.ToString("D"),
                Args = new ControlArgs
                {
                    Window = secondWindowId.Value.ToString("D"),
                    Text = $"echo {restartSecondMarker}\n",
                },
            }));
            var restartFirstText = await WaitForSessionTextAsync(
                restartClient,
                firstWindowId.Value,
                firstBackgroundSessionId,
                restartFirstMarker);
            var restartSecondText = await WaitForSessionTextAsync(
                restartClient,
                secondWindowId.Value,
                secondBackgroundSessionId,
                restartSecondMarker);
            Assert.DoesNotContain(restartSecondMarker, restartFirstText);
            Assert.DoesNotContain(restartFirstMarker, restartSecondText);
            Assert.Equal(expectedFirstSelection, ReadLocked(
                restartFirstAttachment,
                () => restartFirstAttachment.Store.SelectedSessionId));
            Assert.Equal(expectedSecondSelection, ReadLocked(
                restartSecondAttachment,
                () => restartSecondAttachment.Store.SelectedSessionId));
            Assert.Empty(restartFirstNotifications);
            Assert.Empty(restartSecondNotifications);
            Assert.Empty(restartFirstStatusSounds);
            Assert.Empty(restartSecondStatusSounds);
        }
        finally
        {
            restartSecondPresentation?.Dispose();
            restartFirstPresentation?.Dispose();
            secondPresentation?.Dispose();
            firstPresentation?.Dispose();
            restartApplication?.Dispose();
            foreach (var surface in restartRealizedSurfaces)
                await WaitForPaneExitSettlementAsync(surface);
            if (restartSecondPage is not null)
            {
                await AssertPanesRemovedAsync(restartSecondPage, restartRealizedSurfaces
                    .Where(surface => ReferenceEquals(surface.Pane.Page, restartSecondPage.Page)));
                await DisposePageAsync(restartSecondPage);
            }
            if (restartFirstPage is not null)
            {
                await AssertPanesRemovedAsync(restartFirstPage, restartRealizedSurfaces
                    .Where(surface => ReferenceEquals(surface.Pane.Page, restartFirstPage.Page)));
                await DisposePageAsync(restartFirstPage);
            }
            secondAttachment?.Dispose();
            firstAttachment?.Dispose();
            application?.Dispose();
            foreach (var surface in realizedSurfaces)
                await WaitForPaneExitSettlementAsync(surface);
            if (secondPage is not null)
            {
                await AssertPanesRemovedAsync(secondPage, realizedSurfaces
                    .Where(surface => ReferenceEquals(surface.Pane.Page, secondPage.Page)));
                await DisposePageAsync(secondPage);
            }
            if (firstPage is not null)
            {
                await AssertPanesRemovedAsync(firstPage, realizedSurfaces
                    .Where(surface => ReferenceEquals(surface.Pane.Page, firstPage.Page)));
                await DisposePageAsync(firstPage);
            }
            if (Directory.Exists(stateDir))
                Directory.Delete(stateDir, recursive: true);
        }

        Assert.False(Directory.Exists(stateDir));
    }

    private LiveControlHostOptions CreateHostOptions(string stateDir, XtermPage page)
    {
        var settings = new SettingsStore(stateDir).Load();
        return new LiveControlHostOptions
        {
            ConfigDir = stateDir,
            DefaultShell = CommandShell,
            HomeDirectory = stateDir,
            AvailableThemes = ThemeCatalog.Default.Names,
            InitialTheme = LiveThemeSettings.From(settings),
            NotificationSettings = CreateHostSettings(),
            OpenSurface = async (request, cancellationToken) =>
                await _webView.RunAsync(async _ =>
                    await XtermPaneSurface.OpenAsync(page, request.Options, cancellationToken)),
        };
    }

    private static AppSettings CreateHostSettings() => new()
    {
        Theme = "agterm",
        NotificationsEnabled = true,
        NotificationBadgeEnabled = true,
        NotificationSoundName = "default",
    };

    private async Task<PagePresentation> AttachPagePresentationAsync(
        LiveControlHost host,
        TestPage page,
        string stateDir)
    {
        var layoutCoordinator = new TerminalLayoutCoordinator(layout =>
            _webView.RunAsync(_ => page.Page.ApplyLayoutAsync(layout)));
        long layoutGeneration = 0;
        var settingsGate = new object();
        var settingsStore = new SettingsStore(stateDir);
        var settings = settingsStore.Load();
        LiveSelectionSnapshot? selection = null;
        var initialRenderer = LiveThemeSettings.Resolve(
            settings,
            ThemeCatalog.Default,
            systemDark: false).RendererSettings;
        var themeApplication = ApplyRendererSettingsAfterAsync(
            Task.CompletedTask,
            page.Page,
            initialRenderer);

        void OnSelectionChanged(LiveSelectionSnapshot snapshot)
        {
            lock (settingsGate) selection = snapshot;
            layoutCoordinator.Submit(snapshot.ToTerminalLayout(
                Interlocked.Increment(ref layoutGeneration)));
        }

        void OnThemeChanged(HeadlessThemeSnapshot state)
        {
            lock (settingsGate)
            {
                settings = LiveThemeSettings.Apply(settings, state);
                var presentation = LiveThemeSettings.Resolve(
                    settings,
                    ThemeCatalog.Default,
                    systemDark: false,
                    preservePaneFontSizes: true);
                themeApplication = PersistAndApplyRendererSettingsAfterAsync(
                    themeApplication,
                    page.Page,
                    presentation.RendererSettings,
                    settingsStore,
                    settings);
            }
        }

        void OnDashboardHighlighted(string controlRef) =>
            _ = host.HighlightDashboardMemberAsync(controlRef);

        void OnDashboardActivated(string controlRef) =>
            _ = host.ActivateDashboardMemberAsync(controlRef);

        host.StoreCoordinator.SelectionSnapshotChanged += OnSelectionChanged;
        host.ThemeChanged += OnThemeChanged;
        page.Page.DashboardHighlighted += OnDashboardHighlighted;
        page.Page.DashboardActivated += OnDashboardActivated;

        await host.StoreCoordinator.ReadAsync(store =>
        {
            var selectedSessionId = store.SelectedSessionId;
            string? selectedDisplayName = null;
            string? selectedWorkspaceName = null;
            if (selectedSessionId is { } selectedId)
            {
                selectedDisplayName = store.SessionWithId(selectedId)?.DisplayName;
                selectedWorkspaceName = store.WorkspaceForSession(selectedId)?.Name;
            }
            var selectedSession = selectedSessionId is { } selectedIdForLayout
                ? store.SessionWithId(selectedIdForLayout)
                : null;
            OnSelectionChanged(new LiveSelectionSnapshot(
                selectedSessionId,
                selectedDisplayName,
                selectedWorkspaceName,
                selectedSession?.Surface?.PaneToken,
                selectedSession?.SplitSurface?.PaneToken,
                selectedSession?.IsSplit ?? false,
                selectedSession?.SplitFocused ?? false,
                selectedSession?.SplitRatio,
                selectedSession?.ScratchSurface?.PaneToken,
                selectedSession?.ScratchActive ?? false,
                selectedSession?.OverlaySurface?.PaneToken,
                selectedSession?.OverlayActive ?? false,
                selectedSession?.OverlaySizePercent,
                selectedSession?.OverlayBackgroundColor,
                selectedSession?.SearchSurface?.PaneToken,
                selectedSession?.SearchActive ?? false,
                selectedSession?.SearchNeedle ?? "",
                selectedSession?.SearchDisplayText ?? "",
                selectedSession?.SearchTotal,
                selectedSession?.SearchSelected,
                host.Host.Actions.Quick.Surface?.PaneToken,
                host.Host.Actions.Quick.Visible));
            return true;
        });

        return new PagePresentation(
            layoutCoordinator,
            readSettings: () =>
            {
                lock (settingsGate) return settings;
            },
            readSelection: () =>
            {
                lock (settingsGate) return selection;
            },
            readThemeApplication: () =>
            {
                lock (settingsGate) return themeApplication;
            },
            unsubscribe: () =>
            {
                host.StoreCoordinator.SelectionSnapshotChanged -= OnSelectionChanged;
                host.ThemeChanged -= OnThemeChanged;
                page.Page.DashboardHighlighted -= OnDashboardHighlighted;
                page.Page.DashboardActivated -= OnDashboardActivated;
            });
    }

    private async Task ApplyRendererSettingsAfterAsync(
        Task previous,
        XtermPage page,
        RendererSettingsSnapshot settings)
    {
        await previous.ConfigureAwait(false);
        await _webView.RunAsync<object?>(async _ =>
        {
            await page.ApplyRendererSettingsAsync(settings);
            return null;
        });
    }

    private async Task PersistAndApplyRendererSettingsAfterAsync(
        Task previous,
        XtermPage page,
        RendererSettingsSnapshot rendererSettings,
        SettingsStore settingsStore,
        AppSettings settings)
    {
        await ApplyRendererSettingsAfterAsync(previous, page, rendererSettings);
        await _settingsSaveGate.WaitAsync();
        try
        {
            settingsStore.Save(settings);
        }
        finally
        {
            _settingsSaveGate.Release();
        }
    }

    private async Task<TestPage> CreatePageAsync()
    {
        var view = await _webView.CreateWebViewAsync();
        var page = await _webView.RunAsync(async _ =>
        {
            var created = new XtermPage(new Win32ScriptHost(view.CoreWebView2));
            await created.InitializeAsync();
            return created;
        });
        return new TestPage(page, view);
    }

    private async Task AssertPanesRemovedAsync(
        TestPage page,
        IEnumerable<XtermPaneSurface> surfaces)
    {
        foreach (var surface in surfaces)
        {
            var token = JsonSerializer.Serialize(surface.PaneToken);
            var raw = await _webView.RunAsync(async _ =>
                await page.View.CoreWebView2.ExecuteScriptAsync(
                    $"agterm.text({token}, true, null)"));
            Assert.Equal("null", raw);
        }
    }

    private async Task DisposePageAsync(TestPage page)
    {
        await _webView.RunAsync(_ =>
        {
            page.View.Dispose();
            return Task.FromResult(true);
        });
    }

    private async Task<SearchPageState> WaitForSearchStateAsync(
        TestPage page,
        string inspectedPaneId,
        Func<SearchPageState, bool> predicate,
        string failureMessage)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        SearchPageState? last = null;
        while (DateTime.UtcNow < deadline)
        {
            last = await ReadSearchStateAsync(page, inspectedPaneId);
            if (predicate(last))
                return last;
            await Task.Delay(50);
        }
        throw new TimeoutException($"{failureMessage}; last state: {last}");
    }

    private async Task<SearchPageState> ReadSearchStateAsync(
        TestPage page,
        string inspectedPaneId)
    {
        var paneLiteral = JsonSerializer.Serialize(inspectedPaneId);
        var raw = await _webView.RunAsync(async _ =>
            await page.Page.ExecuteScriptAsync(
                $$"""
                (() => {
                    const bar = document.getElementById('search-bar');
                    const input = document.getElementById('search-input');
                    const status = document.getElementById('search-status');
                    const addon = agterm.getSearchState({{paneLiteral}});
                    return {
                        hidden: !bar || bar.hidden,
                        ownerPaneId: bar && !bar.hidden ? (bar.dataset.paneId ?? null) : null,
                        value: input ? input.value : null,
                        status: status ? status.textContent : null,
                        inputFocused: !!input && document.activeElement === input,
                        needle: addon ? addon.needle : null,
                        addonCount: addon ? addon.count : -1,
                        addonSelected: addon ? addon.selected : null,
                        focusedPaneId: document.activeElement
                            ? document.activeElement.closest('[data-pane-id]')?.dataset.paneId ?? null
                            : null,
                    };
                })()
                """));
        return JsonSerializer.Deserialize<SearchPageState>(raw, JsonOptions)!;
    }

    private async Task<PageLayoutState> WaitForPageLayoutAsync(
        TestPage page,
        Func<PageLayoutState, bool> predicate,
        string failureMessage)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        PageLayoutState? last = null;
        while (DateTime.UtcNow < deadline)
        {
            last = await ReadPageLayoutAsync(page);
            if (predicate(last))
                return last;
            await Task.Delay(50);
        }
        throw new TimeoutException($"{failureMessage}; last state: {last}");
    }

    private async Task<PageLayoutState> ReadPageLayoutAsync(TestPage page)
    {
        var raw = await _webView.RunAsync(async _ =>
            await page.Page.ExecuteScriptAsync(
                """
                (() => {
                    const panes = Array.from(document.querySelectorAll('[data-pane-id]'));
                    const highlighted = document.querySelector('.dashboard-highlighted');
                    return {
                        visiblePaneIds: panes
                            .filter(pane => getComputedStyle(pane).display !== 'none')
                            .map(pane => pane.dataset.paneId),
                        registeredPaneIds: panes.map(pane => pane.dataset.paneId),
                        dashboardControlRefs: Array.from(document.querySelectorAll('.dashboard-cell'))
                            .map(pane => pane.dataset.dashboardControlRef),
                        dashboardHighlighted: highlighted
                            ? highlighted.dataset.dashboardControlRef
                            : null,
                        containerDisplay: getComputedStyle(document.getElementById('panes')).display,
                    };
                })()
                """));
        return JsonSerializer.Deserialize<PageLayoutState>(raw, JsonOptions)!;
    }

    private Task DispatchDocumentKeyAsync(TestPage page, string key) =>
        _webView.RunAsync(_ => page.Page.ExecuteScriptAsync(
            $"document.dispatchEvent(new KeyboardEvent('keydown', {{key:{JsonSerializer.Serialize(key)},bubbles:true,cancelable:true}}))"));

    private async Task AssertRendererThemeAsync(
        TestPage page,
        string paneId,
        TerminalTheme expected)
    {
        var paneLiteral = JsonSerializer.Serialize(paneId);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        RendererThemeState? actual = null;
        while (DateTime.UtcNow < deadline)
        {
            var raw = await _webView.RunAsync(async _ =>
                await page.Page.ExecuteScriptAsync(
                    $"agterm.getRendererSettings({paneLiteral})"));
            actual = JsonSerializer.Deserialize<RendererThemeState>(raw, JsonOptions);
            if (actual?.BackgroundColor == expected.BackgroundColor)
                break;
            await Task.Delay(50);
        }

        Assert.NotNull(actual);
        Assert.Equal(expected.ForegroundColor, actual.ForegroundColor);
        Assert.Equal(expected.BackgroundColor, actual.BackgroundColor);
        Assert.Equal(expected.CursorColor, actual.CursorColor);
        Assert.Equal(expected.SelectionBackgroundColor, actual.SelectionBackgroundColor);
        Assert.Equal(expected.AnsiColors, actual.AnsiColors);
    }

    private static Guid InitialSessionId(LiveControlHost host)
    {
        Guid sessionId = default;
        host.Host.RunLocked(() =>
        {
            var workspace = Assert.Single(host.Store.Workspaces);
            sessionId = Assert.Single(workspace.Sessions).Id;
        });
        return sessionId;
    }

    private static async Task<XtermPaneSurface> WaitForSurfaceAsync(
        LiveControlHost host,
        Guid sessionId) =>
        await WaitForSurfaceSlotAsync(
            host,
            () => host.Store.SessionWithId(sessionId)?.Surface,
            $"main pane for session {sessionId:D}");

    private static async Task<XtermPaneSurface> WaitForSurfaceSlotAsync(
        LiveControlHost host,
        Func<ITerminalSurface?> readSlot,
        string slotName)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            XtermPaneSurface? surface = null;
            host.Host.RunLocked(() => surface = readSlot() as XtermPaneSurface);
            if (surface is not null)
                return surface;
            await Task.Delay(50);
        }
        throw new TimeoutException($"{slotName} did not realize a live pane");
    }

    private static async Task<string> WaitForSessionTextAsync(
        ControlPipeClient client,
        Guid windowId,
        Guid sessionId,
        string marker,
        string pane = "left")
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        ControlResponse? response = null;
        while (DateTime.UtcNow < deadline)
        {
            response = client.Send(new ControlRequest
            {
                Cmd = Command.SessionText,
                Target = sessionId.ToString("D"),
                Args = new ControlArgs
                {
                    Window = windowId.ToString("D"),
                    All = true,
                    Pane = pane,
                },
            });
            if (response.Ok && response.Result?.Text?.Contains(marker) == true)
                return response.Result.Text;
            await Task.Delay(100);
        }
        throw new TimeoutException(
            $"session {sessionId:D} pane {pane} did not contain '{marker}'; " +
            $"last error: {response?.Error}; last text: {response?.Result?.Text}");
    }

    private static async Task<string> WaitForQuickTextAsync(
        ControlPipeClient client,
        Guid windowId,
        string marker)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        ControlResponse? response = null;
        while (DateTime.UtcNow < deadline)
        {
            response = client.Send(new ControlRequest
            {
                Cmd = Command.QuickText,
                Args = new ControlArgs
                {
                    All = true,
                    Window = windowId.ToString("D"),
                },
            });
            if (response.Ok && response.Result?.Text?.Contains(marker) == true)
                return response.Result.Text;
            await Task.Delay(100);
        }
        throw new TimeoutException(
            $"window {windowId:D} quick terminal did not contain '{marker}'; " +
            $"last error: {response?.Error}; last text: {response?.Result?.Text}");
    }

    private static async Task<string> WaitForSurfaceTextAsync(
        XtermPaneSurface surface,
        string marker,
        string slotName)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        var text = "";
        while (DateTime.UtcNow < deadline)
        {
            text = surface.ReadScreenText(all: true, lines: null) ?? "";
            if (text.Contains(marker))
                return text;
            await Task.Delay(100);
        }
        throw new TimeoutException(
            $"{slotName} surface {surface.PaneToken} did not contain '{marker}'; last text: {text}");
    }

    private static T ReadLocked<T>(LiveControlHost host, Func<T> read)
    {
        var value = default(T)!;
        host.Host.RunLocked(() => value = read());
        return value;
    }

    private static async Task WaitUntilLockedAsync(
        LiveControlHost host,
        Func<bool> predicate,
        string failureMessage)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (ReadLocked(host, predicate))
                return;
            await Task.Delay(100);
        }
        throw new TimeoutException(failureMessage);
    }

    private static async Task WaitUntilAsync(
        Func<bool> predicate,
        string failureMessage)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
                return;
            await Task.Delay(50);
        }
        throw new TimeoutException(failureMessage);
    }

    private static async Task WaitForPaneExitSettlementAsync(XtermPaneSurface surface)
    {
        try
        {
            await surface.Pane.Exited.WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch
        {
            Assert.True(
                surface.Pane.Exited.IsCompleted,
                $"pane {surface.PaneToken} exit did not settle after teardown");
        }
    }

    private static Guid CreateWorkspace(
        ControlPipeClient client,
        Guid windowId,
        string name)
    {
        var response = Ok(client.Send(new ControlRequest
        {
            Cmd = Command.WorkspaceNew,
            Args = new ControlArgs
            {
                Window = windowId.ToString("D"),
                Name = name,
            },
        }));
        return Guid.Parse(response.Result!.Id!);
    }

    private static Guid CreateBackgroundSession(
        ControlPipeClient client,
        Guid windowId,
        Guid workspaceId,
        string cwd,
        string name)
    {
        var response = Ok(client.Send(new ControlRequest
        {
            Cmd = Command.SessionNew,
            Args = new ControlArgs
            {
                Window = windowId.ToString("D"),
                Workspace = workspaceId.ToString("D"),
                Cwd = cwd,
                Name = name,
                NoSelect = true,
            },
        }));
        return Guid.Parse(response.Result!.Id!);
    }

    private static void AssertWindowState(
        LiveControlHost host,
        Guid backgroundWorkspaceId,
        Guid backgroundSessionId)
    {
        host.Host.RunLocked(() =>
        {
            Assert.Equal(2, host.Store.Workspaces.Count);
            var workspace = host.Store.Workspaces.Single(item => item.Id == backgroundWorkspaceId);
            Assert.Equal(backgroundSessionId, Assert.Single(workspace.Sessions).Id);
            Assert.NotEqual(backgroundSessionId, host.Store.ActiveSession?.Id);
        });
    }

    private static void AssertRestoredWindowState(
        LiveControlHost host,
        Guid expectedSelectedSessionId,
        params RestoredWorkspaceExpectation[] expectedWorkspaces)
    {
        host.Host.RunLocked(() =>
        {
            Assert.Equal(expectedSelectedSessionId, host.Store.SelectedSessionId);
            Assert.Equal(expectedWorkspaces.Length, host.Store.Workspaces.Count);
            for (var index = 0; index < expectedWorkspaces.Length; index++)
            {
                var expected = expectedWorkspaces[index];
                var actual = host.Store.Workspaces[index];
                Assert.Equal(expected.Id, actual.Id);
                Assert.Equal(expected.Name, actual.Name);
                Assert.Equal(
                    expected.SessionIds,
                    actual.Sessions.Select(session => session.Id).ToArray());
            }

            Assert.All(
                host.Store.Workspaces.SelectMany(workspace => workspace.Sessions),
                session =>
                {
                    Assert.Equal(0, session.UnseenCount);
                    Assert.False(session.SplitFocused);
                    Assert.False(session.ScratchActive);
                    Assert.Null(session.ScratchSurface);
                    Assert.False(session.OverlayActive);
                    Assert.Null(session.OverlaySurface);
                    Assert.Null(session.OverlayExitCode);
                    Assert.False(session.SearchActive);
                    Assert.Equal(string.Empty, session.SearchNeedle);
                    Assert.Null(session.SearchTotal);
                    Assert.Null(session.SearchSelected);
                    Assert.Null(session.SearchSurface);
                });
            Assert.False(host.Actions.Quick.Visible);
            Assert.Null(host.Actions.Quick.Surface);
            Assert.Null(host.Actions.Zoom.Target);
            Assert.False(host.Actions.Dashboard.IsOpen);
            Assert.Empty(host.Actions.Dashboard.Members);
            Assert.Null(host.Actions.Dashboard.Highlighted);
        });
        Assert.False(host.ZoomActive);
    }

    private static void AssertWindowEvents(
        ControlEventBatch events,
        Guid windowId,
        Guid backgroundSessionId)
    {
        var expectedWindow = windowId.ToString("D").ToUpperInvariant();
        var expectedSession = backgroundSessionId.ToString("D").ToUpperInvariant();
        Assert.Contains(events.Items, item =>
            item.Kind == ControlEventKind.SessionCreated
            && item.Window == expectedWindow
            && item.Session == expectedSession);
        Assert.Contains(events.Items, item =>
            item.Kind == ControlEventKind.TreeChanged
            && item.Window == expectedWindow);
    }

    private static void AssertNotificationEvent(
        ControlEventBatch events,
        Guid windowId,
        Guid sessionId,
        string title,
        string body)
    {
        var item = Assert.Single(events.Items, item =>
            item.Kind == ControlEventKind.Notify
            && item.Window == windowId.ToString("D").ToUpperInvariant()
            && item.Session == sessionId.ToString("D").ToUpperInvariant());
        Assert.Equal(title, item.Payload.Title);
        Assert.Equal(body, item.Payload.Body);
    }

    private static void AssertThemeState(LiveControlHost host, string expected)
    {
        Assert.Equal(expected, host.Host.Actions.Theme.Light);
        Assert.Null(host.Host.Actions.Theme.Dark);
        Assert.False(host.Host.Actions.Theme.Sync);
    }

    private static ControlTree Tree(ControlPipeClient client, Guid windowId) =>
        Assert.IsType<ControlTree>(Ok(client.Send(new ControlRequest
        {
            Cmd = Command.Tree,
            Args = new ControlArgs { Window = windowId.ToString("D") },
        })).Result?.Tree);

    private static string ControlRef(Guid sessionId, string surface) =>
        $"{sessionId.ToString("D").ToUpperInvariant()}:{surface}";

    private static bool SameSet(IEnumerable<string> actual, IEnumerable<string> expected) =>
        actual.ToHashSet(StringComparer.Ordinal).SetEquals(expected);

    private static ControlResponse Ok(ControlResponse response)
    {
        Assert.True(response.Ok, response.Error);
        return response;
    }

    private static StableWindowStoreState StableStoreState(LiveControlHost host) =>
        ReadLocked(host, () => new StableWindowStoreState(
            host.Store.SelectedSessionId,
            host.Store.Workspaces.Count,
            host.Store.Workspaces.Sum(workspace => workspace.Sessions.Count),
            string.Join("\n", host.Store.Workspaces
                .SelectMany(workspace => workspace.Sessions)
                .Select(session => $"{session.Id:D}\0{session.CustomName}"))));

    private sealed record TestPage(
        XtermPage Page,
        Microsoft.Web.WebView2.WinForms.WebView2 View);

    private sealed record StableWindowStoreState(
        Guid? SelectedSessionId,
        int WorkspaceCount,
        int SessionCount,
        string SessionNames);

    private sealed record RestoredWorkspaceExpectation(
        Guid Id,
        string Name,
        Guid[] SessionIds);

    private sealed record SearchPageState(
        bool Hidden,
        string? OwnerPaneId,
        string? Value,
        string? Status,
        bool InputFocused,
        string? Needle,
        int AddonCount,
        int? AddonSelected,
        string? FocusedPaneId);

    private sealed record PageLayoutState(
        string[] VisiblePaneIds,
        string[] RegisteredPaneIds,
        string[] DashboardControlRefs,
        string? DashboardHighlighted,
        string ContainerDisplay);

    private sealed record RendererThemeState(
        string ForegroundColor,
        string BackgroundColor,
        string CursorColor,
        string SelectionBackgroundColor,
        string[]? AnsiColors);

    private sealed class PagePresentation : IDisposable
    {
        private readonly TerminalLayoutCoordinator _layoutCoordinator;
        private readonly Func<AppSettings> _readSettings;
        private readonly Func<LiveSelectionSnapshot?> _readSelection;
        private readonly Func<Task> _readThemeApplication;
        private readonly Action _unsubscribe;
        private bool _disposed;

        public PagePresentation(
            TerminalLayoutCoordinator layoutCoordinator,
            Func<AppSettings> readSettings,
            Func<LiveSelectionSnapshot?> readSelection,
            Func<Task> readThemeApplication,
            Action unsubscribe)
        {
            _layoutCoordinator = layoutCoordinator;
            _readSettings = readSettings;
            _readSelection = readSelection;
            _readThemeApplication = readThemeApplication;
            _unsubscribe = unsubscribe;
        }

        public AppSettings Settings => _readSettings();

        public LiveSelectionSnapshot? Selection => _readSelection();

        public Task WhenThemeIdleAsync() => _readThemeApplication();

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _unsubscribe();
            _layoutCoordinator.Dispose();
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed class AttachingWindowController(
        Func<Guid, CancellationToken, Task<LiveControlHost>> attach) : ILiveWindowController
    {
        public Guid? OpenedWindowId { get; private set; }

        public LiveControlHost? OpenedHost { get; private set; }

        public async Task OpenWindowAsync(Guid windowId, CancellationToken cancellationToken)
        {
            OpenedWindowId = windowId;
            OpenedHost = await attach(windowId, cancellationToken);
        }

        public Task ActivateWindowAsync(Guid windowId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task CloseWindowAsync(Guid windowId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<LiveWindowPresentation?> CaptureWindowAsync(
            Guid windowId,
            CancellationToken cancellationToken) =>
            Task.FromResult<LiveWindowPresentation?>(null);

        public Task<int> GetDisplayCountAsync(CancellationToken cancellationToken) =>
            Task.FromResult(1);

        public Task ResizeWindowAsync(
            Guid windowId,
            int width,
            int height,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<LiveWindowMoveResult> MoveWindowAsync(
            Guid windowId,
            int x,
            int y,
            int? display,
            CancellationToken cancellationToken) =>
            Task.FromResult(new LiveWindowMoveResult(true, 1));

        public Task ToggleZoomAsync(Guid windowId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ToggleFullscreenAsync(Guid windowId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}