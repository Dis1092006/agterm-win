using AgTerm.App;
using AgTerm.ControlServer;
using AgTerm.Core.Keymap;
using AgTerm.Core.Model;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

[Collection("webview2")]
public sealed class LiveCustomCommandTests
{
    private readonly WebView2Fixture _webView;

    public LiveCustomCommandTests(WebView2Fixture webView)
    {
        _webView = webView;
    }

    [Fact]
    public async Task Custom_single_and_leader_emit_stable_id_generation_and_completing_chord()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("custom-live"));
        var singleId = Guid.NewGuid();
        var leaderId = Guid.NewGuid();
        var observed = new List<CustomHotkeyEvent>();
        page.CustomCommandPressed += observed.Add;

        await _webView.RunAsync<object?>(async _ =>
        {
            await page.SetHotkeyManifestAsync(new HotkeyManifest(
                3,
                500,
                [],
                [
                    new(singleId, [new(true, false, false, "k")]),
                    new(leaderId,
                    [
                        new(true, false, false, "a"),
                        new(false, false, false, "b"),
                    ]),
                ]));
            return null;
        });

        await DispatchAsync(view, "custom-live", "k", ctrl: true);
        await DispatchAsync(view, "custom-live", "a", ctrl: true);
        await DispatchAsync(view, "custom-live", "b");
        await WaitUntilAsync(() => observed.Count == 2, TimeSpan.FromSeconds(5));

        Assert.Equal(singleId, observed[0].CommandId);
        Assert.Equal(new(true, false, false, "k"), observed[0].Chord);
        Assert.Equal(leaderId, observed[1].CommandId);
        Assert.Equal(new(false, false, false, "b"), observed[1].Chord);
        Assert.All(observed, hit =>
        {
            Assert.Equal("custom-live", hit.PaneId);
            Assert.Equal(3, hit.Generation);
        });
    }

    [Fact]
    public async Task Atomic_replacement_cancels_armed_old_leader_and_installs_new_binding()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("custom-reload"));
        var oldId = Guid.NewGuid();
        var newId = Guid.NewGuid();
        var observed = new List<CustomHotkeyEvent>();
        page.CustomCommandPressed += observed.Add;

        await SetManifestAsync(page, new HotkeyManifest(
            1,
            1000,
            [],
            [new(oldId, [new(true, false, false, "a"), new(false, false, false, "b")])]));
        await DispatchAsync(view, "custom-reload", "a", ctrl: true);

        await SetManifestAsync(page, new HotkeyManifest(
            2,
            1000,
            [],
            [new(newId, [new(true, false, false, "x")])]));
        await DispatchAsync(view, "custom-reload", "b");
        await DispatchAsync(view, "custom-reload", "x", ctrl: true);
        await WaitUntilAsync(() => observed.Count == 1, TimeSpan.FromSeconds(5));

        var hit = Assert.Single(observed);
        Assert.Equal(newId, hit.CommandId);
        Assert.Equal(2, hit.Generation);
    }

    [Fact]
    public async Task Leader_timeout_releases_the_next_ordinary_key_to_terminal_data()
    {
        var (page, view) = await BootPageAsync();
        var terminal = await _webView.RunAsync(_ => page.OpenTerminalAsync("custom-timeout"));
        var commandId = Guid.NewGuid();
        var observed = new List<CustomHotkeyEvent>();
        var data = new List<string>();
        page.CustomCommandPressed += observed.Add;
        terminal.DataReceived += data.Add;

        await SetManifestAsync(page, new HotkeyManifest(
            1,
            100,
            [],
            [new(commandId, [new(true, false, false, "a"), new(false, false, false, "b")])]));
        await DispatchAsync(view, "custom-timeout", "a", ctrl: true);
        await Task.Delay(200);
        await DispatchAsync(view, "custom-timeout", "x");
        await WaitUntilAsync(() => data.Any(chunk => chunk.Contains('x')), TimeSpan.FromSeconds(5));

        Assert.Empty(observed);
    }

    [Fact]
    public async Task Armed_custom_completion_wins_over_builtin_root_chord()
    {
        var (page, view) = await BootPageAsync();
        await _webView.RunAsync(_ => page.OpenTerminalAsync("custom-priority"));
        var commandId = Guid.NewGuid();
        var custom = new List<CustomHotkeyEvent>();
        var builtins = new List<PhysicalChord>();
        page.CustomCommandPressed += custom.Add;
        page.ChordPressed += (_, chord) => builtins.Add(chord);

        await SetManifestAsync(page, new HotkeyManifest(
            1,
            1000,
            [new(true, false, false, "n")],
            [new(commandId,
            [
                new(true, false, false, "a"),
                new(true, false, false, "n"),
            ])]));

        await DispatchAsync(view, "custom-priority", "a", ctrl: true);
        await DispatchAsync(view, "custom-priority", "n", ctrl: true);
        await WaitUntilAsync(() => custom.Count + builtins.Count > 0, TimeSpan.FromSeconds(5));

        Assert.Equal(commandId, Assert.Single(custom).CommandId);
        Assert.Empty(builtins);
    }

    private Task SetManifestAsync(XtermPage page, HotkeyManifest manifest) =>
        _webView.RunAsync<object?>(async _ =>
        {
            await page.SetHotkeyManifestAsync(manifest);
            return null;
        });

    private Task DispatchAsync(
        Microsoft.Web.WebView2.WinForms.WebView2 view,
        string paneId,
        string key,
        bool ctrl = false,
        bool alt = false,
        bool shift = false) =>
        _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync(
            $"agterm.dispatchKey({XtermPage.PaneLiteral(paneId)}, {{key:{XtermPage.PaneLiteral(key)},ctrlKey:{ctrl.ToString().ToLowerInvariant()},altKey:{alt.ToString().ToLowerInvariant()},shiftKey:{shift.ToString().ToLowerInvariant()}}})"));

    private async Task<(XtermPage Page, Microsoft.Web.WebView2.WinForms.WebView2 View)> BootPageAsync()
    {
        var view = await _webView.CreateWebViewAsync();
        var page = await _webView.RunAsync(async _ =>
        {
            var created = new XtermPage(new Win32ScriptHost(view.CoreWebView2));
            await created.InitializeAsync();
            return created;
        });
        return (page, view);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("Condition not met within the timeout.");
            await Task.Delay(25);
        }
    }
}

