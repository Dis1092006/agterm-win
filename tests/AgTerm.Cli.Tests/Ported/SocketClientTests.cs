using AgTerm.Cli;
using AgTerm.ControlServer;
using AgTerm.Core.Protocol;
using Xunit;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli.Tests.Ported;

// Port of agtermctlKitTests/SocketClientTests.swift (1:1, names preserved).
//
// Transport mapping (see PLATFORM-NOTES): the unix-socket SocketClient became the named-pipe
// ControlPipeClient, SocketClientError became PipeClientException, and the in-process StubServer /
// ScriptedStubServer fixtures became ControlPipeServer with a canned/scripted dispatch delegate
// (ControlPipeServer accepts in a loop, so it covers both the one-shot and the multi-connection
// fixture). Swift's `--socket server.path` has no filesystem path on the pipe transport, so the
// AGTERM_CONTROL_SOCKET pipe-name override (injected via CliEnvironment.GetEnv) plays the --socket
// role. Formatting helpers map SocketClient.formatResponse → ClientOutput.FormatResponse.
//
// The Swift suite is @Suite(.serialized) because captureStdout redirects the process-global
// STDOUT_FILENO, so runSucceedsOnOkResponse/runThrowsExitCodeFailureOnErrorResponse race on stdout
// in parallel. The port injects a per-test writer (CliEnvironment.Out) instead — the captureStdout
// helper dissolves into the environment seam and no serialization is needed.
public class SocketClientTests
{
    private static string TestPipeName() => "agterm-test-" + Guid.NewGuid().ToString("N");

    private static ControlPipeServer StartServer(string pipeName,
        Func<ControlRequest, ControlResponse> responder)
    {
        var server = new ControlPipeServer(pipeName,
            request => Task.FromResult<ControlResponse?>(responder(request)));
        server.Start();
        return server;
    }

    /// <summary>The environment a ported run() test executes in: output captured (the port of
    /// captureStdout), the fixture pipe name injected (the port of --socket), sleeps elided so the
    /// --block poll does not pad the test wall clock.</summary>
    private static CliEnvironment ServerEnv(ControlPipeServer server, StringWriter output,
        StringWriter? error = null) => new()
    {
        GetEnv = key => key == "AGTERM_CONTROL_SOCKET" ? server.PipeName : null,
        Out = output,
        Error = error ?? new StringWriter(),
        Sleep = _ => { },
    };

    /// <summary>Pins the upstream "\n" line terminator regardless of the platform newline.</summary>
    private static StringWriter NewOutput() => new() { NewLine = "\n" };

    [Fact]
    public void ConsecutiveEventReadsUseIndependentOneShotConnections()
    {
        var run = Guid.Parse("CBB5E3D0-7A9B-4C96-9EA2-18B14380DDB1");
        var script = new EventReadScript(run);
        using var server = StartServer(TestPipeName(), script.Respond);
        var client = new ControlPipeClient(server.PipeName);

        var first = client.Send(new ControlRequest { Cmd = WireCommand.EventsRead });
        var anchor = first.Result?.Events;
        Assert.NotNull(anchor); // try #require
        var second = client.Send(new ControlRequest
        {
            Cmd = WireCommand.EventsRead,
            Args = new ControlArgs
            {
                After = anchor!.Next.ToString(),
                // Swift anchor.run.uuidString is the uppercase canonical form.
                Run = anchor.Run.ToString("D").ToUpperInvariant(),
            },
        });

        Assert.Equal((ulong?)8, second.Result?.Events?.Next);
        Assert.Equal(new string?[] { null, "7" },
            script.Requests().Select(r => r.Args?.After).ToArray());
    }

