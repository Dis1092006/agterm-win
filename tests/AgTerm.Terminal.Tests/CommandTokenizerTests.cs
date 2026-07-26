namespace AgTerm.Terminal.Tests;

/// <summary>
/// The §6 session.new --command contract: the ENGINE tokenizes the command string into argv with
/// quote awareness and execs argv[0] directly — shell operators are NOT interpreted. Upstream hands
/// the string to libghostty's config parser; here the tokenizer is the Windows engine's own
/// (documented in PLATFORM-NOTES): whitespace splits, single quotes group literally, double quotes
/// group with \" and \\ escapes, backslash outside quotes escapes the next character.
/// </summary>
public class CommandTokenizerTests
{
    [Theory]
    [InlineData("htop", new[] { "htop" })]
    [InlineData("ssh host -t tmux a", new[] { "ssh", "host", "-t", "tmux", "a" })]
    // runs of whitespace (spaces/tabs) collapse into one separator
    [InlineData("  a \t b  ", new[] { "a", "b" })]
    // double quotes group; \" escapes a quote, \\ a backslash inside them
    [InlineData("sh -c \"echo hi\"", new[] { "sh", "-c", "echo hi" })]
    [InlineData("app \"say \\\"hi\\\"\"", new[] { "app", "say \"hi\"" })]
    [InlineData("app \"back\\\\slash\"", new[] { "app", "back\\slash" })]
    // single quotes group literally (backslash is a plain character inside)
    [InlineData("app 'a b' 'c\\d'", new[] { "app", "a b", "c\\d" })]
    // backslash outside quotes escapes the next character
    [InlineData("app a\\ b", new[] { "app", "a b" })]
    // quotes can join adjacent runs into one token
    [InlineData("app pre\"fix mid\"post", new[] { "app", "prefix midpost" })]
    // an explicitly quoted empty token survives
    [InlineData("app \"\"", new[] { "app", "" })]
    // Windows paths pass through unmangled (backslash before a non-special char keeps it... via quoting)
    [InlineData("'C:\\Program Files\\pwsh.exe' -NoLogo", new[] { "C:\\Program Files\\pwsh.exe", "-NoLogo" })]
    public void Tokenize_splits_on_whitespace_with_quotes(string command, string[] expected)
    {
        Assert.Equal(expected, CommandTokenizer.Tokenize(command));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Tokenize_of_blank_input_is_empty_argv(string command)
    {
        Assert.Empty(CommandTokenizer.Tokenize(command));
    }

    [Fact]
    public void Tokenize_unterminated_quote_takes_rest_literally()
    {
        Assert.Equal(new[] { "app", "a b" }, CommandTokenizer.Tokenize("app \"a b"));
    }
}
