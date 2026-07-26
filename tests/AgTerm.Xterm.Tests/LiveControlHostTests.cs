using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// The live control host end-to-end: real requests over a REAL named pipe → dispatcher → live
/// AppStore whose sessions realize REAL surfaces (ConPTY cmd.exe ↔ xterm.js in a hidden WebView2).
/// This is the app's control plane minus WinUI. cmd.exe drives; each test gets an isolated state
/// dir + pipe name.
/// </summary>
[Collection("webview2")]
public sealed class LiveControlHostTests : IDisposable
{
    private static readonly string Cmd =
        Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";

    private readonly WebView2Fixture _webView;
    private readonly string _stateDir =
        Directory.CreateTempSubdirectory("agterm-live-host-").FullName;
    private readonly List<LiveControlHost> _hosts = new();

    public LiveControlHostTests(WebView2Fixture webView)
    {
        _webView = webView;
    }

    public void Dispose()
    {
        foreach (var host in _hosts) host.Dispose();
        try
        {
            Directory.Delete(_stateDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private (LiveControlHost Host, ControlPipeClient Client) StartHost()
    {
        var host = new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-live-" + Guid.NewGuid().ToString("N"),
            DefaultShell = Cmd,
            HomeDirectory = _stateDir,
            OpenSurface = async (_, surfaceOptions) =>
            {
                var view = await _webView.CreateWebViewAsync();
                return await _webView.RunAsync(async v2 =>
                    await XtermPaneSurface.OpenAsync(new Win32ScriptHost(view.CoreWebView2), surfaceOptions));
            },
        });
        _hosts.Add(host);
        host.Start();
        return (host, new ControlPipeClient(host.PipeName));
    }

    private static ControlResponse Ok(ControlResponse response)
    {
        Assert.True(response.Ok, $"expected ok, got error: {response.Error}");
        return response;
    }

    private static string WaitForText(ControlPipeClient client, string contains,
                                      string? target = null, int timeoutMs = 30000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        var text = "";
        while (Environment.TickCount64 < deadline)
        {
            var response = client.Send(new ControlRequest
            {
                Cmd = Command.SessionText,
                Target = target,
                Args = new ControlArgs { All = true },
            });
            text = response.Result?.Text ?? "";
            if (text.Contains(contains)) return text;
            Thread.Sleep(100);
        }
        Assert.Fail($"session.text never contained \"{contains}\"; last:\n{text}");
        return "";
    }

    [Fact]
    public void Type_text_copy_run_against_the_live_default_session()
    {
        var (_, client) = StartHost();
        WaitForText(client, ">"); // the default session realized and its cmd prompt painted

        Ok(client.Send(new ControlRequest
        {
            Cmd = Command.SessionType,
            Args = new ControlArgs { Text = "echo live-ctl-marker\n" },
        }));
        WaitForText(client, "live-ctl-marker");

        Ok(client.Send(new ControlRequest { Cmd = Command.SessionSelectAll }));
        var copied = Ok(client.Send(new ControlRequest { Cmd = Command.SessionCopy }));
        Assert.Contains("live-ctl-marker", copied.Result!.Text);
    }

    [Fact]
    public void Session_new_with_command_realizes_the_argv_child()
    {
        var (_, client) = StartHost();
        var created = Ok(client.Send(new ControlRequest
        {
            Cmd = Command.SessionNew,
            // the shell path rides UNQUOTED — backslashes in Windows paths must survive tokenization
            Args = new ControlArgs { Cwd = _stateDir, Command = $"{Cmd} /k echo cmd-mode-live-ok" },
        }));
        var id = created.Result!.Id!;
        WaitForText(client, "cmd-mode-live-ok", target: id);
    }

    [Fact]
    public void Shell_exit_closes_the_session()
    {
        var (host, client) = StartHost();
        var created = Ok(client.Send(new ControlRequest
        {
            Cmd = Command.SessionNew,
            Args = new ControlArgs { Cwd = _stateDir },
        }));
        var id = Guid.Parse(created.Result!.Id!);
        WaitForText(client, ">", target: id.ToString());

        Ok(client.Send(new ControlRequest
        {
            Cmd = Command.SessionType,
            Target = id.ToString(),
            Args = new ControlArgs { Text = "exit\n" },
        }));
        var deadline = Environment.TickCount64 + 30000;
        while (Environment.TickCount64 < deadline)
        {
            var gone = false;
            host.Host.RunLocked(() => gone = host.Host.Store.SessionWithId(id) is null);
            if (gone) return;
            Thread.Sleep(100);
        }
        Assert.Fail("the exited session never left the store");
    }

    [Fact]
    public void Osc_title_reaches_the_tree_read_back()
    {
        var (_, client) = StartHost();
        WaitForText(client, ">");
        Ok(client.Send(new ControlRequest
        {
            Cmd = Command.SessionType,
            Args = new ControlArgs { Text = "title live-tree-title\n" },
        }));
        var deadline = Environment.TickCount64 + 30000;
        string? seen = null;
        while (Environment.TickCount64 < deadline)
        {
            var tree = Ok(client.Send(new ControlRequest { Cmd = Command.Tree }));
            seen = tree.Result!.Tree!.Workspaces.SelectMany(w => w.Sessions).First().Title;
            if (seen is not null && seen.Contains("live-tree-title")) return;
            Thread.Sleep(100);
        }
        Assert.Fail($"tree title never carried the marker; last: {seen}");
    }

    [Fact]
    public void Sessions_survive_a_host_restart()
    {
        var (host, client) = StartHost();
        var created = Ok(client.Send(new ControlRequest
        {
            Cmd = Command.SessionNew,
            Args = new ControlArgs { Cwd = _stateDir, Name = "keeper" },
        }));
        var id = created.Result!.Id!;
        WaitForText(client, ">", target: id);
        host.Dispose(); // flushes the snapshot

        var (restarted, client2) = StartHost();
        Session? restored = null;
        restarted.Host.RunLocked(() =>
            restored = restarted.Host.Store.SessionWithId(Guid.Parse(id)));
        Assert.NotNull(restored);
        Assert.Equal("keeper", restored!.CustomName);
        Assert.Equal(_stateDir, restored.InitialCwd);
        // the launch-selected session must realize at bootstrap (upstream: the window shows it) —
        // a live shell answers session.text right after the restart
        WaitForText(client2, ">");
    }
}
