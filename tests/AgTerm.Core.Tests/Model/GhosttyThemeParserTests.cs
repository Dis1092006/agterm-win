using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

// The format is the Ghostty theme file that upstream reads line by line in
// agterm/Ghostty/GhosttyApp.swift keyValues (ed831ab): key = value, blanks and # comments skipped.
public class GhosttyThemeParserTests
{
    // 16 palette lines, then the six keys: background is line 17.
    private static List<string> ValidLines() =>
    [
        .. Enumerable.Range(0, 16).Select(i => $"palette = {i}=#{i:x2}{i:x2}{i:x2}"),
        "background = #282a36",
        "foreground = #f8f8f2",
        "cursor-color = #f8f8f2",
        "cursor-text = #282a36",
        "selection-background = #44475a",
        "selection-foreground = #ffffff",
    ];

    private static string Text(IEnumerable<string> lines) => string.Join("\n", lines);

    private static List<string> Without(string key) =>
        ValidLines().Where(line => !line.StartsWith(key + " ", StringComparison.Ordinal)).ToList();

    [Fact]
    public void Parses_every_value_into_the_theme()
    {
        var result = GhosttyThemeParser.Parse("Dracula", Text(ValidLines()));

        Assert.Empty(result.Errors);
        var theme = Assert.IsType<TerminalTheme>(result.Theme);
        Assert.Equal("Dracula", theme.Name);
        Assert.Equal("#f8f8f2", theme.ForegroundColor);
        Assert.Equal("#282a36", theme.BackgroundColor);
        Assert.Equal("#f8f8f2", theme.CursorColor);
        Assert.Equal("#282a36", theme.CursorTextColor);
        Assert.Equal("#44475a", theme.SelectionBackgroundColor);
        Assert.Equal("#ffffff", theme.SelectionForegroundColor);
        Assert.Equal(16, theme.AnsiColors.Count);
        Assert.Equal("#000000", theme.AnsiColors[0]);
        Assert.Equal("#0f0f0f", theme.AnsiColors[15]);
    }

    [Fact]
    public void Skips_blank_lines_and_comments_and_trims_around_the_equals_sign()
    {
        var lines = Without("background");
        lines.InsertRange(0, ["", "   ", "# Dracula, https://draculatheme.com", "  # indented comment"]);
        lines.Add("   background   =   #101010   ");

        var result = GhosttyThemeParser.Parse("t", Text(lines));

        Assert.Empty(result.Errors);
        Assert.Equal("#101010", result.Theme!.BackgroundColor);
    }

    [Fact]
    public void Reads_crlf_text_like_lf_text()
    {
        var result = GhosttyThemeParser.Parse("t", string.Join("\r\n", ValidLines()) + "\r\n");

        Assert.Empty(result.Errors);
        Assert.Equal("#282a36", result.Theme!.BackgroundColor);
        Assert.Equal("#0f0f0f", result.Theme.AnsiColors[15]);
    }

    [Fact]
    public void A_repeated_key_takes_the_last_value()
    {
        var lines = ValidLines();
        lines.Add("background = #000001");
        lines.Add("palette = 3=#000002");

        var theme = GhosttyThemeParser.Parse("t", Text(lines)).Theme!;

        Assert.Equal("#000001", theme.BackgroundColor);
        Assert.Equal("#000002", theme.AnsiColors[3]);
    }

    [Fact]
    public void Ignores_an_unknown_key()
    {
        var lines = ValidLines();
        lines.Add("font-size = 12");
        lines.Add("bold-is-bright = true");

        Assert.Empty(GhosttyThemeParser.Parse("t", Text(lines)).Errors);
    }

    [Fact]
    public void Accepts_upper_case_hex_digits()
    {
        var lines = Without("background");
        lines.Add("background = #ABCDEF");

        Assert.Equal("#ABCDEF", GhosttyThemeParser.Parse("t", Text(lines)).Theme!.BackgroundColor);
    }

    [Theory]
    [InlineData("background")]
    [InlineData("foreground")]
    [InlineData("cursor-color")]
    [InlineData("cursor-text")]
    [InlineData("selection-background")]
    [InlineData("selection-foreground")]
    public void A_missing_key_is_an_error_without_a_line(string key)
    {
        var result = GhosttyThemeParser.Parse("t", Text(Without(key)));

        Assert.Null(result.Theme);
        Assert.Equal([new GhosttyThemeError(0, $"missing {key}")], result.Errors);
    }

    [Fact]
    public void A_missing_palette_entry_is_an_error_without_a_line()
    {
        var lines = ValidLines().Where(line => !line.StartsWith("palette = 7=", StringComparison.Ordinal));

        var result = GhosttyThemeParser.Parse("t", Text(lines));

        Assert.Null(result.Theme);
        Assert.Equal([new GhosttyThemeError(0, "missing palette 7")], result.Errors);
    }

    [Theory]
    [InlineData("#abc")]
    [InlineData("red")]
    [InlineData("#gggggg")]
    [InlineData("#12345678")]
    [InlineData("282a36")]
    [InlineData("")]
    public void A_color_that_is_not_hash_rrggbb_is_an_error_on_its_line(string value)
    {
        var lines = Without("background");
        lines.Add($"background = {value}");

        var result = GhosttyThemeParser.Parse("t", Text(lines));

        Assert.Null(result.Theme);
        Assert.Contains(new GhosttyThemeError(22, $"background must be #rrggbb: {value}"), result.Errors);
    }

    [Theory]
    [InlineData("16")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("x")]
    [InlineData("")]
    [InlineData("99999999999")]
    public void A_palette_index_outside_0_to_15_is_an_error(string index)
    {
        var lines = ValidLines();
        lines.Add($"palette = {index}=#000000");

        var result = GhosttyThemeParser.Parse("t", Text(lines));

        Assert.Null(result.Theme);
        Assert.Equal([new GhosttyThemeError(23, $"palette index must be 0 to 15: {index}")], result.Errors);
    }

    [Fact]
    public void A_palette_value_without_an_index_is_an_error()
    {
        var lines = ValidLines();
        lines.Add("palette = #000000");

        Assert.Equal(
            [new GhosttyThemeError(23, "palette must be N=#rrggbb: #000000")],
            GhosttyThemeParser.Parse("t", Text(lines)).Errors);
    }

    [Fact]
    public void A_palette_color_that_is_not_hash_rrggbb_is_an_error()
    {
        var lines = ValidLines();
        lines.Add("palette = 3=red");

        Assert.Equal(
            [new GhosttyThemeError(23, "palette 3 must be #rrggbb: red")],
            GhosttyThemeParser.Parse("t", Text(lines)).Errors);
    }

    [Fact]
    public void A_line_without_an_equals_sign_or_without_a_key_is_an_error()
    {
        var lines = ValidLines();
        lines.Insert(0, "background #282a36");
        lines.Insert(1, "= #282a36");

        Assert.Equal(
            [new GhosttyThemeError(1, "expected key = value"), new GhosttyThemeError(2, "empty key")],
            GhosttyThemeParser.Parse("t", Text(lines)).Errors);
    }
}
