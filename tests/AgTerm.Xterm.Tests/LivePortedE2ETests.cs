using AgTerm.Cli;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// Phase-2 scenarios that were PORT-GAP-skipped in the headless E2E suite because their oracle is
/// a LIVE terminal, re-adapted here at the integration level against the real ConPTY backend (no
/// UI automation — the app window itself stays a manual check). Also encodes the phase-3 DoD:
/// the REAL agtermctl CLI (in-process, real transport) driving type/text/copy against a live
/// session over the pipe.
/// </summary>
[Collection("webview2")]
public sealed class LivePortedE2ETests : IDisposable
{
    private static readonly string Cmd =
        Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";

    private readonly WebView2Fixture _webView;
    private readonly string _stateDir =
        Directory.CreateTempSubdirectory("agterm-live-e2e-").FullName;
    private readonly LiveControlHost _host;
    private readonly Lazy<Task<XtermPage>> _page;

    public LivePortedE2ETests(WebView2Fixture webView)
    {
        _webView = webView;
        // one page per host — the app's one-WebView2-per-window shape
        _page = new Lazy<Task<XtermPage>>(() => _webView.CreatePageAsync());
        _host = new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-live-e2e-" + Guid.NewGuid().ToString("N"),
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

    /// <summary>The upstream ControlAPITestCase.sendCommand seam, but through the REAL CLI:
    /// agtermctl runs in-process with the real ControlPipeClient transport (the DoD seam of the
    /// headless suite, now against live surfaces).</summary>
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

    private string WaitForCtlText(string contains, int timeoutMs = 30000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        var text = "";
        while (Environment.TickCount64 < deadline)
        {
            text = Ctl("session", "text", "--all").Out;
            if (text.Contains(contains)) return text;
            Thread.Sleep(100);
        }
        Assert.Fail($"agtermctl session text never contained \"{contains}\"; last:\n{text}");
        return "";
    }

    // The phase-3 DoD, verbatim: agtermctl session type / text / copy work against a LIVE session
    // over the pipe (real CLI → real named pipe → dispatcher → live AppStore → ConPTY + xterm.js).
    [Fact]
    public void Agtermctl_type_text_copy_drive_a_live_session()
    {
        WaitForCtlText(">");
        CtlOk("session", "type", "echo ctl-dod-marker\n");
        WaitForCtlText("ctl-dod-marker");
        CtlOk("session", "select-all");
        var copied = CtlOk("session", "copy");
        Assert.Contains("ctl-dod-marker", copied);
    }

    // The phase-4 eager deck: in the LIVE app every session is realized at creation (upstream:
    // each deck member's view exists from the start), so session.type reaches a background
    // (--no-select) session WITHOUT a select. Headless keeps the pinned "session not realized"
    // error — this test is the live half of that split.
    [Fact]
    public void Session_type_reaches_a_background_session_without_select()
    {
        WaitForCtlText(">");
        var activeBefore = ActiveSessionId();
        var id = CtlOk("session", "new", "--no-select").Trim();
        Assert.NotEqual(activeBefore, id);

        CtlOk("session", "type", "--target", id, "echo eager-deck-marker\n");
        var deadline = Environment.TickCount64 + 30000;
        var text = "";
        while (Environment.TickCount64 < deadline)
        {
            text = Ctl("session", "text", "--all", "--target", id).Out;
            if (text.Contains("eager-deck-marker")) break;
            Thread.Sleep(100);
        }
        Assert.Contains("eager-deck-marker", text);
        // ...and the background type must not have moved the selection
        Assert.Equal(activeBefore, ActiveSessionId());
    }

    /// <summary>The id of the tree's active session (agtermctl tree → JSON).</summary>
    private string ActiveSessionId()
    {
        var response = System.Text.Json.Nodes.JsonNode.Parse(CtlOk("tree", "--json"))!;
        var tree = response["result"]!["tree"]!;
        foreach (var workspace in tree["workspaces"]!.AsArray())
        {
            foreach (var session in workspace!["sessions"]!.AsArray())
            {
                if (session!["active"]?.GetValue<bool>() == true)
                    return session["id"]!.GetValue<string>();
            }
        }
        Assert.Fail("tree has no active session");
        return "";
    }

    // Port of agtermUITests SessionNewWithCommandFocusesTheNewSession (PORT-GAP-skipped headless):
    // upstream types on the REAL keyboard to prove the new command session took keyboard focus.
    // The keystroke path here is the control channel (no UI automation), so the ported invariant
    // is the CONTROL half of the same behavior: session.new --command focuses the new session, so
    // UNTARGETED typed input lands in the new session's live process — proven by the marker file
    // its shell writes.
    [Fact]
    public void Session_new_with_command_focuses_the_new_session_for_typed_input()
    {
        WaitForCtlText(">"); // the default session is realized and could steal the typing
        var marker = Path.Combine(_stateDir, $"agterm-focus-{Guid.NewGuid():N}.txt");
        CtlOk("session", "new", "--command", $"{Cmd} /k");
        WaitForCtlText(">"); // the new pane's prompt (it is now the active session)

        CtlOk("session", "type", $"echo FOCUSED> \"{marker}\"\n"); // no --target: active session
        var deadline = Environment.TickCount64 + 30000;
        string? content = null;
        while (Environment.TickCount64 < deadline)
        {
            if (File.Exists(marker))
            {
                try
                {
                    content = File.ReadAllText(marker).Trim();
                    if (content == "FOCUSED") return;
                }
                catch (IOException)
                {
                    // cmd.exe still holds the file open from its redirect
                }
            }
            Thread.Sleep(100);
        }
        Assert.Fail($"typed text never reached the new command session's process; marker: {content ?? "<missing>"}");
    }
}