public sealed class CustomCommandInvocationTests
{
    [Fact]
    public void Hotkey_resolves_exact_split_surface_context_and_preserves_quoted_Windows_path()
    {
        var store = TestStores.Scratch();
        var workspace = store.AddWorkspace("Workspace");
        var session = store.AddSession(workspace.Id, @"C:\work tree")!;
        session.CustomName = "Shell";
        session.Surface = new FakeSurface("left-pane", "left selection");
        session.SplitSurface = new FakeSurface("right-pane", "right selection");
        session.SplitFocused = false;
        var command = new CustomCommand(
            "Run",
            "pwsh -File \"{AGT_SESSION_PWD}\\deploy.ps1\" --pane {AGT_PANE}",
            "ctrl+k",
            Guid.NewGuid());
        var keymap = new Keymap(new Dictionary<BuiltinAction, Chord>(), [command]);
        var window = new CustomCommandWindowContext(
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            "main",
            "agterm-pipe",
            HotkeyGeneration: 4);

        var invocation = CustomCommandInvocationResolver.Resolve(
            store,
            keymap,
            new CustomCommandExecutionRequest(
                command.Id,
                CustomCommandSource.Hotkey,
                PaneId: "right-pane",
                HotkeyGeneration: 4,
                Selection: "right selection"),
            window);

        Assert.NotNull(invocation);
        Assert.Equal(
            "pwsh -File \"C:\\work tree\\deploy.ps1\" --pane right",
            invocation!.ShellLine);
        Assert.Equal(@"C:\work tree", invocation.WorkingDirectory);
        Assert.Equal("right selection", invocation.Environment["AGT_SELECTION"]);
        Assert.Equal("right", invocation.Environment["AGT_PANE"]);
        Assert.Equal("agterm-pipe", invocation.Environment["AGT_SOCKET"]);
        Assert.Equal(session.Id.ToString("D").ToUpperInvariant(), invocation.Environment["AGT_SESSION_ID"]);
    }

