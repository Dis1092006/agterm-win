using AgTerm.Core.Keymap;
using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Keymap;

public class CommandErrorOptionsTests
{
    private static (CustomCommand? Command, List<KeymapDiagnostic> Diagnostics) ParseOne(string line)
    {
        var (keymap, diagnostics) = KeymapParser.Parse(line);
        return (keymap.Commands.FirstOrDefault(), diagnostics);
    }

    [Fact]
    public void A_plain_command_has_no_error_panel()
    {
        var (command, _) = ParseOne("command \"probe\" ./script");
        Assert.NotNull(command);
        Assert.False(command.ErrorHud);
        Assert.Equal(HudPosition.Center, command.ErrorPosition);
        Assert.Null(command.ErrorPane);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ctrl+alt+j ")]
    public void Error_hud_after_a_chord_or_the_name_turns_the_panel_on(string shortcut)
    {
        var (command, diagnostics) = ParseOne($"command \"probe\" {shortcut}--error-hud ./script");
        Assert.Empty(diagnostics);
        Assert.NotNull(command);
        Assert.True(command.ErrorHud);
        Assert.Equal(HudPosition.Center, command.ErrorPosition);
        Assert.Null(command.ErrorPane);
        Assert.Equal(shortcut.Trim(), command.Shortcut);
        Assert.Equal("./script", command.Command);
    }

    [Theory]
    [InlineData("--error-hud --error-position top-right --error-pane left")]
    [InlineData("--error-pane left --error-position top-right --error-hud")]
    [InlineData("--error-position top-right --error-hud --error-pane left")]
    public void Options_in_any_order_set_position_and_pane(string options)
    {
        var (command, diagnostics) = ParseOne($"command \"probe\" {options} ./script");
        Assert.Empty(diagnostics);
        Assert.NotNull(command);
        Assert.True(command.ErrorHud);
        Assert.Equal(HudPosition.TopRight, command.ErrorPosition);
        Assert.Equal(OverlayPane.Left, command.ErrorPane);
        Assert.Equal("./script", command.Command);
    }

    [Theory]
    [InlineData("top-left")]
    [InlineData("top-center")]
    [InlineData("top-right")]
    [InlineData("center-left")]
    [InlineData("center")]
    [InlineData("center-right")]
    [InlineData("bottom-left")]
    [InlineData("bottom-center")]
    [InlineData("bottom-right")]
    [InlineData("top")]
    [InlineData("bottom")]
    public void Every_hud_position_name_is_accepted(string name)
    {
        var (command, diagnostics) = ParseOne($"command \"probe\" --error-hud --error-position {name} ./script");
        Assert.Empty(diagnostics);
        Assert.NotNull(command);
        Assert.Equal(HudPositions.Parse(name), command.ErrorPosition);
    }

    [Fact]
    public void An_option_after_the_first_shell_token_is_shell_text()
    {
        var (command, diagnostics) = ParseOne("command \"probe\" --error-hud ./script --error-pane right");
        Assert.Empty(diagnostics);
        Assert.NotNull(command);
        Assert.Equal("./script --error-pane right", command.Command);
        Assert.Null(command.ErrorPane);
    }

    [Fact]
    public void Double_dash_ends_the_options()
    {
        var (command, diagnostics) = ParseOne("command \"probe\" -- --error-hud 'two  spaces'");
        Assert.Empty(diagnostics);
        Assert.NotNull(command);
        Assert.False(command.ErrorHud);
        Assert.Equal("--error-hud 'two  spaces'", command.Command);
    }

    [Fact]
    public void A_chord_after_the_options_is_shell_text()
    {
        var (command, diagnostics) = ParseOne("command \"probe\" --error-hud ctrl+a>p ./script");
        Assert.Empty(diagnostics);
        Assert.NotNull(command);
        Assert.Equal("", command.Shortcut);
        Assert.Equal("ctrl+a>p ./script", command.Command);
    }

    [Theory]
    [InlineData("--error-position top echo ok")]
    [InlineData("--error-pane left echo ok")]
    [InlineData("--error-hud --error-position")]
    [InlineData("--error-hud --error-pane")]
    [InlineData("--error-hud --error-position sideways echo ok")]
    [InlineData("--error-hud --error-pane scratch echo ok")]
    [InlineData("--error-hud --error-position --error-pane left echo ok")]
    [InlineData("--error-hud --error-hud echo ok")]
    [InlineData("--error-hud --error-position top --error-position bottom echo ok")]
    [InlineData("--error-hud --error-pane left --error-pane right echo ok")]
    [InlineData("--error-positon top echo ok")]
    [InlineData("--error-hud=on echo ok")]
    [InlineData("--error-hud")]
    [InlineData("--error-hud --")]
    [InlineData("--")]
    public void Malformed_options_skip_the_command(string body)
    {
        var (keymap, diagnostics) = KeymapParser.Parse($"command \"probe\" {body}");
        Assert.Empty(keymap.Commands);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(1, diagnostic.Line);
    }

    [Theory]
    [InlineData("--error-positon top echo ok", "command 'probe' has unknown option '--error-positon'")]
    [InlineData("--error-hud --error-hud echo ok", "command 'probe' repeats option '--error-hud'")]
    [InlineData("--error-hud --error-position sideways echo ok",
        "command 'probe' requires a valid HUD position after --error-position")]
    [InlineData("--error-hud --error-pane scratch echo ok",
        "command 'probe' requires left or right after --error-pane")]
    [InlineData("--error-pane left echo ok",
        "command 'probe' requires --error-hud for error placement options")]
    [InlineData("--error-hud", "command 'probe' has no shell line")]
    [InlineData("   ", "command 'probe' has no shell line")]
    public void Malformed_option_messages_match_upstream(string body, string message)
    {
        var (_, diagnostics) = KeymapParser.Parse($"command \"probe\" {body}");
        Assert.Equal(message, Assert.Single(diagnostics).Message);
    }
}
