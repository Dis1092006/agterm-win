using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Xunit;

namespace AgTerm.E2E;

/// <summary>
/// The Definition-of-Done smoke: agtermctl (tree / session new / session.status / events) driven
/// against the headless host over the real named pipe — in-process for the fast paths, plus one
/// scenario spawning the actual built agtermctl.exe binary.
/// </summary>
public class AgtermctlEndToEndTests : ControlApiTestCase
{
    [Fact]
    public async Task Pick_waits_for_the_answer_and_prints_it()
    {
        var stdout = new StringWriter();
        var run = Task.Run(() => RunCtlWithStdin(new[] { "pick" }, "alpha" + Environment.NewLine + "beta" + Environment.NewLine, stdout));
        string? id = null;
        for (var i = 0; i < 200 && id is null; i++)
        {
            id = Host.Actions.Pick.PendingId;
            if (id is null) await Task.Delay(25);
        }
        Assert.NotNull(id);

        Host.Actions.Pick.Resolve(id!, AgTerm.Core.Protocol.ControlPickResult.Picked("beta", "beta", 1));

        Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("{\"result\":\"picked\",\"id\":\"beta\",\"label\":\"beta\",\"index\":1}", stdout.ToString().Trim());
    }

    [Fact]
    public async Task Ask_waits_for_the_answer_and_prints_it()
    {
        var stdout = new StringWriter();
        var run = Task.Run(() => RunCtlTo(new[] { "ask", "Deploy?", "--button", "yes=Yes", "--button", "no", "--style", "gui" }, stdout));
        var id = await PendingAskIdAsync(() => Host.Actions.Pick.PendingAsk?.Id);

        Host.Actions.Pick.ResolveAsk(id, AgTerm.Core.Protocol.ControlAskResult.Answered("no", "no", 1));

        Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("{\"result\":\"answered\",\"id\":\"no\",\"label\":\"no\",\"index\":1}", stdout.ToString().Trim());
    }

