using System.IO.Pipes;
using System.Text;
using AgTerm.ControlServer;
using AgTerm.Core.Protocol;
using Xunit;

namespace AgTerm.ControlServer.Tests;

public class ControlPipeNameTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(@"C:\tmp\state-a")]
    public void Resolved_socket_path_derives_to_the_resolved_pipe(string? stateDir)
    {
        Func<string, string?> env = key => key == "AGTERM_STATE_DIR" ? stateDir : null;

        var path = ControlPipeName.ResolveSocketPath(env);

        Assert.NotNull(path);
        Assert.Equal(ControlPipeName.Resolve(env), ControlPipeName.Derive(path!));
    }

    [Fact]
    public void Override_has_no_socket_path()
    {
        Func<string, string?> env = key => key switch
        {
            "AGTERM_CONTROL_SOCKET" => "my-test-pipe",
            "AGTERM_STATE_DIR" => @"C:\tmp\state-a",
            _ => null,
        };

        Assert.Null(ControlPipeName.ResolveSocketPath(env));
    }

    [Fact]
    public void Empty_override_is_not_an_override_for_the_socket_path_either()
    {
        // Resolve ignores an empty AGTERM_CONTROL_SOCKET, so the path must follow the same rule.
        Func<string, string?> env = key => key == "AGTERM_CONTROL_SOCKET" ? "" : null;

        Assert.Equal(ControlPipeName.Resolve(env), ControlPipeName.Derive(ControlPipeName.ResolveSocketPath(env)!));
    }

    [Fact]
    public void Advertised_socket_path_requires_a_match()
    {
        const string path = @"C:\tmp\state-a\agterm.sock";

        Assert.Equal(path, ControlPipeName.AdvertisedSocketPath(ControlPipeName.Derive(path), path));
        Assert.Null(ControlPipeName.AdvertisedSocketPath("my-test-pipe", path));
        Assert.Null(ControlPipeName.AdvertisedSocketPath(ControlPipeName.Derive(""), ""));
        Assert.Null(ControlPipeName.AdvertisedSocketPath("my-test-pipe", null));
    }

    [Fact]
    public void DerivedNameIsDeterministicAndPrefixed()
    {
        var a = ControlPipeName.Derive(@"C:\Users\u\AppData\Local\agterm\agterm.sock");
        var b = ControlPipeName.Derive(@"C:\Users\u\AppData\Local\agterm\agterm.sock");
        var other = ControlPipeName.Derive(@"C:\other\agterm.sock");

        Assert.Equal(a, b);
        Assert.StartsWith("agterm-", a);
        Assert.NotEqual(a, other);
        // a pipe name must not contain path separators or characters invalid in the pipe namespace.
        Assert.DoesNotContain('\\', a);
        Assert.DoesNotContain('/', a);
    }

    [Fact]
    public void ExplicitOverrideWinsVerbatim()
    {
        var name = ControlPipeName.Resolve(key => key == "AGTERM_CONTROL_SOCKET" ? "my-test-pipe" : null);
        Assert.Equal("my-test-pipe", name);
    }

    [Fact]
    public void StateDirDrivesTheDerivedName()
    {
        var a = ControlPipeName.Resolve(key => key == "AGTERM_STATE_DIR" ? @"C:\tmp\state-a" : null);
        var b = ControlPipeName.Resolve(key => key == "AGTERM_STATE_DIR" ? @"C:\tmp\state-b" : null);
        Assert.NotEqual(a, b);
        Assert.Equal(ControlPipeName.Derive(Path.Combine(@"C:\tmp\state-a", "agterm.sock")), a);
    }
}

public class ControlPipeServerTests
{
    private static string FreshPipe() => "agterm-test-" + Guid.NewGuid().ToString("N");

    private static ControlPipeServer StartEcho(string pipe,
        Func<ControlRequest, Task<ControlResponse?>>? dispatch = null)
    {
        var server = new ControlPipeServer(pipe, dispatch ?? (_ =>
            Task.FromResult<ControlResponse?>(new ControlResponse { Ok = true })));
        server.Start();
        return server;
    }

    [Fact]
    public async Task RoundTripsOneRequestPerConnection()
    {
        var pipe = FreshPipe();
        using var server = StartEcho(pipe, request => Task.FromResult<ControlResponse?>(
            new ControlResponse { Ok = true, Result = new ControlResult { Id = request.Target } }));
        var client = new ControlPipeClient(pipe);

        var response = client.Send(new ControlRequest { Cmd = Command.SessionSelect, Target = "abc" });

        Assert.True(response.Ok);
        Assert.Equal("abc", response.Result?.Id);
    }

