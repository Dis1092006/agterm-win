using AgTerm.Cli;
using AgTerm.Core.Protocol;
using Xunit;

namespace AgTerm.Cli.Tests;

public sealed class HooksCommandTests
{
    [Fact]
    public void FormatHooks_with_no_hooks_says_so()
    {
        var hooks = new ControlHooks { Path = "p", Diagnostics = [], Hooks = [] };
        Assert.Equal("hooks: p\n  (no hooks)", ClientOutput.FormatHooks(hooks));
    }

    [Fact]
    public void FormatHooks_prints_one_row_per_hook_then_the_diagnostics()
    {
        var hooks = new ControlHooks
        {
            Path = "C:\\cfg\\hooks.conf",
            Diagnostics = [new ControlKeymapDiagnostic { Line = 4, Message = "unknown event kind: x" }],
            Hooks =
            [
                new ControlHookEntry { Kind = "status", Command = "notify.cmd", Line = 1 },
                new ControlHookEntry
                {
                    Kind = "message", Command = "run.cmd", Line = 2, RunningPid = 42, ElapsedSeconds = 12.9,
                    Pending = 3, Dropped = 5, LastFailure = "exit 1",
                },
                new ControlHookEntry
                {
                    Kind = "app.launched", Command = "old.cmd", Line = 3, RunningPid = 7, ElapsedSeconds = 0.4,
                    Retired = true,
                },
            ],
        };

        var expected = string.Join("\n",
            "hooks: C:\\cfg\\hooks.conf",
            "  line 1: on status notify.cmd",
            "  line 2: on message run.cmd  running pid 42 for 12s  pending 3  dropped 5  last failure: exit 1",
            "  line 3: on app.launched old.cmd  running pid 7 for 0s  (retired, removed from the file)",
            "",
            "diagnostics:",
            "    line 4: unknown event kind: x");
        Assert.Equal(expected, ClientOutput.FormatHooks(hooks));
    }

    [Fact]
    public void FormatResponse_routes_a_hooks_result_to_the_hooks_listing()
    {
        var response = new ControlResponse
        {
            Ok = true,
            Result = new ControlResult { Hooks = new ControlHooks { Path = "p", Diagnostics = [], Hooks = [] } },
        };
        Assert.Equal("hooks: p\n  (no hooks)", ClientOutput.FormatResponse(response));
    }

    [Fact]
    public void A_reload_prints_ok_or_the_diagnostic_count()
    {
        string Reload(int count) => ClientOutput.FormatResponse(
            new ControlResponse { Ok = true, Result = new ControlResult { Count = count } });
        Assert.Equal("ok", Reload(0));
        Assert.Equal("2 diagnostic(s)", Reload(2));
    }
}