    [Fact]
    public void Hotkey_from_the_split_pane_exports_the_split_working_directory()
    {
        var store = TestStores.Scratch();
        var workspace = store.AddWorkspace("Workspace");
        var session = store.AddSession(workspace.Id, @"C:\left")!;
        session.Surface = new FakeSurface("left-pane", "");
        session.SplitSurface = new FakeSurface("right-pane", "");
        session.SplitCwd = @"D:\logs";
        var command = new CustomCommand("Run", "echo {AGT_SESSION_PWD}", "ctrl+k", Guid.NewGuid());
        var keymap = new Keymap(new Dictionary<BuiltinAction, Chord>(), [command]);
        var window = new CustomCommandWindowContext(Guid.NewGuid(), "main", "pipe", 1);

        var right = CustomCommandInvocationResolver.Resolve(
            store,
            keymap,
            new(command.Id, CustomCommandSource.Hotkey, PaneId: "right-pane", HotkeyGeneration: 1),
            window)!;
        var left = CustomCommandInvocationResolver.Resolve(
            store,
            keymap,
            new(command.Id, CustomCommandSource.Hotkey, PaneId: "left-pane", HotkeyGeneration: 1),
            window)!;

        Assert.Equal(@"echo D:\logs", right.ShellLine);
        Assert.Equal(@"D:\logs", right.WorkingDirectory);
        Assert.Equal(@"D:\logs", right.Environment["AGT_SESSION_PWD"]);
        Assert.Equal(@"C:\left", left.WorkingDirectory);
    }

    [Fact]
    public void Stale_hotkey_generation_and_missing_command_are_ignored()
    {
        var command = new CustomCommand("Run", "echo ok", "ctrl+k", Guid.NewGuid());
        var keymap = new Keymap(new Dictionary<BuiltinAction, Chord>(), [command]);
        var window = new CustomCommandWindowContext(Guid.NewGuid(), "main", "pipe", 3);

        Assert.Null(CustomCommandInvocationResolver.Resolve(
            TestStores.Scratch(),
            keymap,
            new(command.Id, CustomCommandSource.Hotkey, HotkeyGeneration: 2),
            window));
        Assert.Null(CustomCommandInvocationResolver.Resolve(
            TestStores.Scratch(),
            keymap,
            new(Guid.NewGuid(), CustomCommandSource.Hotkey, HotkeyGeneration: 3),
            window));
    }

    [Fact]
    public void Sessionless_hotkey_allows_launcher_but_rejects_session_scoped_command_and_palette()
    {
        var launcher = new CustomCommand(
            "Open",
            "agtermctl --socket {AGT_SOCKET} session new",
            "ctrl+k",
            Guid.NewGuid());
        var dangerous = new CustomCommand(
            "Danger",
            "remove \"{AGT_SESSION_PWD}\"",
            "ctrl+d",
            Guid.NewGuid());
        var keymap = new Keymap(new Dictionary<BuiltinAction, Chord>(), [launcher, dangerous]);
        var window = new CustomCommandWindowContext(Guid.NewGuid(), "main", "literal-pipe", 1);

        var allowed = CustomCommandInvocationResolver.Resolve(
            TestStores.Scratch(),
            keymap,
            new(launcher.Id, CustomCommandSource.Hotkey, HotkeyGeneration: 1),
            window);
        var blocked = CustomCommandInvocationResolver.Resolve(
            TestStores.Scratch(),
            keymap,
            new(dangerous.Id, CustomCommandSource.Hotkey, HotkeyGeneration: 1),
            window);
        var palette = CustomCommandInvocationResolver.Resolve(
            TestStores.Scratch(),
            keymap,
            new(launcher.Id, CustomCommandSource.Palette),
            window);

        Assert.NotNull(allowed);
        Assert.Equal("agtermctl --socket literal-pipe session new", allowed!.ShellLine);
        Assert.Equal(string.Empty, allowed.WorkingDirectory);
        Assert.Null(blocked);
        Assert.Null(palette);
    }

