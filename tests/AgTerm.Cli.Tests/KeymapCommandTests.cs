using AgTerm.Cli.Tests.TestSupport;
using AgTerm.Core.Protocol;
using Xunit;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli.Tests;

public class KeymapCommandTests
{
    private static ControlRequest Request(params string[] argv) => CliFrameworkTests.Request(argv);

    [Fact]
    public void List_sends_the_bare_command()
    {
        CanonicalJson.AssertEqual(new ControlRequest { Cmd = WireCommand.KeymapList }, Request("keymap", "list"));
    }

    [Fact]
    public void Run_carries_the_name_target_and_window()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.KeymapRun, Target = "abc",
                Args = new ControlArgs { Name = "Build", Window = "w1" },
            },
            Request("keymap", "run", "Build", "--target", "abc", "--window", "w1"));
    }

    [Fact]
    public void Run_targets_the_active_session_by_default()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.KeymapRun, Target = "active",
                Args = new ControlArgs { Name = "Build" },
            },
            Request("keymap", "run", "Build"));
    }

    [Fact]
    public void Run_success_prints_ok()
    {
        var response = new ControlResponse { Ok = true, Result = new ControlResult { Id = "9f3c" } };
        Assert.Equal("ok", ClientOutput.FormatResponse(response, json: false));
    }

    [Fact]
    public void FormatKeymap_prints_every_block()
    {
        var keymap = new ControlKeymap
        {
            Path = "/cfg/keymap.conf",
            Actions =
            [
                new ControlKeymapAction { Action = "a", Chord = "ctrl+shift+t", Overridden = true },
                new ControlKeymapAction { Action = "longer.name" },
            ],
            Commands =
            [
                new ControlKeymapCommand { Name = "Build", Shortcut = "ctrl+b" },
                new ControlKeymapCommand { Name = "Lint" },
            ],
            Diagnostics =
            [
                new ControlKeymapDiagnostic { Line = 3, Message = "bad chord" },
                new ControlKeymapDiagnostic { Line = 0, Message = "whole file" },
            ],
        };

        var expected = string.Join("\n",
            "keymap: /cfg/keymap.conf",
            "",
            "actions:",
            "  * a            ctrl+shift+t",
            "    longer.name  -",
            "",
            "commands:",
            "    Build  ctrl+b",
            "    Lint  (palette only)",
            "",
            "diagnostics:",
            "    line 3: bad chord",
            "    whole file");
        Assert.Equal(expected, ClientOutput.FormatKeymap(keymap));
    }

    [Fact]
    public void FormatKeymap_appends_the_error_options_for_an_opted_in_command_only()
    {
        var keymap = new ControlKeymap
        {
            Path = "p",
            Actions = [],
            Commands =
            [
                new ControlKeymapCommand
                {
                    Name = "loud", ErrorHud = true, ErrorPosition = "bottom-center", ErrorPane = "right",
                },
                new ControlKeymapCommand { Name = "whole", ErrorHud = true, ErrorPosition = "center" },
                new ControlKeymapCommand { Name = "quiet", ErrorPosition = "center" },
            ],
            Diagnostics = [],
        };

        var text = ClientOutput.FormatKeymap(keymap);

        Assert.Contains("\n    loud  (palette only)  --error-hud --error-position bottom-center --error-pane right\n", text);
        Assert.Contains("\n    whole  (palette only)  --error-hud --error-position center\n", text);
        Assert.EndsWith("\n    quiet  (palette only)", text);
    }

    [Fact]
    public void FormatKeymap_leaves_out_empty_blocks()
    {
        var keymap = new ControlKeymap
        {
            Path = "p",
            Actions = [new ControlKeymapAction { Action = "a", Chord = "x" }],
            Commands = [],
            Diagnostics = [],
        };
        Assert.Equal("keymap: p\n\nactions:\n    a  x", ClientOutput.FormatKeymap(keymap));
    }

    [Fact]
    public void FormatResponse_routes_a_keymap_result_before_text()
    {
        var keymap = new ControlKeymap { Path = "p", Actions = [], Commands = [], Diagnostics = [] };
        var response = new ControlResponse { Ok = true, Result = new ControlResult { Keymap = keymap, Text = "t" } };
        Assert.Equal("keymap: p\n\nactions:", ClientOutput.FormatResponse(response, json: false));
    }
}
