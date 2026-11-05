using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

// Ported from upstream SessionInfoMarkupTests (80f822bf), plus the cases the Markdig port has to pin
// where Foundation's inline-only parser behaves differently from a full CommonMark document.
public class SessionInfoMarkupTests
{
    private static IReadOnlyList<SessionInfoSpan> Parse(string source, bool markdown = true) =>
        SessionInfoMarkup.Spans(source, markdown);

    private static SessionInfoParseError? Rejection(string source, bool markdown = true)
    {
        try
        {
            SessionInfoMarkup.Spans(source, markdown);
            return null;
        }
        catch (SessionInfoParseException error)
        {
            return error.Error;
        }
    }

    private static string? Rejected(string source, bool markdown, double hideAfter)
    {
        SessionInfo.Make(source, markdown, hideAfter, out var rejection);
        return rejection;
    }

    [Fact]
    public void PlainTextKeepsMarkdownLiteral()
    {
        Assert.Equal(new[] { new SessionInfoSpan("~/dev/*scratch*/notes_v2.md **x** ^[a](color: \"red\")") },
            Parse("~/dev/*scratch*/notes_v2.md **x** ^[a](color: \"red\")", markdown: false));
    }

    [Fact]
    public void InlineMarkdownMapsToTraits()
    {
        Assert.Equal(new[]
        {
            new SessionInfoSpan("3", Bold: true),
            new SessionInfoSpan(" agents "),
            new SessionInfoSpan("on", Italic: true),
            new SessionInfoSpan(" "),
            new SessionInfoSpan("rr", Code: true),
        }, Parse("**3** agents *on* `rr`"));
    }

    [Fact]
    public void ColorAndDimAttributesMapToSpans()
    {
        Assert.Equal(new[]
        {
            new SessionInfoSpan("running", Color: TintColor.Green),
            new SessionInfoSpan(" "),
            new SessionInfoSpan("idle", Dim: true),
            new SessionInfoSpan(" "),
            new SessionInfoSpan("x", Dim: true, Color: TintColor.Blue),
        }, Parse("^[running](color: \"green\") ^[idle](dim: true) ^[x](color: 'Blue', dim: true)"));
    }

    [Fact]
    public void BoldInsideAColorBlockCombinesTraits()
    {
        Assert.Equal(new[]
        {
            new SessionInfoSpan("done", Bold: true, Color: TintColor.Green),
            new SessionInfoSpan(" now", Color: TintColor.Green),
        }, Parse("^[**done** now](color: \"green\")"));
    }

    [Fact]
    public void ALinkKeepsOnlyItsLabel()
    {
        Assert.Equal(new[] { new SessionInfoSpan("see PR 861 now") }, Parse("see [PR 861](https://example.com/861) now"));
    }

    [Fact]
    public void AnUnknownAttributeKeyRendersUnstyled()
    {
        Assert.Equal(new[] { new SessionInfoSpan("a b") }, Parse("^[a](colour: \"red\") b"));
    }

    [Fact]
    public void BadColorValuesAreRejected()
    {
        Assert.Equal(SessionInfoParseError.UnknownColor("pink"), Rejection("^[a](color: \"pink\")"));
        Assert.Equal(SessionInfoParseError.InvalidMarkdown, Rejection("^[a](color: 42)"));
        Assert.Equal(SessionInfoParseError.InvalidMarkdown, Rejection("^[a](color: red)"));
    }

    [Fact]
    public void MakeBuildsAValidatedInfo()
    {
        var info = SessionInfo.Make("**3** ^[ok](color: \"green\")", markdown: true, hideAfter: 30, out var rejection);
        Assert.Null(rejection);
        Assert.NotNull(info);
        Assert.Equal("**3** ^[ok](color: \"green\")", info.Source);
        Assert.True(info.Markdown);
        Assert.Equal(30, info.HideAfter);
        Assert.Equal(new[]
        {
            new SessionInfoSpan("3", Bold: true),
            new SessionInfoSpan(" "),
            new SessionInfoSpan("ok", Color: TintColor.Green),
        }, info.Spans);
        var plain = SessionInfo.Make("**3**", markdown: false, hideAfter: 0, out _);
        Assert.Equal(new[] { new SessionInfoSpan("**3**") }, plain!.Spans);
    }