    [Fact]
    public void RoundTripOkResponse()
    {
        var canned = new ControlResponse { Ok = true, Result = new ControlResult { Id = "9f3c" } };
        ControlRequest? received = null;
        using var server = StartServer(TestPipeName(), request =>
        {
            received = request;
            return canned;
        });

        var client = new ControlPipeClient(server.PipeName);
        var response = client.Send(new ControlRequest
        {
            Cmd = WireCommand.SessionSelect, Target = "active",
        });

        Assert.True(response.Ok);
        Assert.Equal("9f3c", response.Result?.Id);
        // the server echoed the request it received — confirm the client wrote it correctly.
        Assert.Equal(WireCommand.SessionSelect, received?.Cmd);
        Assert.Equal("active", received?.Target);
    }

    [Fact]
    public void RoundTripErrorResponse()
    {
        var canned = new ControlResponse { Ok = false, Error = "cannot delete last workspace" };
        using var server = StartServer(TestPipeName(), _ => canned);

        var client = new ControlPipeClient(server.PipeName);
        var response = client.Send(new ControlRequest
        {
            Cmd = WireCommand.WorkspaceDelete, Target = "active",
        });

        Assert.False(response.Ok);
        Assert.Equal("cannot delete last workspace", response.Error);
    }

    /// <summary>A `session.text --all` response can exceed the old 1 MiB cap (full scrollback). A
    /// >1 MiB text payload must round-trip intact, not fail with "no response".</summary>
    [Fact]
    public void RoundTripLargeResponse()
    {
        var big = string.Concat(Enumerable.Repeat("scrollback line\n", 200_000)); // ~3 MiB, well over 1 MiB
        using var server = StartServer(TestPipeName(),
            _ => new ControlResponse { Ok = true, Result = new ControlResult { Text = big } });

        var client = new ControlPipeClient(server.PipeName);
        var response = client.Send(new ControlRequest
        {
            Cmd = WireCommand.SessionText, Target = "active",
        });

        Assert.True(response.Ok);
        Assert.Equal(big, response.Result?.Text);
    }

    [Fact]
    public void ConnectFailureToMissingSocketThrows()
    {
        // a short connect timeout: connecting to a nonexistent pipe waits the whole timeout on
        // Windows, and the default 5 s would only pad the test.
        var client = new ControlPipeClient("agterm-missing-" + Guid.NewGuid().ToString("N")[..8],
            connectTimeoutMs: 250);
        var error = Assert.Throws<PipeClientException>(
            () => client.Send(new ControlRequest { Cmd = WireCommand.Tree }));
        Assert.Contains("— is agterm running?", error.Message);
    }

    [Fact(Skip = "PORT-GAP: unix-socket-specific; named-pipe transport covers this differently")]
    public void SocketPathLimitMatchesPlatformCapacity()
    {
        // The unix transport pre-validates the socket path against sockaddr_un.sun_path capacity and
        // throws "socket path too long ..." for an oversized one. The Windows pipe namespace is flat
        // (names, not filesystem paths) with no equivalent capacity arm. Original Swift body:
        //
        //     let addr = sockaddr_un()
        //     let capacity = MemoryLayout.size(ofValue: addr.sun_path)
        //     let fittingPath = "/" + String(repeating: "x", count: capacity - 2)
        //     let oversizedPath = "/" + String(repeating: "x", count: capacity - 1)
        //
        //     do {
        //         _ = try SocketClient(path: fittingPath).send(ControlRequest(cmd: .tree))
        //         Issue.record("expected connect to a missing socket to fail")
        //     } catch let error as SocketClientError {
        //         #expect(!error.description.hasPrefix("socket path too long"))
        //     } catch {
        //         Issue.record("unexpected error: \(error)")
        //     }
        //
        //     do {
        //         _ = try SocketClient(path: oversizedPath).send(ControlRequest(cmd: .tree))
        //         Issue.record("expected an oversized socket path to fail")
        //     } catch let error as SocketClientError {
        //         #expect(error.description.hasPrefix("socket path too long"))
        //     } catch {
        //         Issue.record("unexpected error: \(error)")
        //     }
    }