    [Fact]
    public void Palette_and_hotkey_resolve_identical_active_pane_context()
    {
        var store = TestStores.Scratch();
        var workspace = store.AddWorkspace("Work");
        var session = store.AddSession(workspace.Id, @"C:\work")!;
        session.Surface = new FakeSurface("main-pane", "selected");
        var command = new CustomCommand(
            "Run",
            "echo {AGT_SESSION_ID} {AGT_PANE} {AGT_SELECTION}",
            "ctrl+k",
            Guid.NewGuid());
        var keymap = new Keymap(new Dictionary<BuiltinAction, Chord>(), [command]);
        var window = new CustomCommandWindowContext(Guid.NewGuid(), "main", "pipe", 5);

        var palette = CustomCommandInvocationResolver.Resolve(
            store,
            keymap,
            new(command.Id, CustomCommandSource.Palette, PaneId: "main-pane", Selection: "selected"),
            window);
        var hotkey = CustomCommandInvocationResolver.Resolve(
            store,
            keymap,
            new(command.Id, CustomCommandSource.Hotkey, "main-pane", 5, "selected"),
            window);

        Assert.NotNull(palette);
        Assert.NotNull(hotkey);
        Assert.Equal(palette!.CommandId, hotkey!.CommandId);
        Assert.Equal(palette.Name, hotkey.Name);
        Assert.Equal(palette.ShellLine, hotkey.ShellLine);
        Assert.Equal(palette.WorkingDirectory, hotkey.WorkingDirectory);
        Assert.Equal(
            palette.Environment.OrderBy(pair => pair.Key),
            hotkey.Environment.OrderBy(pair => pair.Key));
    }

    [Fact]
    public void ProcessStartInfo_uses_pwsh_first_with_opaque_line_environment_and_working_directory()
    {
        var invocation = Invocation();

        var startInfo = CustomCommandProcessRunner.CreateStartInfo(
            invocation,
            which: name => name == "pwsh.exe" ? @"C:\Tools\pwsh.exe" : null,
            env: _ => @"C:\Windows\System32\cmd.exe");

        Assert.Equal(@"C:\Tools\pwsh.exe", startInfo.FileName);
        Assert.Equal(["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", invocation.ShellLine],
            startInfo.ArgumentList);
        Assert.Equal(invocation.WorkingDirectory, startInfo.WorkingDirectory);
        Assert.Equal("pipe", startInfo.Environment["AGT_SOCKET"]);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
    }

    [Fact]
    public void ProcessStartInfo_falls_back_to_COMSPEC_with_separate_opaque_argument()
    {
        var invocation = Invocation();

        var startInfo = CustomCommandProcessRunner.CreateStartInfo(
            invocation,
            which: _ => null,
            env: key => key == "COMSPEC" ? @"C:\Windows\System32\cmd.exe" : null);

        Assert.Equal(@"C:\Windows\System32\cmd.exe", startInfo.FileName);
        Assert.Empty(startInfo.ArgumentList);
        Assert.Equal($"/d /c \"{invocation.ShellLine}\"", startInfo.Arguments);
    }