    [Fact]
    public void MakeCapsTheSourceInUtf8Bytes()
    {
        Assert.Null(Rejected(new string('a', 512), markdown: false, hideAfter: 0));
        Assert.Equal("info text is over 512 bytes", Rejected(new string('a', 513), markdown: false, hideAfter: 0));
        Assert.Null(Rejected(new string('é', 256), markdown: false, hideAfter: 0));
        Assert.Equal("info text is over 512 bytes", Rejected(new string('é', 257), markdown: false, hideAfter: 0));
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(86_401.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void MakeRejectsAnOutOfRangeHideAfter(double seconds)
    {
        Assert.Equal("--hide-after must be 0...86400 seconds", Rejected("x", markdown: false, hideAfter: seconds));
    }

    [Fact]
    public void MakeAcceptsTheLargestHideAfter()
    {
        Assert.Null(Rejected("x", markdown: false, hideAfter: 86_400));
    }

    [Fact]
    public void MakeReportsParseErrorsAsMessages()
    {
        Assert.Equal("unknown info color: pink (red|orange|yellow|green|blue|purple|gray)",
            Rejected("^[a](color: \"pink\")", markdown: true, hideAfter: 0));
        Assert.Equal("info text must be one line", Rejected("a\nb", markdown: false, hideAfter: 0));
        Assert.Equal("info text is empty", Rejected("  ", markdown: false, hideAfter: 0));
        Assert.Equal("invalid info markdown; quote attribute values: ^[text](color: \"green\")",
            Rejected("^[a](color: red)", markdown: true, hideAfter: 0));
        Assert.Equal("info text contains a control character", Rejected("a\tb", markdown: false, hideAfter: 0));
    }

    [Theory]
    [InlineData("", true, "empty")]
    [InlineData("   ", false, "empty")]
    [InlineData("&#32;&#32;", true, "empty")]
    [InlineData("^[ ](dim: true)", true, "empty")]
    [InlineData("a\nb", false, "lineBreak")]
    [InlineData("a&#10;b", true, "lineBreak")]
    [InlineData("a\u2028b", false, "lineBreak")]
    [InlineData("x &#27;[31m y", true, "controlCharacter")]
    [InlineData("a\tb", false, "controlCharacter")]
    [InlineData("a\u009B31mb", false, "controlCharacter")]
    [InlineData("a&#155;b", true, "controlCharacter")]
    public void BlankOrMultilineOrControlTextIsRejected(string source, bool markdown, string expected)
    {
        var want = expected switch
        {
            "empty" => SessionInfoParseError.Empty,
            "lineBreak" => SessionInfoParseError.LineBreak,
            _ => SessionInfoParseError.ControlCharacter,
        };
        Assert.Equal(want, Rejection(source, markdown));
    }

    // Port-specific: Foundation parses the source inline-only and keeps its whitespace, so block syntax
    // stays literal text and a raw line break anywhere is refused.

    [Theory]
    [InlineData("# heading")]
    [InlineData("- item")]
    [InlineData("1. item")]
    [InlineData("> quote")]
    [InlineData("[a]: https://example.com")]
    [InlineData("    indented")]
    public void BlockSyntaxStaysLiteral(string source)
    {
        Assert.Equal(new[] { new SessionInfoSpan(source) }, Parse(source));
    }

    [Fact]
    public void SurroundingSpacesAreKept()
    {
        Assert.Equal(new[] { new SessionInfoSpan("  a  ") }, Parse("  a  "));
    }

    [Theory]
    [InlineData("a\n")]
    [InlineData("\na")]
    [InlineData("a\r\nb")]
    [InlineData("a\n\nb")]
    [InlineData("`a\nb`")]
    public void ARawLineBreakIsRefusedInMarkdown(string source)
    {
        Assert.Equal(SessionInfoParseError.LineBreak, Rejection(source));
    }

    [Fact]
    public void ACodeSpanKeepsAttributeSyntaxLiteral()
    {
        Assert.Equal(new[] { new SessionInfoSpan("^[a](color: \"red\")", Code: true) }, Parse("`^[a](color: \"red\")`"));
    }

    [Fact]
    public void EntitiesAndStrikethroughDecodeToPlainText()
    {
        Assert.Equal(new[] { new SessionInfoSpan("a & b c") }, Parse("a &amp; b ~~c~~"));
    }

    [Fact]
    public void AnUnclosedAttributeBlockStaysLiteral()
    {
        Assert.Equal(new[] { new SessionInfoSpan("^[a](color: \"red\"") }, Parse("^[a](color: \"red\""));
        Assert.Equal(new[] { new SessionInfoSpan("^[a] b") }, Parse("^[a] b"));
    }

    [Fact]
    public void NestedAttributeBlocksInheritTheOuterStyle()
    {
        Assert.Equal(new[]
        {
            new SessionInfoSpan("a", Dim: true, Color: TintColor.Red),
            new SessionInfoSpan(" b", Color: TintColor.Red),
        }, Parse("^[^[a](dim: true) b](color: \"red\")"));
    }

    [Fact]
    public void AttributesAcceptJson5Forms()
    {
        Assert.Equal(new[] { new SessionInfoSpan("a", Dim: true, Color: TintColor.Red) },
            Parse("^[a](\"color\": \"red\", dim: true,)"));
        Assert.Equal(new[] { new SessionInfoSpan("a") }, Parse("^[a]()"));
        Assert.Equal(new[] { new SessionInfoSpan("a") }, Parse("^[a](note: {x: [1, 'two', null]})"));
    }

    [Theory]
    [InlineData("^[a](dim: \"yes\")")]
    [InlineData("^[a](dim: 1)")]
    [InlineData("^[a](color: null)")]
    [InlineData("^[a](colour: red)")]
    [InlineData("^[a](color \"red\")")]
    public void MalformedOrMistypedAttributesAreInvalid(string source)
    {
        Assert.Equal(SessionInfoParseError.InvalidMarkdown, Rejection(source));
    }

    [Fact]
    public void AnInvalidBlockWinsOverAnUnknownColorEarlierInTheLine()
    {
        // Foundation fails the whole parse before it reads any color, so the syntax error is reported
        Assert.Equal(SessionInfoParseError.InvalidMarkdown, Rejection("^[a](color: \"pink\") ^[b](color: 42)"));
    }

    [Fact]
    public void InfoEqualityIsTheSourceTheModeAndTheExpiry()
    {
        var first = SessionInfo.Make("**a**", true, 5, out _);
        Assert.Equal(first, SessionInfo.Make("**a**", true, 5, out _));
        Assert.NotEqual(first, SessionInfo.Make("**a**", false, 5, out _));
        Assert.NotEqual(first, SessionInfo.Make("**a**", true, 6, out _));
        Assert.NotEqual(first, SessionInfo.Make("**b**", true, 5, out _));
    }
}
