using System.Text.Json.Nodes;
using AgTerm.Cli;
using AgTerm.Core.Model;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// The phase-4 DoD, end to end against LIVE surfaces (one WebView2 page, many ConPTY panes):
/// everything agtermctl can do to the sidebar reads back in tree/events instantly, the UI seam
/// (SidebarController — the same layer the window drives) mirrors and produces the same state,
/// session.type reaches background sessions without a select, and the sidebar chrome survives a
/// host restart from the same state dir.
/// </summary>
[Collection("webview2")]
public sealed class LiveSidebarE2ETests : IDisposable
{
    private static readonly string Cmd =
        Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";

    private readonly WebView2Fixture _webView;
    private readonly string _stateDir =
        Directory.CreateTempSubdirectory("agterm-live-sidebar-").FullName;
    private readonly List<LiveControlHost> _hosts = new();
    private LiveControlHost _host = null!;

    public LiveSidebarE2ETests(WebView2Fixture webView)
    {
        _webView = webView;
        _host = StartHost();
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

    private LiveControlHost StartHost()
    {
        var page = new Lazy<Task<XtermPage>>(() => _webView.CreatePageAsync());
        var host = new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-live-sb-" + Guid.NewGuid().ToString("N"),
            DefaultShell = Cmd,
            HomeDirectory = _stateDir,
            OpenSurface = async (_, options) =>
            {
                var shared = await page.Value;
                return await _webView.RunAsync(async _ => await XtermPaneSurface.OpenAsync(shared, options));
            },
        });
        _hosts.Add(host);
        host.Start();
        return host;
    }

    private (int Exit, string Out, string Error) Ctl(params string[] argv)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = Agtermctl.Run(argv, new CliEnvironment
        {
            GetEnv = key => key == "AGTERM_CONTROL_SOCKET" ? _host.PipeName : null,
            Out = stdout,
            Error = stderr,
        });
        return (exit, stdout.ToString(), stderr.ToString());
    }

    private string CtlOk(params string[] argv)
    {
        var (exit, output, error) = Ctl(argv);
        Assert.True(exit == 0, $"agtermctl {string.Join(' ', argv)} failed ({exit}): {error}");
        return output;
    }

    private JsonNode Tree()
    {
        var response = JsonNode.Parse(CtlOk("tree", "--json"))!;
        return response["result"]!["tree"]!;
    }

    private static JsonNode? SessionNode(JsonNode tree, string id)
    {
        foreach (var workspace in tree["workspaces"]!.AsArray())
        {
            foreach (var session in workspace!["sessions"]!.AsArray())
            {
                if (string.Equals(session!["id"]!.GetValue<string>(), id,
                        StringComparison.OrdinalIgnoreCase)) return session;
            }
        }
        return null;
    }

    private static JsonNode WorkspaceNode(JsonNode tree, string id) =>
        tree["workspaces"]!.AsArray()
            .Single(w => string.Equals(w!["id"]!.GetValue<string>(), id,
                StringComparison.OrdinalIgnoreCase))!;

    [Fact]
    public void Sidebar_dod_scenario_control_to_ui_and_back_with_restart()
    {
        // events.read bootstrap: no cursor = read from the CURRENT tail (§11) — take the cursor
        // before the churn so the final read pages through everything this test caused
        var client = new AgTerm.ControlServer.ControlPipeClient(_host.PipeName);
        var bootstrap = client.Send(new AgTerm.Core.Protocol.ControlRequest
        {
            Cmd = AgTerm.Core.Protocol.Command.EventsRead,
        });
        Assert.True(bootstrap.Ok, bootstrap.Error);
        var run = bootstrap.Result!.Events!.Run.ToString();
        var after = bootstrap.Result!.Events!.Next.ToString();

        // ---- build: 2 workspaces, sessions in both (one background) ----
        var w2 = CtlOk("workspace", "new", "beta").Trim();
        var s1 = CtlOk("session", "new", "--workspace", w2, "--name", "api").Trim();
        var s2 = CtlOk("session", "new", "--workspace", w2, "--no-select").Trim();

        // the UI seam observes through the SAME store the socket mutates
        var snapshots = new List<SidebarSnapshot>();
        var controller = new SidebarController(_host.Host, runOnUi: work => work());
        controller.SnapshotChanged += snapshots.Add;
        controller.Start();
        SidebarSnapshot Latest() => snapshots[^1];

        Assert.Contains(Latest().Rows, r => r.Kind == SidebarRowKind.Session &&
                                            r.Id == Guid.Parse(s1) && r.Label == "api");

        // ---- eager deck: type into the background session, no select ----
        CtlOk("session", "type", "--target", s2, "echo sidebar-dod-eager\n");
        var deadline = Environment.TickCount64 + 30000;
        var text = "";
        while (Environment.TickCount64 < deadline)
        {
            text = Ctl("session", "text", "--all", "--target", s2).Out;
            if (text.Contains("sidebar-dod-eager")) break;
            Thread.Sleep(100);
        }
        Assert.Contains("sidebar-dod-eager", text);
        Assert.Equal(Guid.Parse(s1), Latest().SelectedSessionId);

        // ---- every ctl sidebar verb reads back in tree AND lands in the UI snapshot ----
        CtlOk("session", "rename", "renamed-api", "--target", s1);
        Assert.Equal("renamed-api", SessionNode(Tree(), s1)!["name"]!.GetValue<string>());
        Assert.Contains(Latest().Rows, r => r.Id == Guid.Parse(s1) && r.Label == "renamed-api");

        CtlOk("session", "flag", "on", "--target", s1);
        Assert.True(SessionNode(Tree(), s1)!["flagged"]!.GetValue<bool>());
        Assert.True(Latest().Rows.Single(r => r.Id == Guid.Parse(s1)).Flagged);

        CtlOk("workspace", "focus", "on", "--target", w2);
        Assert.True(WorkspaceNode(Tree(), w2)["focused"]!.GetValue<bool>());
        Assert.Equal(Guid.Parse(w2), Latest().FocusedWorkspaceId);

        CtlOk("workspace", "collapse", "--target", w2);
        Assert.True(WorkspaceNode(Tree(), w2)["collapsed"]!.GetValue<bool>());
        Assert.True(Latest().Rows.Single(r => r.Id == Guid.Parse(w2)).Collapsed);
        CtlOk("workspace", "expand", "--target", w2);

        CtlOk("sidebar", "mode", "flagged");
        Assert.Equal("flagged", Tree()["sidebarMode"]!.GetValue<string>());
        Assert.Equal(SidebarMode.Flagged, Latest().Mode);
        Assert.All(Latest().Rows, r => Assert.Equal(SidebarRowKind.Session, r.Kind));
        CtlOk("sidebar", "mode", "tree");

        var move = CtlOk("session", "move", "--target", s2, "--to", "top");
        Assert.NotNull(move);

        // ---- and the reverse: UI gestures read back over the wire ----
        controller.RenameSession(Guid.Parse(s2), "from-ui");
        Assert.Equal("from-ui", SessionNode(Tree(), s2)!["name"]!.GetValue<string>());
        controller.SetFlag(new[] { Guid.Parse(s2) }, on: true);
        Assert.True(SessionNode(Tree(), s2)!["flagged"]!.GetValue<bool>());
        controller.SetSidebarWidth(333);
        controller.SetSidebarVisible(false);

        // ---- events: the ring recorded the structural churn (events.read over the pipe; the
        // CLI's `events` verb is the endless follow loop) ----
        var response = client.Send(new AgTerm.Core.Protocol.ControlRequest
        {
            Cmd = AgTerm.Core.Protocol.Command.EventsRead,
            Args = new AgTerm.Core.Protocol.ControlArgs { Run = run, After = after, Limit = 1000 },
        });
        Assert.True(response.Ok, response.Error);
        var kinds = response.Result!.Events!.Items.Select(e => e.Kind).ToHashSet();
        Assert.Contains(AgTerm.Core.Protocol.ControlEventKind.SessionCreated, kinds);
        Assert.Contains(AgTerm.Core.Protocol.ControlEventKind.TreeChanged, kinds);

        controller.Dispose();

        // ---- restart from the same state dir: sidebar chrome + tree survive ----
        _host.Dispose();
        _host = StartHost();
        var restored = Tree();
        Assert.Equal("from-ui", SessionNode(restored, s2)!["name"]!.GetValue<string>());
        Assert.True(SessionNode(restored, s2)!["flagged"]!.GetValue<bool>());
        Assert.True(WorkspaceNode(restored, w2)["focused"]!.GetValue<bool>());
        _host.RunLockedForTest(store =>
        {
            Assert.Equal(333, store.SidebarWidth);
            Assert.False(store.SidebarVisible);
            Assert.Equal(Guid.Parse(w2), store.FocusedWorkspaceId);
        });

        // the restored deck is eager: the background session still types without a select
        CtlOk("session", "type", "--target", s2, "echo survived-restart\n");
        deadline = Environment.TickCount64 + 30000;
        while (Environment.TickCount64 < deadline)
        {
            text = Ctl("session", "text", "--all", "--target", s2).Out;
            if (text.Contains("survived-restart")) break;
            Thread.Sleep(100);
        }
        Assert.Contains("survived-restart", text);
    }
}

internal static class LiveControlHostTestExtensions
{
    /// <summary>Read store state under the host gate (assertion helper).</summary>
    public static void RunLockedForTest(this LiveControlHost host, Action<AppStore> read) =>
        host.Host.RunLocked(() => read(host.Host.Store));
}