    [Fact]
    public async Task Ask_exits_three_when_the_user_dismisses_a_terminal_question()
    {
        var stdout = new StringWriter();
        var run = Task.Run(() => RunCtlTo(new[] { "ask", "Deploy?", "--button", "yes" }, stdout));
        Guid sessionId = default;
        var id = await PendingAskIdAsync(() =>
        {
            string? found = null;
            Host.RunLocked(() =>
            {
                var session = Host.Store.ActiveSession;
                sessionId = session?.Id ?? default;
                found = session?.AskPending?.Id;
            });
            return found;
        });

        Host.RunLocked(() => Host.Store.ResolveAsk(sessionId, id, AgTerm.Core.Protocol.ControlAskResult.Escaped()));

        Assert.Equal(3, await run.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("{\"result\":\"escaped\"}", stdout.ToString().Trim());
    }

    [Fact]
    public async Task Ask_exits_two_when_another_client_cancels_it()
    {
        var stdout = new StringWriter();
        var run = Task.Run(() => RunCtlTo(new[] { "ask", "Deploy?", "--button", "yes", "--style", "gui" }, stdout));
        var id = await PendingAskIdAsync(() => Host.Actions.Pick.PendingAsk?.Id);

        Assert.Equal(0, RunCtl("ask", "cancel", id).Exit);

        Assert.Equal(2, await run.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("{\"result\":\"cancelled\"}", stdout.ToString().Trim());
    }

    private static async Task<string> PendingAskIdAsync(Func<string?> read)
    {
        for (var i = 0; i < 200; i++)
        {
            if (read() is { } id) return id;
            await Task.Delay(25);
        }
        Assert.Fail("no ask became pending");
        return "";
    }

    [Fact]
    public void TreeSessionNewStatusAndEventsRoundTripThroughTheCli()
    {
        // session new prints the fresh id (a create command echoes result.id).
        var created = RunCtl("session", "new", "--cwd", StateDir, "--name", "api");
        Assert.Equal(0, created.Exit);
        var id = created.Out.Trim();
        Assert.True(Guid.TryParse(id, out _), $"expected a session uuid, got: {created.Out}");

        // tree lists the new session by name and marks it active.
        var tree = RunCtl("tree");
        Assert.Equal(0, tree.Exit);
        Assert.Contains("* api", tree.Out);
        Assert.Contains(id, tree.Out);

        // session.status drives the ephemeral indicator; the tree read-back reports it.
        var status = RunCtl("session", "status", "blocked", "--target", id[..8], "--blink");
        Assert.Equal(0, status.Exit);
        var node = FindSession(Tree(), id);
        Assert.Equal("blocked", node["status"]?.GetValue<string>());
        Assert.Equal(true, node["statusBlink"]?.GetValue<bool>());

        // events.read over the raw pipe: the bootstrap anchors at the tail, the cursor then sees
        // the status event emitted by a second status change.
        var bootstrap = SendOk("""{"cmd":"events.read"}""");
        var events = bootstrap["events"]!.AsObject();
        var run = events["run"]!.GetValue<string>();
        var next = events["next"]!.GetValue<ulong>();

        Assert.Equal(0, RunCtl("session", "status", "completed", "--target", id[..8]).Exit);
        var page = SendOk(
            $$$"""{"cmd":"events.read","args":{"run":"{{{run}}}","after":"{{{next}}}","kinds":["status"]}}""");
        var items = page["events"]!["items"]!.AsArray();
        Assert.True(items.Count >= 1, "expected at least the completed status event");
        Assert.Equal("status", items[^1]!["kind"]!.GetValue<string>());
        Assert.Equal("completed", items[^1]!["payload"]!["status"]!.GetValue<string>());
    }

    [Fact]
    public void FailedResolutionExitsOneWithThePinnedError()
    {
        var missing = RunCtl("session", "select", "--target", "zzzz");
        Assert.Equal(1, missing.Exit);
        Assert.Contains("no such session: zzzz", missing.Error);
    }

    [Fact]
    public void TheBuiltAgtermctlExeDrivesTheHostOverTheRealPipe()
    {
        var exe = LocateAgtermctlExe();
        Assert.True(File.Exists(exe), $"agtermctl.exe not built at {exe} — run dotnet build first");

        var create = RunExe(exe, "session new --cwd . --name from-exe");
        Assert.Equal(0, create.Exit);
        Assert.True(Guid.TryParse(create.Out.Trim(), out _), $"expected a uuid, got: {create.Out}");

        var tree = RunExe(exe, "tree --json");
        Assert.Equal(0, tree.Exit);
        var response = JsonNode.Parse(tree.Out)!.AsObject();
        Assert.True(response["ok"]!.GetValue<bool>());
        var sessions = response["result"]!["tree"]!["workspaces"]!.AsArray()
            .SelectMany(w => w!["sessions"]!.AsArray())
            .Select(s => s!["name"]!.GetValue<string>());
        Assert.Contains("from-exe", sessions);
    }

    private (int Exit, string Out, string Error) RunExe(string exe, string arguments)
    {
        var info = new ProcessStartInfo(exe, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.Environment["AGTERM_CONTROL_SOCKET"] = Host.PipeName;
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(30_000), "agtermctl.exe did not exit");
        return (process.ExitCode, stdout, stderr);
    }

    private static string LocateAgtermctlExe()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "AgTerm.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        Assert.NotNull(dir);

        // Look in the build output roots rather than pinning one layout: the SDK artifacts output
        // layout (UseArtifactsOutput) puts the exe under artifacts\bin\AgTerm.Cli\<pivot>\, while a
        // plain per-project build puts it under src\AgTerm.Cli\bin\<config>\<tfm>\.
        //
        // A cross-compiled exe must be skipped, not merely deprioritized: publishing for another
        // architecture (an ARM64 publish on an x64 host) leaves an agtermctl.exe under these same
        // roots, and starting it fails with "not a valid application for this OS platform". Any path
        // naming a foreign architecture is therefore excluded, and the newest survivor wins.
        var foreign = (RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "x64" : "arm64");
        var roots = new[]
        {
            Path.Combine(dir!, "artifacts", "bin", "AgTerm.Cli"),
            Path.Combine(dir!, "src", "AgTerm.Cli", "bin"),
        };
        var candidate = roots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "agtermctl.exe", SearchOption.AllDirectories))
            .Where(path => !path.Contains(foreign, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        return candidate ?? Path.Combine(roots[0], "debug", "agtermctl.exe");
    }

    private static JsonObject FindSession(JsonObject treeResult, string id)
    {
        foreach (var workspace in treeResult["tree"]!["workspaces"]!.AsArray())
        {
            foreach (var session in workspace!["sessions"]!.AsArray())
            {
                if (session!["id"]!.GetValue<string>() == id) return session.AsObject();
            }
        }
        Assert.Fail($"session {id} not in tree: {treeResult.ToJsonString()}");
        return null!;
    }
}