    /// <summary>A create command's `run()` returns without throwing on `{"ok":true}` and prints the
    /// new id.</summary>
    [Fact]
    public void RunEchoesNewIdForCreateCommand()
    {
        using var server = StartServer(TestPipeName(),
            _ => new ControlResponse { Ok = true, Result = new ControlResult { Id = "9f3c" } });

        var command = Agtermctl.ParseAsRoot(new[] { "session", "new" });
        var output = NewOutput();
        var exit = command.Execute(ServerEnv(server, output));
        Assert.Equal(0, exit); // run() returned without throwing
        Assert.Equal("9f3c\n", output.ToString());
    }

    /// <summary>A non-create command suppresses the id echo (prints `ok`) even when the response
    /// carries an id.</summary>
    [Fact]
    public void RunQuietsIdForNonCreateCommand()
    {
        using var server = StartServer(TestPipeName(),
            _ => new ControlResponse { Ok = true, Result = new ControlResult { Id = "9f3c" } });

        var command = Agtermctl.ParseAsRoot(new[] { "tree" });
        var output = NewOutput();
        var exit = command.Execute(ServerEnv(server, output));
        Assert.Equal(0, exit);
        Assert.Equal("ok\n", output.ToString());
    }

    // Swift's private captureStdout helper (dup2 of STDOUT_FILENO) has no port: run() writes to the
    // injected CliEnvironment.Out, so each test captures with a plain StringWriter.

    /// <summary>`RequestCommand.run()` throws `ExitCode.failure` on a `{"ok":false}` response —
    /// here: Execute returns exit code 1.</summary>
    [Fact]
    public void RunThrowsExitCodeFailureOnErrorResponse()
    {
        using var server = StartServer(TestPipeName(),
            _ => new ControlResponse { Ok = false, Error = "boom" });

        var command = Agtermctl.ParseAsRoot(new[] { "tree", "--json" });
        var exit = command.Execute(ServerEnv(server, NewOutput()));
        Assert.Equal(1, exit);
    }

    // the --block path: open → poll session.overlay.result (retry while still running) → exit with the
    // captured status. drives the real run() against a scripted multi-connection server.
    [Fact]
    public void BlockPollsResultThenExitsWithStatus()
    {
        var script = new OverlayResultScript(stillRunningTimes: 1,
            finalResult: new ControlResponse
            {
                Ok = true, Result = new ControlResult { Id = "abc", ExitCode = 7 },
            });
        using var server = StartServer(TestPipeName(), script.Respond);

        var command = Agtermctl.ParseAsRoot(new[] { "session", "overlay", "open", "echo", "--block" });
        // Swift expects a thrown ExitCode(7); here --block's status is Execute's return value.
        var exit = command.Execute(ServerEnv(server, NewOutput()));
        Assert.Equal(7, exit);
    }

    // an ok result with no exitCode is a protocol violation → --block fails, never exits 0.
    [Fact]
    public void BlockFailsWhenResultMissingExitCode()
    {
        var script = new OverlayResultScript(stillRunningTimes: 0,
            finalResult: new ControlResponse
            {
                Ok = true, Result = new ControlResult { Id = "abc" },
            });
        using var server = StartServer(TestPipeName(), script.Respond);

        var command = Agtermctl.ParseAsRoot(new[] { "session", "overlay", "open", "echo", "--block" });
        Assert.Equal(1, command.Execute(ServerEnv(server, NewOutput())));
    }

    // a failed open short-circuits --block before any poll.
    [Fact]
    public void BlockFailsWhenOpenFails()
    {
        using var server = StartServer(TestPipeName(), request =>
            request.Cmd == WireCommand.SessionOverlayOpen
                ? new ControlResponse { Ok = false, Error = "overlay already open" }
                : new ControlResponse { Ok = false, Error = "unexpected" });

        var command = Agtermctl.ParseAsRoot(new[] { "session", "overlay", "open", "echo", "--block" });
        Assert.Equal(1, command.Execute(ServerEnv(server, NewOutput())));
    }