    [Fact]
    public void ProcessStartInfo_skips_windows_powershell_and_falls_back_to_COMSPEC()
    {
        var invocation = Invocation();

        var startInfo = CustomCommandProcessRunner.CreateStartInfo(
            invocation,
            which: name => name == "powershell.exe"
                ? @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"
                : null,
            env: key => key == "COMSPEC" ? @"C:\Windows\System32\cmd.exe" : null);

        Assert.Equal(@"C:\Windows\System32\cmd.exe", startInfo.FileName);
        Assert.Equal($"/d /c \"{invocation.ShellLine}\"", startInfo.Arguments);
    }

    [Fact]
    public async Task ProcessRunner_names_the_missing_session_directory_before_launch()
    {
        var missing = Path.Combine(Path.GetTempPath(), "agterm-missing-" + Guid.NewGuid().ToString("N"));
        var invocation = new CustomCommandInvocation(Guid.NewGuid(), "Mark", "echo ok", missing,
            new Dictionary<string, string>());
        var runner = new CustomCommandProcessRunner(which: _ => null,
            env: key => key == "COMSPEC" ? Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe" : null);

        var error = await Assert.ThrowsAsync<CustomCommandNotStartedException>(() => runner.StartAsync(invocation));
        Assert.Equal($"session directory {missing} no longer exists", error.Message);
    }

    [Fact]
    public void Not_started_notice_names_the_command_and_the_reason()
    {
        var notice = CustomCommandFailureNotice.NotStarted("Mark", @"session directory C:\gone no longer exists");
        Assert.Equal("Command failed", notice.Title);
        Assert.Equal(@"Mark (session directory C:\gone no longer exists)", notice.Message);
    }

    /// <summary>How long a spawned child is given. The shell line below ends in `ping -n 3`, which
    /// sleeps about 2.2 seconds on an idle machine, so a 5 second budget left under 3 seconds for
    /// spawning cmd.exe and draining its output. The solution runs its test assemblies in parallel
    /// and the live WebView2 suite loads the machine, so that ran out: this test timed out in 4 of
    /// 4 full-suite runs while passing on its own. Same budget and same reason as
    /// ConPtySessionTests.ChildBudget. A hung child still fails the test, it just takes a minute.
    /// </summary>
    private static readonly TimeSpan ChildBudget = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task ProcessRunner_executes_with_environment_and_quoted_Windows_path()
    {
        var directory = Directory.CreateTempSubdirectory("agterm-custom runner-");
        var marker = Path.Combine(directory.FullName, "marker file.txt");
        try
        {
            var invocation = new CustomCommandInvocation(
                Guid.NewGuid(),
                "Write marker",
                $"echo %AGT_SOCKET%> \"{marker}\" & ping 127.0.0.1 -n 3 > nul",
                directory.FullName,
                new Dictionary<string, string> { ["AGT_SOCKET"] = "pipe-value" });
            var runner = new CustomCommandProcessRunner(
                which: _ => null,
                env: key => key == "COMSPEC"
                    ? Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe"
                    : null);

            var handle = await runner.StartAsync(invocation);
            Assert.True(handle.ProcessId > 0);

            var deadline = DateTime.UtcNow + ChildBudget;
            while (!File.Exists(marker) && DateTime.UtcNow < deadline)
                await Task.Delay(25);
            Assert.True(File.Exists(marker));
            await handle.Exited.WaitAsync(ChildBudget);
            Assert.Equal("pipe-value", File.ReadAllText(marker).Trim());
        }
        finally
        {
            try { directory.Delete(recursive: true); } catch (IOException) { }
        }
    }

    private static CustomCommandInvocation Invocation() => new(
        Guid.NewGuid(),
        "Run",
        "echo \"C:\\work tree\" & echo %AGT_SOCKET%",
        @"C:\work tree",
        new Dictionary<string, string> { ["AGT_SOCKET"] = "pipe" });

    private sealed class FakeSurface(string token, string selectedText) : IHeadlessSurface
    {
        public string? PaneToken => token;
        public bool Inject(string text) => true;
        public string? ReadScreenText(bool all, int? lines) => string.Empty;
        public string? SelectedText => selectedText;
        public void SelectAll() { }
        public bool Paste() => true;
        public bool PerformBindingAction(string action) => true;
        public void PromoteToPrimaryPane() { }
        public void Teardown() { }
    }
}

public sealed class MainWindowCustomCommandWiringTests
{
    [Fact]
    public void MainWindow_uses_one_active_keymap_for_manifest_palette_and_execution()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AgTerm.App", "MainWindow.xaml.cs"));

