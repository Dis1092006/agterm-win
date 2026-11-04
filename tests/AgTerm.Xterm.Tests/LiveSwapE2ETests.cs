using AgTerm.Cli;
using AgTerm.Core.Model;
using AgTerm.Xterm;
using System.Text.Json;

namespace AgTerm.Xterm.Tests;

/// <summary>session.swap against real ConPTY panes: the live terminals trade slots, and every
/// per-slot behavior (exit, hold, text addressing) follows the slot a pane sits in now.</summary>
[Collection("webview2")]
public sealed class LiveSwapE2ETests : IDisposable
{
    private static readonly string Cmd =
        Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";

    private readonly WebView2Fixture _webView;
    private readonly string _stateDir =
        Directory.CreateTempSubdirectory("agterm-live-swap-").FullName;
    private readonly LiveControlHost _host;
    private readonly Lazy<Task<XtermPage>> _page;

    public LiveSwapE2ETests(WebView2Fixture webView)
    {
        _webView = webView;
        _page = new Lazy<Task<XtermPage>>(() => _webView.CreatePageAsync());
        _host = new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-live-swap-" + Guid.NewGuid().ToString("N"),
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

    private void WaitForText(string contains, string pane)
    {
        var deadline = Environment.TickCount64 + 30000;
        var text = "";
        while (Environment.TickCount64 < deadline)
        {
            text = CtlOk("session", "text", "--all", "--pane", pane);
            if (text.Contains(contains)) return;
            Thread.Sleep(100);
        }
        Assert.Fail($"the {pane} pane never showed \"{contains}\"; last:\n{text}");
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

    private void SplitOn()
    {
        WaitForText(">", "left");
        CtlOk("session", "split", "on");
        WaitForText(">", "right");
    }

    [Fact]
    public void Swapping_hands_each_terminal_to_the_other_slot()
    {
        SplitOn();
        CtlOk("session", "type", "--pane", "left", "echo FROMLEFT\n");
        WaitForText("FROMLEFT", "left");
        var (leftToken, rightToken) = ReadStore(store =>
            (store.ActiveSession!.Surface!.PaneToken!, store.ActiveSession.SplitSurface!.PaneToken!));

        CtlOk("session", "swap");

        WaitForText("FROMLEFT", "right");
        var (afterLeft, afterRight) = ReadStore(store =>
            (store.ActiveSession!.Surface!.PaneToken!, store.ActiveSession.SplitSurface!.PaneToken!));
        Assert.Equal(rightToken, afterLeft);
        Assert.Equal(leftToken, afterRight);
    }

    [Fact]
    public void A_split_made_a_moment_ago_can_be_swapped_at_once()
    {
        WaitForText(">", "left");
        CtlOk("session", "split", "on");

        CtlOk("session", "swap");

        WaitForText(">", "right");
    }

    [Fact]
    public async Task A_held_shell_in_the_swapped_split_slot_closes_the_split_not_the_session()
    {
        SplitOn();
        CtlOk("session", "swap");
        var (sessionId, moved) = ReadStore(store =>
            (store.ActiveSession!.Id, store.ActiveSession.SplitSurface!.PaneToken!));

        CtlOk("session", "type", "--pane", "right", "exit 4\n");
        WaitForText("[process exited with code 4; press any key to close]", "right");
        Assert.True(ReadStore(store => store.SessionWithId(sessionId)!.HasSplit));

        await KeyInPaneAsync(moved, "a");
        await WaitUntilAsync(store => store.SessionWithId(sessionId) is { HasSplit: false },
            "the keystroke did not close the swapped split pane");
        Assert.NotNull(ReadStore(store => store.SessionWithId(sessionId)));
    }
}