    [Fact]
    public void FormatResponseBareOk()
    {
        Assert.Equal("ok", ClientOutput.FormatResponse(new ControlResponse { Ok = true }, json: false));
    }

    [Fact]
    public void FormatResponseEchoesIdWhenRequested()
    {
        var response = new ControlResponse { Ok = true, Result = new ControlResult { Id = "9f3c" } };
        Assert.Equal("9f3c", ClientOutput.FormatResponse(response, json: false, echoId: true));
    }

    [Fact]
    public void FormatResponseSuppressesIdByDefault()
    {
        var response = new ControlResponse { Ok = true, Result = new ControlResult { Id = "9f3c" } };
        Assert.Equal("ok", ClientOutput.FormatResponse(response, json: false));
    }

    [Fact]
    public void FormatResponseText()
    {
        var response = new ControlResponse
        {
            Ok = true, Result = new ControlResult { Text = "selected\nlines" },
        };
        Assert.Equal("selected\nlines", ClientOutput.FormatResponse(response, json: false));
    }

    [Fact]
    public void FormatResponseZeroCountIsOk()
    {
        // keymap.reload reports a parse-diagnostic count; 0 reads as a clean reload.
        var response = new ControlResponse { Ok = true, Result = new ControlResult { Count = 0 } };
        Assert.Equal("ok", ClientOutput.FormatResponse(response, json: false));
    }

    [Fact]
    public void FormatResponseNonZeroCountPluralizes()
    {
        var response = new ControlResponse { Ok = true, Result = new ControlResult { Count = 3 } };
        Assert.Equal("3 diagnostic(s)", ClientOutput.FormatResponse(response, json: false));
    }

    [Theory] // @Test(arguments:) — the one parameterized test in the Swift suite
    [InlineData(1, "1 session")]
    [InlineData(2, "2 sessions")]
    [InlineData(0, "0 sessions")]
    public void FormatResponseAffectedSessions(int affected, string expected)
    {
        var response = new ControlResponse
        {
            Ok = true, Result = new ControlResult { Affected = affected },
        };
        Assert.Equal(expected, ClientOutput.FormatResponse(response, json: false));
    }

    [Fact]
    public void FormatResponseError()
    {
        Assert.Equal("error: boom",
            ClientOutput.FormatResponse(new ControlResponse { Ok = false, Error = "boom" }, json: false));
    }

    [Fact]
    public void FormatResponseRatio()
    {
        // session.resize echoes the applied (clamped) left-pane fraction as a bare 3-decimal number.
        var response = new ControlResponse
        {
            Ok = true, Result = new ControlResult { Id = "9f3c", Ratio = 0.85 },
        };
        Assert.Equal("0.850", ClientOutput.FormatResponse(response, json: false));
    }

    [Fact]
    public void FormatResponseErrorFallback()
    {
        // an error response with no message falls back to a generic line.
        Assert.Equal("error: unknown error",
            ClientOutput.FormatResponse(new ControlResponse { Ok = false }, json: false));
    }

    [Fact]
    public void FormatResponseJSONIsRaw()
    {
        var response = new ControlResponse { Ok = true, Result = new ControlResult { Id = "9f3c" } };
        // the raw line's key order differs from the Swift encoder's, so (like the Swift test) the
        // comparison decodes the line back instead of matching bytes.
        var line = ClientOutput.FormatResponse(response, json: true);
        var decoded = ControlJson.Deserialize<ControlResponse>(line);
        Assert.True(decoded?.Ok);
        Assert.Equal("9f3c", decoded?.Result?.Id);
    }

    [Fact]
    public void FormatResponseTree()
    {
        var session = new ControlSessionNode
        {
            Id = "s1", Name = "shell", Cwd = "/tmp", Active = true, Split = true,
        };
        var workspace = new ControlWorkspaceNode
        {
            Id = "w1", Name = "work", Active = true, Sessions = new[] { session },
        };
        var tree = new ControlTree { Workspaces = new[] { workspace } };
        var @out = ClientOutput.FormatResponse(new ControlResponse
        {
            Ok = true, Result = new ControlResult { Tree = tree },
        }, json: false);
        var lines = @out.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Equal("* work  [w1]", lines[0]);
        // active session (*), the (split) suffix, id and cwd columns.
        Assert.Equal("  * shell (split)  [s1]  /tmp", lines[1]);
    }