        Assert.Contains("_hotkeyStates.Prepare(keymap)", source, StringComparison.Ordinal);
        Assert.Contains("_hotkeyStates.Commit(pending.Generation)", source, StringComparison.Ordinal);
        Assert.Contains("_hotkeyStates.Reject(pending.Generation)", source, StringComparison.Ordinal);
        Assert.Contains("CreateManifest", source, StringComparison.Ordinal);
        Assert.Contains("page.SetHotkeyManifestAsync", source, StringComparison.Ordinal);
        Assert.Contains("page.BuiltinHotkeyPressed +=", source, StringComparison.Ordinal);
        Assert.Contains("page.CustomCommandPressed +=", source, StringComparison.Ordinal);
        Assert.Contains("ExecuteCustomCommandAsync", source, StringComparison.Ordinal);
        Assert.Contains("CustomCommandSource.Hotkey", source, StringComparison.Ordinal);
        Assert.Contains("CustomCommandSource.Palette", source, StringComparison.Ordinal);
        Assert.Contains("WindowPaletteAcceptance.AcceptAsync", source, StringComparison.Ordinal);
        Assert.Contains("CustomCommandInvocationResolver.Resolve", source, StringComparison.Ordinal);
        Assert.Contains("customCommandProcess = await _customCommandRunner.StartAsync", source,
            StringComparison.Ordinal);
        Assert.Contains("ObserveTaskFault(customCommandProcess.Exited,", source, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not find the repository root.");
    }
}

public sealed class HotkeyGenerationRegistryTests
{
    [Fact]
    public void Prepare_keeps_old_active_and_resolves_pending_until_acknowledged_commit()
    {
        var registry = new HotkeyGenerationRegistry();
        var oldKeymap = KeymapWith("old");
        var nextKeymap = KeymapWith("next");
        var old = registry.Prepare(oldKeymap);
        registry.Commit(old.Generation);

        var pending = registry.Prepare(nextKeymap);

        Assert.Same(oldKeymap, registry.Active.Keymap);
        Assert.Same(oldKeymap, registry.Resolve(old.Generation)?.Keymap);
        Assert.Same(nextKeymap, registry.Resolve(pending.Generation)?.Keymap);

        registry.Commit(pending.Generation);

        Assert.Same(nextKeymap, registry.Active.Keymap);
        Assert.Null(registry.Resolve(old.Generation));
        Assert.Same(nextKeymap, registry.Resolve(pending.Generation)?.Keymap);
    }

    [Fact]
    public void Reject_failed_pending_manifest_keeps_the_previous_active_generation()
    {
        var registry = new HotkeyGenerationRegistry();
        var active = registry.Prepare(KeymapWith("active"));
        registry.Commit(active.Generation);
        var pending = registry.Prepare(KeymapWith("pending"));

        registry.Reject(pending.Generation);

        Assert.Equal(active.Generation, registry.Active.Generation);
        Assert.NotNull(registry.Resolve(active.Generation));
        Assert.Null(registry.Resolve(pending.Generation));
    }

    private static Keymap KeymapWith(string name) => new(
        new Dictionary<BuiltinAction, Chord>(),
        [new CustomCommand(name, $"echo {name}", "", Guid.NewGuid())]);
}