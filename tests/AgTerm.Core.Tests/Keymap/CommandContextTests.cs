using AgTerm.Core.Keymap;

namespace AgTerm.Core.Tests.Keymap;

public sealed class CommandContextTests
{
    private static CommandContext Sample() => new()
    {
        SessionId = "sess-1",
        SessionName = "shell",
        SessionPwd = @"C:\work tree",
        WorkspaceId = "ws-1",
        WorkspaceName = "main",
        WindowId = "win-1",
        WindowName = "work",
        FocusedPane = CommandContext.Pane.Right,
        Selection = "hello",
        Socket = "agterm-pipe",
    };

    [Fact]
    public void Expand_substitutes_known_tokens_repeatedly_and_leaves_unknown_braces()
    {
        var context = Sample();

        Assert.Equal(@"open C:\work tree", context.Expand("open {AGT_SESSION_PWD}"));
        Assert.Equal("sess-1 sess-1", context.Expand("{AGT_SESSION_ID} {AGT_SESSION_ID}"));
        Assert.Equal("echo {FOO} ${BAR}", context.Expand("echo {FOO} ${BAR}"));
        Assert.Equal("echo {AGT_SESSION_ID", context.Expand("echo {AGT_SESSION_ID"));
    }

    [Fact]
    public void Expand_preserves_command_author_quotes_around_a_Windows_path()
    {
        var context = Sample();

        Assert.Equal(
            "pwsh -File \"C:\\work tree\\deploy.ps1\"",
            context.Expand("pwsh -File \"{AGT_SESSION_PWD}\\deploy.ps1\""));
    }

    [Fact]
    public void Expand_does_not_resubstitute_token_text_inside_a_value()
    {
        var context = Sample() with { Selection = "{AGT_SOCKET}" };

        Assert.Equal("{AGT_SOCKET} agterm-pipe", context.Expand("{AGT_SELECTION} {AGT_SOCKET}"));
    }

    [Fact]
    public void Environment_and_TokenNames_have_the_same_exact_ten_keys()
    {
        var context = Sample();
        var environment = context.Environment();

        Assert.Equal(10, environment.Count);
        Assert.Equal(CommandContext.TokenNames.ToHashSet(), environment.Keys.ToHashSet());
        Assert.Equal(@"C:\work tree", environment["AGT_SESSION_PWD"]);
        Assert.Equal("right", environment["AGT_PANE"]);
        Assert.Equal("agterm-pipe", environment["AGT_SOCKET"]);
        Assert.All(environment.Keys, key => Assert.DoesNotContain("{", context.Expand($"{{{key}}}")));
    }

    [Theory]
    [InlineData("echo {AGT_SESSION_PWD}")]
    [InlineData("echo $AGT_SESSION_ID")]
    [InlineData("echo ${AGT_WORKSPACE_NAME}")]
    [InlineData("printf %s {AGT_SELECTION}")]
    public void ReferencesSessionScopedContext_detects_dangerous_session_values(string command)
    {
        Assert.True(CommandContext.ReferencesSessionScopedContext(command));
    }

    [Theory]
    [InlineData("agtermctl session new")]
    [InlineData("agtermctl --socket {AGT_SOCKET} session new")]
    [InlineData("echo {AGT_WINDOW_ID} {AGT_WINDOW_NAME} {AGT_PANE}")]
    public void ReferencesSessionScopedContext_allows_sessionless_launchers(string command)
    {
        Assert.False(CommandContext.ReferencesSessionScopedContext(command));
    }

    [Fact]
    public void Pane_defaults_to_left_and_supports_right_and_scratch()
    {
        Assert.Equal("left", new CommandContext().Expand("{AGT_PANE}"));
        Assert.Equal("right", new CommandContext { FocusedPane = CommandContext.Pane.Right }.Expand("{AGT_PANE}"));
        Assert.Equal("scratch", new CommandContext { FocusedPane = CommandContext.Pane.Scratch }.Expand("{AGT_PANE}"));
    }
}