    [Fact]
    public void FormatTreeShowsScratchTag()
    {
        var session = new ControlSessionNode
        {
            Id = "s3", Name = "shell", Cwd = "/tmp", Active = true, Split = false,
            Overlay = false, Scratch = true,
        };
        var workspace = new ControlWorkspaceNode
        {
            Id = "w3", Name = "work", Active = true, Sessions = new[] { session },
        };
        var tree = new ControlTree { Workspaces = new[] { workspace } };
        var @out = ClientOutput.FormatResponse(new ControlResponse
        {
            Ok = true, Result = new ControlResult { Tree = tree },
        }, json: false);
        var lines = @out.Split('\n');
        Assert.Equal("  * shell (scratch)  [s3]  /tmp", lines[1]);
    }

    [Fact]
    public void FormatTreeMarksInactive()
    {
        var session = new ControlSessionNode
        {
            Id = "s2", Name = "logs", Cwd = "/var", Active = false, Split = false,
        };
        var workspace = new ControlWorkspaceNode
        {
            Id = "w2", Name = "other", Active = false, Sessions = new[] { session },
        };
        var tree = new ControlTree { Workspaces = new[] { workspace } };
        var @out = ClientOutput.FormatResponse(new ControlResponse
        {
            Ok = true, Result = new ControlResult { Tree = tree },
        }, json: false);
        var lines = @out.Split('\n');
        Assert.Equal("  other  [w2]", lines[0]);
        Assert.Equal("    logs  [s2]  /var", lines[1]);
    }

    [Fact]
    public void FormatResponseWindows()
    {
        var windows = new[]
        {
            new ControlWindowNode { Id = "w1", Name = "work", Open = true, Active = true },
            new ControlWindowNode { Id = "w2", Name = "personal", Open = true, Active = false },
            new ControlWindowNode { Id = "w3", Name = "archive", Open = false, Active = false },
            // a closed-but-active window (frontmost id pointing at a window not yet loaded) still
            // renders the [active] tag without [open].
            new ControlWindowNode { Id = "w4", Name = "pending", Open = false, Active = true },
        };
        var @out = ClientOutput.FormatResponse(new ControlResponse
        {
            Ok = true, Result = new ControlResult { Windows = windows },
        }, json: false);
        var lines = @out.Split('\n');
        Assert.Equal(4, lines.Length);
        Assert.Equal("w1  work [open] [active]", lines[0]);
        Assert.Equal("w2  personal [open]", lines[1]);
        Assert.Equal("w3  archive", lines[2]);
        Assert.Equal("w4  pending [active]", lines[3]);
    }

    [Fact]
    public void FormatResponseEmptyWindows()
    {
        var @out = ClientOutput.FormatResponse(new ControlResponse
        {
            Ok = true, Result = new ControlResult { Windows = Array.Empty<ControlWindowNode>() },
        }, json: false);
        // an empty window list renders an empty string (no per-window lines), not the bare ok line —
        // a present-but-empty `windows` payload still takes the windows branch.
        Assert.Equal("", @out);
    }

    [Fact]
    public void FormatResponseThemesMarksCurrent()
    {
        var response = new ControlResponse
        {
            Ok = true, Result = new ControlResult
            {
                Theme = "Nord", Themes = new[] { "Dracula", "Nord" },
            },
        };
        var @out = ClientOutput.FormatResponse(response, json: false);
        Assert.Equal(new[] { "  default ghostty", "  Dracula", "* Nord" }, @out.Split('\n'));
    }

