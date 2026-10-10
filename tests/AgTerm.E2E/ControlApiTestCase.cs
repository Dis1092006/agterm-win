using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using AgTerm.ControlServer;
using Xunit;

namespace AgTerm.E2E;

/// <summary>
/// The E2E fixture — the port of agtermUITests ControlAPITestCase: a headless host (AppStore +
/// dispatcher + pipe server in one process) stands in for the launched app, with an isolated
/// state dir (the AGTERM_STATE_DIR analogue) and a per-test pipe (the AGTERM_CONTROL_SOCKET
/// analogue). SendCommand ships one VERBATIM newline-JSON request line over a fresh connection
/// and parses the single response line — scenario request literals stay byte-identical to the
/// upstream Swift.
/// </summary>
public abstract class ControlApiTestCase : IDisposable
{
    protected readonly string StateDir;
    protected readonly HeadlessHost Host;

    protected ControlApiTestCase()
    {
        StateDir = Path.Combine(Path.GetTempPath(), "agterm-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(StateDir);
        Host = new HeadlessHost(new HeadlessHostOptions
        {
            StateDir = StateDir,
            ConfigDir = StateDir,
            PipeName = "agterm-e2e-" + Guid.NewGuid().ToString("N"),
        });
        Host.Start();
    }

    public void Dispose()
    {
        Host.Stop();
        try
        {
            Directory.Delete(StateDir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Connect to the host's control pipe, send the line (newline-terminated), read the
    /// single response line, and parse it as a JSON object — the upstream sendCommand.</summary>
    protected JsonObject SendCommand(string line)
    {
        using var pipe = new NamedPipeClientStream(".", Host.PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous);
        pipe.Connect(10_000);
        pipe.Write(Encoding.UTF8.GetBytes(line + "\n"));
        pipe.Flush();
        var response = ReadLine(pipe);
        var node = JsonNode.Parse(response);
        Assert.True(node is JsonObject, $"response should be a JSON object, got: {response}");
        return (JsonObject)node!;
    }

    /// <summary>SendCommand asserting ok: true, returning the result object (empty when absent).</summary>
    protected JsonObject SendOk(string line)
    {
        var response = SendCommand(line);
        Assert.True(response["ok"]?.GetValue<bool>(),
            $"expected ok response for {line}, got: {response.ToJsonString()}");
        return response["result"] as JsonObject ?? new JsonObject();
    }

    /// <summary>The current tree result — the read-back most scenarios assert against.</summary>
    protected JsonObject Tree() => SendOk("""{"cmd":"tree"}""");

    /// <summary>Runs the REAL agtermctl CLI in-process against the host's pipe (the transport is
    /// the real ControlPipeClient), capturing stdout/stderr — the DoD seam.</summary>
    protected (int Exit, string Out, string Error) RunCtl(params string[] argv)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = AgTerm.Cli.Agtermctl.Run(argv, new AgTerm.Cli.CliEnvironment
        {
            GetEnv = key => key == "AGTERM_CONTROL_SOCKET" ? Host.PipeName : null,
            Out = stdout,
            Error = stderr,
        });
        return (exit, stdout.ToString(), stderr.ToString());
    }

    /// <summary>RunCtl for a command that reads its items from stdin (pick): the parsed command gets
    /// the given text as UTF-8 bytes. Takes the writer so a test can read output while the run blocks.</summary>
    protected int RunCtlWithStdin(string[] argv, string stdin, TextWriter stdout)
    {
        var parsed = (AgTerm.Cli.PickCommands.Open)AgTerm.Cli.Agtermctl.ParseAsRoot(argv);
        var command = new AgTerm.Cli.PickCommands.Open
        {
            Prompt = parsed.Prompt,
            Query = parsed.Query,
            Select = parsed.Select,
            AllowCustom = parsed.AllowCustom,
            Follow = parsed.Follow,
            NoBlock = parsed.NoBlock,
            Client = parsed.Client,
            StdinStream = () => new MemoryStream(Encoding.UTF8.GetBytes(stdin)),
        };
        return command.Execute(new AgTerm.Cli.CliEnvironment
        {
            GetEnv = key => key == "AGTERM_CONTROL_SOCKET" ? Host.PipeName : null,
            Out = stdout,
            Error = new StringWriter(),
        });
    }

    private static string ReadLine(PipeStream pipe)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        while (true)
        {
            var n = pipe.Read(chunk);
            if (n == 0) break;
            var newline = Array.IndexOf(chunk, (byte)'\n', 0, n);
            if (newline >= 0)
            {
                buffer.Write(chunk, 0, newline);
                break;
            }
            buffer.Write(chunk, 0, n);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