    [Fact]
    public async Task ServesSequentialClients()
    {
        var pipe = FreshPipe();
        var count = 0;
        using var server = StartEcho(pipe, _ =>
        {
            var n = Interlocked.Increment(ref count);
            return Task.FromResult<ControlResponse?>(new ControlResponse
            {
                Ok = true, Result = new ControlResult { Count = n },
            });
        });
        var client = new ControlPipeClient(pipe);

        var first = client.Send(new ControlRequest { Cmd = Command.Tree });
        var second = client.Send(new ControlRequest { Cmd = Command.Tree });
        var third = client.Send(new ControlRequest { Cmd = Command.Tree });

        Assert.Equal(1, first.Result?.Count);
        Assert.Equal(2, second.Result?.Count);
        Assert.Equal(3, third.Result?.Count);
    }

    [Fact]
    public async Task MalformedJsonYieldsInvalidRequestError()
    {
        var pipe = FreshPipe();
        using var server = StartEcho(pipe);

        var line = await SendRaw(pipe, "this is not json\n");

        var response = ControlJson.Deserialize<ControlResponse>(line)!;
        Assert.False(response.Ok);
        Assert.StartsWith("invalid request: ", response.Error);
    }

    [Fact]
    public async Task UnknownCommandYieldsInvalidRequestErrorNotACrash()
    {
        var pipe = FreshPipe();
        using var server = StartEcho(pipe);

        var line = await SendRaw(pipe, """{"cmd":"nope.nope"}""" + "\n");

        var response = ControlJson.Deserialize<ControlResponse>(line)!;
        Assert.False(response.Ok);
        Assert.StartsWith("invalid request: ", response.Error);

        // and the server is still alive for the next connection.
        var client = new ControlPipeClient(pipe);
        Assert.True(client.Send(new ControlRequest { Cmd = Command.Tree }).Ok);
    }

    [Fact]
    public async Task OversizedLineIsRejected()
    {
        var pipe = FreshPipe();
        using var server = StartEcho(pipe);
        var huge = new string('x', (1 << 20) + 64) + "\n";

        var line = await SendRaw(pipe, huge);

        var response = ControlJson.Deserialize<ControlResponse>(line)!;
        Assert.False(response.Ok);
        Assert.Equal("request too large or read failed", response.Error);
    }

    [Fact]
    public async Task NullDispatcherResultMapsToUiTestOnlySeamError()
    {
        // debug.appearance is the one command the dispatcher declines (returns null); the transport
        // must answer with the pinned upstream refusal instead of hanging or crashing.
        var pipe = FreshPipe();
        using var server = StartEcho(pipe, _ => Task.FromResult<ControlResponse?>(null));
        var client = new ControlPipeClient(pipe);

        var response = client.Send(new ControlRequest { Cmd = Command.DebugAppearance });

        Assert.False(response.Ok);
        Assert.Equal("debug.appearance is a UI-test-only seam", response.Error);
    }

    [Fact]
    public async Task ConnectionClosesAfterTheSingleResponse()
    {
        var pipe = FreshPipe();
        using var server = StartEcho(pipe);
        using var raw = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
        await raw.ConnectAsync(5000);
        var payload = Encoding.UTF8.GetBytes("""{"cmd":"tree"}""" + "\n");
        await raw.WriteAsync(payload);

        using var reader = new StreamReader(raw, Encoding.UTF8, leaveOpen: true);
        var line = await reader.ReadLineAsync();
        Assert.NotNull(line);
        // after the one response the server disconnects: the next read reports end-of-stream.
        var after = await reader.ReadLineAsync();
        Assert.Null(after);
    }

    [Fact]
    public void ClientReportsAMissingServer()
    {
        var client = new ControlPipeClient("agterm-test-no-such-pipe-" + Guid.NewGuid().ToString("N"),
            connectTimeoutMs: 300);

        var error = Assert.Throws<PipeClientException>(
            () => client.Send(new ControlRequest { Cmd = Command.Tree }));

        Assert.Contains("is agterm running?", error.Message);
    }

    [Fact]
    public void StartIsIdempotentAndStopUnbinds()
    {
        var pipe = FreshPipe();
        var server = StartEcho(pipe);
        server.Start(); // second start must be a no-op, not a second bind
        var client = new ControlPipeClient(pipe);
        Assert.True(client.Send(new ControlRequest { Cmd = Command.Tree }).Ok);

        server.Stop();
        var afterStop = new ControlPipeClient(pipe, connectTimeoutMs: 300);
        Assert.Throws<PipeClientException>(() => afterStop.Send(new ControlRequest { Cmd = Command.Tree }));
    }

    /// <summary>Writes raw bytes over a fresh pipe connection and reads the single response line.</summary>
    private static async Task<string> SendRaw(string pipe, string raw)
    {
        using var stream = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
        await stream.ConnectAsync(5000);
        await stream.WriteAsync(Encoding.UTF8.GetBytes(raw));
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var line = await reader.ReadLineAsync();
        Assert.NotNull(line);
        return line!;
    }
}