    [Fact]
    public void FormatResponseThemesMarksGhosttyDefaultWhenCurrent()
    {
        // nil current theme = ghostty's built-in is active, so the leading "default ghostty" row is marked.
        var response = new ControlResponse
        {
            Ok = true, Result = new ControlResult { Theme = null, Themes = new[] { "Dracula" } },
        };
        var @out = ClientOutput.FormatResponse(response, json: false);
        Assert.Equal(new[] { "* default ghostty", "  Dracula" }, @out.Split('\n'));
    }

    [Fact]
    public void FormatResponseThemeSetIsBareOk()
    {
        // theme.set returns only `theme` (no `themes` array), so it prints `ok` like other mutations.
        var response = new ControlResponse
        {
            Ok = true, Result = new ControlResult { Theme = "Dracula" },
        };
        Assert.Equal("ok", ClientOutput.FormatResponse(response, json: false));
    }

    [Fact]
    public void FormatResponseThemesMarksBothSyncedSides()
    {
        // when syncing, both the light and dark themes are marked and a header notes the pair.
        var response = new ControlResponse
        {
            Ok = true, Result = new ControlResult
            {
                Theme = null, Themes = new[] { "agterm", "Builtin Light", "Nord" },
                Sync = true, Light = "Builtin Light", Dark = "agterm",
            },
        };
        var @out = ClientOutput.FormatResponse(response, json: false);
        var lines = @out.Split('\n');
        Assert.Equal("syncing with macOS appearance — light: Builtin Light, dark: agterm", lines.First());
        Assert.Contains("* agterm", lines);
        Assert.Contains("* Builtin Light", lines);
        Assert.Contains("  Nord", lines);
        Assert.Contains("  default ghostty", lines); // unmarked while syncing
    }

    /// <summary>Records every request and answers events.read with an empty page whose cursor
    /// advances (7, then 8) — the port of the Swift EventReadScript.</summary>
    private sealed class EventReadScript
    {
        private readonly object _lock = new();
        private readonly List<ControlRequest> _seen = new();
        private readonly Guid _run;

        public EventReadScript(Guid run) => _run = run;

        public ControlResponse Respond(ControlRequest request)
        {
            int count;
            lock (_lock)
            {
                _seen.Add(request);
                count = _seen.Count;
            }
            ulong next = count == 1 ? 7UL : 8UL;
            return new ControlResponse
            {
                Ok = true,
                Result = new ControlResult
                {
                    Events = new ControlEventBatch
                    {
                        Run = _run, Next = next, Items = Array.Empty<ControlEvent>(),
                    },
                },
            };
        }

        public IReadOnlyList<ControlRequest> Requests()
        {
            lock (_lock)
            {
                return _seen.ToList();
            }
        }
    }

    /// <summary>Scripts the `--block` round trips: `session.overlay.open` returns a fixed id;
    /// `session.overlay.result` errors `stillRunning` for the first `stillRunningTimes` calls, then
    /// returns `finalResult`.</summary>
    private sealed class OverlayResultScript
    {
        private readonly object _lock = new();
        private int _resultCalls;
        private readonly int _stillRunningTimes;
        private readonly ControlResponse _finalResult;

        public OverlayResultScript(int stillRunningTimes, ControlResponse finalResult)
        {
            _stillRunningTimes = stillRunningTimes;
            _finalResult = finalResult;
        }

        public ControlResponse Respond(ControlRequest request)
        {
            switch (request.Cmd)
            {
                case WireCommand.SessionOverlayOpen:
                    return new ControlResponse { Ok = true, Result = new ControlResult { Id = "abc" } };
                case WireCommand.SessionOverlayResult:
                    int n;
                    lock (_lock)
                    {
                        n = ++_resultCalls;
                    }
                    return n <= _stillRunningTimes
                        ? new ControlResponse { Ok = false, Error = OverlayResultError.StillRunning }
                        : _finalResult;
                default:
                    return new ControlResponse { Ok = false, Error = $"unexpected cmd {request.Cmd}" };
            }
        }
    }
}
