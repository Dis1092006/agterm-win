using AgTerm.Terminal;

namespace AgTerm.Terminal.Tests;

/// <summary>
/// Argv → CreateProcessW command line. The §6 session.new --command contract is argv semantics
/// (engine execs argv[0] directly, no shell interpretation); on Windows CreateProcess takes one
/// string, so the quoting must be the exact inverse of CommandLineToArgvW / MSVCRT parsing rules —
/// otherwise arguments with spaces/quotes/backslashes would not survive the trip into the child.
/// </summary>
public class WindowsCommandLineTests
{
    [Theory]
    // plain args need no quoting
    [InlineData(new[] { "cmd", "/c", "echo" }, "cmd /c echo")]
    // whitespace forces quotes
    [InlineData(new[] { "C:\\Program Files\\pwsh.exe", "-NoLogo" }, "\"C:\\Program Files\\pwsh.exe\" -NoLogo")]
    // an empty argument must survive as ""
    [InlineData(new[] { "app", "" }, "app \"\"")]
    // embedded quote is backslash-escaped
    [InlineData(new[] { "app", "say \"hi\"" }, "app \"say \\\"hi\\\"\"")]
    // backslashes before an embedded quote double, lone backslashes do not
    [InlineData(new[] { "app", "dir\\sub" }, "app dir\\sub")]
    [InlineData(new[] { "app", "back\\\"slash" }, "app \"back\\\\\\\"slash\"")]
    // trailing backslash inside a quoted arg doubles before the closing quote
    [InlineData(new[] { "app", "trail space\\" }, "app \"trail space\\\\\"")]
    public void FromArgv_quotes_like_CommandLineToArgvW_inverse(string[] argv, string expected)
    {
        Assert.Equal(expected, WindowsCommandLine.FromArgv(argv));
    }

    [Fact]
    public void FromArgv_rejects_empty_argv()
    {
        Assert.Throws<ArgumentException>(() => WindowsCommandLine.FromArgv(Array.Empty<string>()));
    }

    [Theory]
    [InlineData(new[] { "captured-tool", "--resume" }, true)]
    [InlineData(new[] { "ls", "C:\\dir with space\\x" }, true)]
    [InlineData(new[] { "echo", "naïve_1.0+2~x:y/z" }, true)]
    [InlineData(new[] { "echo", "a&calc" }, false)]
    [InlineData(new[] { "echo", "a;calc" }, false)]
    [InlineData(new[] { "echo", "$(calc)" }, false)]
    [InlineData(new[] { "echo", "a|b" }, false)]
    [InlineData(new[] { "echo", "%PATH%" }, false)]
    [InlineData(new[] { "echo", "a`calc" }, false)]
    [InlineData(new[] { "echo", "say \"hi\"" }, false)]
    [InlineData(new[] { "echo", "a>b" }, false)]
    [InlineData(new[] { "echo", "a^b" }, false)]
    [InlineData(new[] { "echo", "a!b" }, false)]
    [InlineData(new[] { "echo", "*.txt" }, false)]
    [InlineData(new[] { "calc&" }, false)]
    public void IsInertForShells_accepts_only_text_neither_pwsh_nor_cmd_acts_on(string[] argv, bool expected)
    {
        Assert.Equal(expected, WindowsCommandLine.IsInertForShells(argv));
    }
}
