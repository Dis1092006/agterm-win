using AgTerm.Cli;
using AgTerm.Core.Model;
using AgTerm.Xterm;
using System.Text.Json;

namespace AgTerm.Xterm.Tests;

/// <summary>Live coverage for closeOnShellExit: a shell pane whose shell exits with a code the
/// setting does not close on stays open with the code, and the next keystroke closes it.</summary>
[Collection("webview2")]
public sealed class LiveShellExitE2ETests : IDisposable
{
    private static readonly string Cmd =
        Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";

    private readonly WebView2Fixture _webView;
    private readonly string _stateDir =
        Directory.CreateTempSubdirectory("agterm-live-shell-exit-").FullName;
    private readonly LiveControlHost _host;
    private readonly Lazy<Task<XtermPage>> _page;

    public LiveShellExitE2ETests(WebView2Fixture webView)
    {
        _webView = webView;
        _page = new Lazy<Task<XtermPage>>(() => _webView.CreatePageAsync());
        _host = new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-live-shell-exit-" + Guid.NewGuid().ToString("N"),
            DefaultShell = Cmd,
            HomeDirectory = _stateDir,
            OpenSurface = async (request, cancellationToken) =>
            {
                var shared = await _page.Value;
                return await _webView.RunAsync(async _ =>
                    await XtermPaneSurface.OpenAsync(shared, request.Options, cancellationToken));
            },
        });
        _host.Start();
    }

    public void Dispose()
    {
        _host.Dispose();
        try
        {
            Directory.Delete(_stateDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string CtlOk(params string[] argv)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = Agtermctl.Run(argv, new CliEnvironment
        {
            GetEnv = key => key == "AGTERM_CONTROL_SOCKET" ? _host.PipeName : null,
            Out = stdout,
            Error = stderr,
        });
        Assert.True(exit == 0, $"agtermctl {string.Join(' ', argv)} failed ({exit}): {stderr}");
        return stdout.ToString();
    }

    private void WaitForText(string contains, string? pane = null)
    {
        var deadline = Environment.TickCount64 + 30000;
        var text = "";
        string[] args = pane is null
            ? ["session", "text", "--all"]
            : ["session", "text", "--all", "--pane", pane];
        while (Environment.TickCount64 < deadline)
        {
            text = CtlOk(args);
            if (text.Contains(contains)) return;
            Thread.Sleep(100);
        }
        Assert.Fail($"session text never contained \"{contains}\"; last:\n{text}");
    }

    private T ReadStore<T>(Func<AppStore, T> read)
    {
        T value = default!;
        _host.Host.RunLocked(() => value = read(_host.Store));
        return value;
    }

    private async Task WaitUntilAsync(Func<AppStore, bool> condition, string failure)
    {
        var deadline = Environment.TickCount64 + 30000;
        while (!ReadStore(condition) && Environment.TickCount64 < deadline)
            await Task.Delay(50);
        Assert.True(ReadStore(condition), failure);
    }

    private async Task KeyInPaneAsync(string paneId, string key)
    {
        var page = await _page.Value;
        await _webView.RunAsync(_ => page.ExecuteScriptAsync(
            $"agterm.dispatchKey({JsonSerializer.Serialize(paneId)}, {{key:{JsonSerializer.Serialize(key)}}})"));
    }

    [Fact]
    public async Task A_main_shell_that_exits_with_an_error_stays_open_until_a_keystroke()
    {
        WaitForText(">");
        var (sessionId, main) = ReadStore(store => (store.ActiveSession!.Id, store.ActiveSession.Surface!.PaneToken!));

        CtlOk("session", "type", "exit 3\n");
        WaitForText("[process exited with code 3; press any key to close]");
        Assert.NotNull(ReadStore(store => store.SessionWithId(sessionId)));

        await KeyInPaneAsync(main, "a");
        await WaitUntilAsync(store => store.SessionWithId(sessionId) is null,
            "the keystroke did not close the held session");
    }

    [Fact]
    public async Task Always_closes_a_main_shell_that_exits_with_an_error()
    {
        WaitForText(">");
        // the app applies a reloaded settings.json the same way (MainWindow.ApplySettingsAsync)
        _host.SetNotificationSettings(new AppSettings { CloseOnShellExit = "always" });
        var sessionId = ReadStore(store => store.ActiveSession!.Id);

        CtlOk("session", "type", "exit 3\n");
        await WaitUntilAsync(store => store.SessionWithId(sessionId) is null,
            "always did not close the session on exit code 3");
    }

    [Fact]
    public async Task A_split_shell_that_exits_with_an_error_stays_open_until_a_keystroke()
    {
        WaitForText(">");
        var splitRealized = new TaskCompletionSource<XtermPaneSurface>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _host.SurfaceRealized += (request, surface) =>
        {
            if (request.Kind == LiveSurfaceKind.Split)
                splitRealized.TrySetResult(surface);
        };
        CtlOk("session", "split", "on");
        var split = await splitRealized.Task.WaitAsync(TimeSpan.FromSeconds(15));
        WaitForText(">", pane: "right");
        var sessionId = ReadStore(store => store.ActiveSession!.Id);

        CtlOk("session", "type", "--pane", "right", "exit 4\n");
        WaitForText("[process exited with code 4; press any key to close]", pane: "right");
        Assert.True(ReadStore(store => store.SessionWithId(sessionId)!.HasSplit));

        await KeyInPaneAsync(split.PaneToken!, "a");
        await WaitUntilAsync(store => store.SessionWithId(sessionId) is { HasSplit: false },
            "the keystroke did not close the held split");
    }

    [Fact]
    public async Task A_command_session_without_wait_still_closes_when_its_command_fails()
    {
        WaitForText(">");
        var sessionId = Guid.Parse(CtlOk("session", "new", "--command", "cmd /c exit 5").Trim());

        await WaitUntilAsync(store => store.SessionWithId(sessionId) is null,
            "a --command session without --wait was held open");
    }
}
