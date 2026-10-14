using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

// Ported from agtermCore HudMarkdownTests.swift (upstream ed831ab). The sgr tests are not ported:
// the page draws runs as spans, so there is no escape encoding to pin.
public class HudMarkdownTests
{
    private const HudMarkdownStyle None = HudMarkdownStyle.None;
    private const HudMarkdownStyle Bold = HudMarkdownStyle.Bold;
    private const HudMarkdownStyle Italic = HudMarkdownStyle.Italic;
    private const HudMarkdownStyle Strike = HudMarkdownStyle.Strikethrough;
    private const HudMarkdownStyle Dim = HudMarkdownStyle.Dim;
    private const HudMarkdownStyle Underline = HudMarkdownStyle.Underline;

    private static HudMarkdownRun Run(string text, HudMarkdownStyle style = HudMarkdownStyle.None, string? link = null) =>
        new(text, style, link);

    private static string Text(IReadOnlyList<HudMarkdownRun> runs) => string.Concat(runs.Select(run => run.Text));

    private static string[] Plain(string source) =>
        HudMarkdown.Lines(source)
            .Select(line => line.Kind == HudMarkdownLineKind.Rule ? line.Lead + "<rule>" : line.Lead + Text(line.Runs))
            .ToArray();

    private static IReadOnlyList<HudMarkdownRun>[] Styled(string source) =>
        HudMarkdown.Lines(source).Select(line => line.Runs).ToArray();

    private static string[] LaidOut(string source, int width) =>
        HudMarkdown.Rows(HudMarkdown.Lines(source), width).Select(Text).ToArray();

    [Fact]
    public void ASoftBreakJoinsWithASpaceAndAHardBreakStartsARow()
    {
        Assert.Equal(new[] { "one two" }, Plain("one\ntwo"));
        Assert.Equal(new[] { "one", "two" }, Plain("one  \ntwo"));
        Assert.Equal(new[] { "one", "two" }, Plain("one\\\ntwo"));
    }

    [Fact]
    public void ParagraphsAreSeparatedByOneBlankRowWhateverTheSourceHad() =>
        Assert.Equal(new[] { "a", "", "b", "", "c" }, Plain("a\n\nb\n\n\n\nc"));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void EveryHeadingLevelIsBold(int level)
    {
        var line = Assert.Single(HudMarkdown.Lines(new string('#', level) + " Tasks"));
        Assert.Equal(new[] { Run("Tasks", Bold) }, line.Runs);
    }

    [Fact]
    public void AHeadingKeepsItsBoldAfterAnInnerStrongSpan() =>
        Assert.Equal(new[] { Run("x", Bold), Run(" y", Bold) }, Assert.Single(Styled("# **x** y")));

    [Fact]
    public void UnorderedItemsGetABulletAndHangTheirContinuation()
    {
        var lines = HudMarkdown.Lines("- build\n- test");

        Assert.Equal(new[] { "• ", "• " }, lines.Select(line => line.Lead));
        Assert.Equal(new[] { "  ", "  " }, lines.Select(line => line.Hang));
        Assert.Equal(new[] { "• build", "• test" }, Plain("- build\n- test"));
    }

    [Fact]
    public void OrderedItemsKeepTheirOrdinalAndHangByItsWidth()
    {
        var lines = HudMarkdown.Lines("9. nine\n10. ten");

        Assert.Equal(new[] { "9. ", "10. " }, lines.Select(line => line.Lead));
        Assert.Equal(new[] { "   ", "    " }, lines.Select(line => line.Hang));
    }

    [Fact]
    public void ANestedListIndentsUnderItsParentsText() =>
        Assert.Equal(new[] { "• parent", "  • child", "    • grandchild" }, Plain("- parent\n  - child\n    - grandchild"));

    [Fact]
    public void TightAndLooseListsRenderAlike()
    {
        Assert.Equal(new[] { "• a", "• b" }, Plain("- a\n- b"));
        Assert.Equal(new[] { "• a", "• b" }, Plain("- a\n\n- b"));
    }

    [Fact]
    public void AListIsSeparatedFromTheParagraphAroundIt() =>
        Assert.Equal(new[] { "status", "", "• a", "• b", "", "done" }, Plain("status\n\n- a\n- b\n\ndone"));

    [Fact]
    public void ATaskCheckboxStaysLiteralText() =>
        Assert.Equal(new[] { "• [ ] queued", "• [x] done" }, Plain("- [ ] queued\n- [x] done"));

    [Fact]
    public void ACodeBlockKeepsItsLinesVerbatimAndIndented()
    {
        var lines = HudMarkdown.Lines("```\n# not a heading\n- not a bullet\n**not bold**\n\n\tx\n```");

        Assert.Equal(new[] { "  # not a heading", "  - not a bullet", "  **not bold**", "  ", "      x" },
            lines.Select(line => line.Lead + Text(line.Runs)));
        Assert.All(lines, line => Assert.All(line.Runs, run => Assert.Equal(None, run.Style)));
    }

    [Fact]
    public void TabsExpandToTheNextStop()
    {
        Assert.Equal("a   b", HudMarkdown.ExpandTabs("a\tb"));
        Assert.Equal("abcd    e", HudMarkdown.ExpandTabs("abcd\te"));
    }

    [Fact]
    public void ABlockQuotePrefixesEveryRow() =>
        Assert.Equal(new[] { "│ first", "│ second" }, Plain("> first  \n> second"));

    [Fact]
    public void AThematicBreakIsARuleRow() =>
        Assert.Equal(
            new[] { HudMarkdownLineKind.Text, HudMarkdownLineKind.Text, HudMarkdownLineKind.Rule, HudMarkdownLineKind.Text, HudMarkdownLineKind.Text },
            HudMarkdown.Lines("above\n\n---\n\nbelow").Select(line => line.Kind));

    [Fact]
    public void ATableIsFramedWithAPaddedGridAndABoldHeader()
    {
        const string source = "| task | state |\n|---|---|\n| build | ok |\n| lint **x** | running |";
        var lines = HudMarkdown.Lines(source);

        Assert.Equal(new[]
        {
            "┌────────┬─────────┐",
            "│ task   │ state   │",
            "├────────┼─────────┤",
            "│ build  │ ok      │",
            "│ lint x │ running │",
            "└────────┴─────────┘",
        }, Plain(source));
        Assert.All(lines[1].Runs.Where(run => run.Text.Any(char.IsLetter)), run => Assert.True(run.Style.HasFlag(Bold)));
        Assert.All(lines[1].Runs.Where(run => run.Text.Contains('│')), run => Assert.Equal(None, run.Style));
        Assert.Contains(Run("x", Bold), lines[4].Runs);
        Assert.Single(lines.Select(line => HudMarkdown.Width(line.Runs)).Distinct());
    }

    [Fact]
    public void AnAllEmptyHeaderLeavesNoHeaderRule() =>
        Assert.Equal(new[] { "┌───┬───┐", "│ 1 │ 2 │", "└───┴───┘" }, Plain("|  |  |\n|---|---|\n| 1 | 2 |"));

    [Fact]
    public void ATableOpeningAListItemCarriesTheMarkerOnItsTopBorder() =>
        Assert.Equal(new[] { "• ┌───┬───┐", "  │ a │ b │", "  ├───┼───┤", "  │ 1 │ 2 │", "  └───┴───┘" },
            Plain("- | a | b |\n  |---|---|\n  | 1 | 2 |"));

    [Fact]
    public void ATableAfterTextInAListItemHangsUnderIt() =>
        Assert.Equal(new[] { "• intro", "  ┌───┐", "  │ a │", "  ├───┤", "  │ 1 │", "  └───┘" },
            Plain("- intro\n\n  | a |\n  |---|\n  | 1 |"));

    [Fact]
    public void AQuotedTableCarriesTheBarOnEveryFrameRow() =>
        Assert.Equal(new[] { "│ ┌───┐", "│ │ a │", "│ ├───┤", "│ │ 1 │", "│ └───┘" }, Plain("> | a |\n> |---|\n> | 1 |"));

    [Fact]
    public void AFrameWiderThanThePanelEndsInTheEllipsis()
    {
        var rows = HudMarkdown.Rows(HudMarkdown.Lines("| alpha | beta |\n|---|---|\n| 1 | 2 |"), 60);

        var clipped = HudMarkdown.Fitted(rows, 6, 10).Select(Text).ToArray();

        Assert.Equal("┌────…", clipped[0]);
        Assert.All(clipped, row => Assert.Equal(6, HudLayout.CellCount(row)));
    }

    // Foundation emits no run for an empty cell or an all-empty row, which shifted later cells left upstream.
    [Fact]
    public void EmptyCellsAndRowsKeepTheirPlace() =>
        Assert.Equal(new[]
        {
            "┌───┬───┬───┐", "│ a │ b │ c │", "├───┼───┼───┤", "│   │ x │ y │", "│ p │   │ r │", "│ s │ t │   │",
            "│   │   │   │", "│ u │ v │ w │", "└───┴───┴───┘",
        }, Plain("| a | b | c |\n|---|---|---|\n|  | x | y |\n| p |  | r |\n| s | t |  |\n|  |  |  |\n| u | v | w |"));

    [Fact]
    public void ATrailingAllEmptyRowIsDropped() =>
        Assert.Equal(new[] { "┌───┐", "│ a │", "├───┤", "│ 1 │", "└───┘" }, Plain("| a |\n|---|\n| 1 |\n|  |"));

    [Fact]
    public void AnImageShowsItsAltTextAndHtmlStaysLiteral()
    {
        Assert.Equal(new[] { "diagram <b>hi</b>" }, Plain("![diagram](d.png) <b>hi</b>"));
        Assert.Equal(new[] { "<!-- note -->" }, Plain("<!-- note -->"));
    }

    // upstream drops the quote bar and the item indent in front of a raw HTML block, because Foundation
    // gives the block no context; Markdig nests it, and the port keeps the prefix, see PLATFORM-NOTES
    [Fact]
    public void ARawHtmlBlockInsideAQuoteOrAListKeepsItsPrefix()
    {
        Assert.Equal(new[] { "│ <div>", "│ </div>" }, Plain("> <div>\n> </div>"));
        Assert.Equal(new[] { "• <div>", "  </div>" }, Plain("- <div>\n  </div>"));
    }

    [Fact]
    public void InlineStylesMapOntoRunStyles() =>
        Assert.Equal(new[]
        {
            Run("b", Bold), Run(" "), Run("i", Italic), Run(" "), Run("s", Strike), Run(" "), Run("c"), Run(" "),
            Run("label", Underline, "http://x"),
        }, Assert.Single(Styled("**b** *i* ~~s~~ `c` [label](http://x)")));

    [Fact]
    public void ALinkShowsItsUnderlinedLabelAlone()
    {
        Assert.Equal(new[] { "see the PR now" }, Plain("see [the PR](https://example.com/pr/7) now"));
        Assert.Equal(new[] { Run("a b", Bold | Underline, "https://example.com") }, Styled("**[a b](https://example.com)**")[0]);
        Assert.Contains("│ a │", Plain("| h |\n|---|\n| [a](http://x) |"));
        Assert.Equal(new[] { "https://example.com/a" }, Plain("<https://example.com/a>"));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("x-custom://do")]
    [InlineData("relative/path.md")]
    [InlineData("file://other.host/tmp/x")]
    public void ALinkThePolicyIgnoresIsItsPlainLabel(string url) =>
        Assert.Equal(new[] { Run("label") }, Styled($"[label]({url})")[0]);

    [Fact]
    public void ALocalFileLinkIsClickable() =>
        Assert.Equal(Run("log", Underline, "file:///tmp/build.log"), Styled("[log](file:///tmp/build.log)")[0][0]);

    [Fact]
    public void ALinkTargetIsAlwaysPrintableAscii()
    {
        Assert.Equal("https://example.com/a%1Bb", HudLinkPolicy.Target("https://example.com/a%1Bb"));
        Assert.Equal("https://example.com/%D1%84", Styled("[x](https://example.com/%D1%84)")[0][0].Link);
    }

    [Fact]
    public void ALinkInAHeadingKeepsTheHeadingStyle() =>
        Assert.Equal(new[] { Run("t", Bold | Underline, "http://x"), Run(" u", Bold) }, Styled("# [t](http://x) u")[0]);

    [Fact]
    public void AWrappedLinkKeepsItsTargetOnEveryRow()
    {
        var rows = HudMarkdown.Rows(HudMarkdown.Lines("[ab cd ef](http://x/yz)"), 6);

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { Run("ab cd", Underline, "http://x/yz") }, rows[0]);
        Assert.Equal(new[] { Run("ef", Underline, "http://x/yz") }, rows[1]);
    }

    [Fact]
    public void AClippedLinkLeavesTheEllipsisOutsideIt() =>
        Assert.Equal(new[] { Run("abc", Underline, "http://x"), Run("…") },
            HudMarkdown.Clipped(new[] { Run("abcdef", Underline, "http://x") }, 4));

    [Fact]
    public void StrongInsideEmphasisCarriesBoth() =>
        Assert.Equal(Run("b", Bold | Italic), Styled("*a **b***")[0][^1]);

    [Fact]
    public void DecodedControlCharactersAreNeutralized()
    {
        Assert.Equal(new[] { "x \uFFFD[31m y" }, Plain("x &#27;[31m y"));
        Assert.Equal(new[] { "a\uFFFDb" }, Plain("a&#10;b"));
        Assert.Equal(new[] { "a b" }, Plain("a&#9;b"));
    }

    [Fact]
    public void HardBreaksAndCodeNewlinesSurviveNeutralization() =>
        Assert.Equal(new[] { "a", "b", "", "  c", "  d" }, Plain("a  \nb\n\n```\nc\nd\n```"));

    [Fact]
    public void WrappingBreaksAtSpacesAcrossStyleRuns()
    {
        var rows = HudMarkdown.Rows(HudMarkdown.Lines("aa **bb** cc"), 5);

        Assert.Equal(new[] { "aa bb", "cc" }, rows.Select(Text));
        Assert.Equal(new[] { Run("aa "), Run("bb", Bold) }, rows[0]);
    }

    [Fact]
    public void AWordSpanningStyleRunsStaysOneWord()
    {
        var rows = HudMarkdown.Rows(HudMarkdown.Lines("pre**fix**suffix tail"), 12);

        Assert.Equal(new[] { "prefixsuffix", "tail" }, rows.Select(Text));
        Assert.Equal(Run("fix", Bold), rows[0][1]);
    }

    // Upstream wrapping once collapsed every run of spaces, rewriting code spans and raw HTML on a wide row.
    [Fact]
    public void LiteralSpacingSurvivesAWideRow()
    {
        Assert.Equal(new[] { "run echo \"a  b\" now" }, LaidOut("run `echo \"a  b\"` now", 60));
        Assert.Equal(new[] { "<div title=\"a  b\">", "    indented", "</div>" },
            LaidOut("<div title=\"a  b\">\n    indented\n</div>", 60));
    }

    [Fact]
    public void LiteralSpacingSurvivesWrappingAndIsDroppedOnlyAtTheBreak() =>
        Assert.Equal(new[] { "a  b", "c" }, LaidOut("`a  b  c`", 4));

    [Fact]
    public void AWordLongerThanTheRowIsSplit() =>
        Assert.Equal(new[] { "abcd", "efgh", "ij" }, LaidOut("abcdefghij", 4));

    [Fact]
    public void ListContinuationsHangUnderTheItemText()
    {
        Assert.Equal(new[] { "• one two", "  three" }, LaidOut("- one two three", 9));
        Assert.Equal(new[] { "10. alpha", "    beta" }, LaidOut("10. alpha beta", 12));
    }

    [Fact]
    public void CodeWrapsAtTheWidthKeepingItsSpaces() =>
        Assert.Equal(new[] { "      ", "  a b" }, LaidOut("```\n    a b\n```", 6));

    [Fact]
    public void ARuleSpansTheWidestOtherRowAndATableNeverWraps()
    {
        Assert.Equal(new[] { "───" }, LaidOut("---", 60));
        Assert.Equal(new[] { "│ ───" }, LaidOut("> ---", 60));
        Assert.Equal(new[] { "a heading of sorts", "", new string('─', 18) }, LaidOut("a heading of sorts\n\n---", 60));
        Assert.Equal("──────", LaidOut("a heading of sorts\n\n---", 6)[^1]);
        Assert.Equal(new[]
        {
            "┌─────────────┬───────┐", "│ long header │ other │", "├─────────────┼───────┤", "│ x           │ y     │",
            "└─────────────┴───────┘",
        }, LaidOut("| long header | other |\n|---|---|\n| x | y |", 6));
    }

    // Upstream pins this as one SGR bold span; the row model gives the page one bold run.
    [Fact]
    public void AHeadingWithAnInnerStrongSpanLaysOutAsOneBoldRun() =>
        Assert.Equal(new[] { Run("x y", Bold) }, HudMarkdown.Rows(HudMarkdown.Lines("# **x** y"), 20)[0]);

    [Fact]
    public void ATooWideRowEndsInAnEllipsis()
    {
        var row = new[] { Run("ab", Bold), Run("cdef") };

        Assert.Equal(new[] { Run("ab", Bold), Run("…") }, HudMarkdown.Clipped(row, 3));
        Assert.Equal(row, HudMarkdown.Clipped(row, 6));
    }

    [Fact]
    public void OverflowingRowsEndInACountOfEveryHiddenRow()
    {
        var rows = Enumerable.Range(1, 10).Select(i => (IReadOnlyList<HudMarkdownRun>)new[] { Run($"row {i}") }).ToList();

        var fitted = HudMarkdown.Fitted(rows, 20, 5);

        Assert.Equal(5, fitted.Count);
        Assert.Equal(new[] { Run("… 6 more", Dim) }, fitted[^1]);
        Assert.Equal(new[] { "row 1", "row 2", "row 3", "row 4" }, fitted.Take(4).Select(row => row[0].Text));
    }

    [Fact]
    public void TheOverflowMarkerIsClippedLikeAnyRow()
    {
        var rows = Enumerable.Range(1, 10).Select(i => (IReadOnlyList<HudMarkdownRun>)new[] { Run($"row {i}") }).ToList();

        Assert.Equal(new[] { "row…", "… 9…" }, HudMarkdown.Fitted(rows, 4, 2).Select(Text));
    }

    [Fact]
    public void TinyBudgetsNeverPaintOutsideThem()
    {
        var rows = new IReadOnlyList<HudMarkdownRun>[] { new[] { Run("abc") }, new[] { Run("def") } };

        Assert.Empty(HudMarkdown.Fitted(rows, 0, 5));
        Assert.Empty(HudMarkdown.Fitted(rows, 5, 0));
        Assert.Equal(HudMarkdown.Clipped(new[] { Run("… 2 more", Dim) }, 5), Assert.Single(HudMarkdown.Fitted(rows, 5, 1)));
        Assert.Equal(new[] { "…", "…" }, HudMarkdown.Fitted(rows, 1, 5).Select(Text));
    }

    // The cases of upstream ControlDispatcherHudTests that decide the refusal, checked on the row model.
    [Theory]
    [InlineData("---", true)]
    [InlineData("<!-- note -->\n\n[ref]: http://a", true)]
    [InlineData("&#27;", true)]
    [InlineData("- a", true)]
    [InlineData("[x]: /y", false)]
    [InlineData("&#32;", false)]
    [InlineData("&nbsp;", false)]
    [InlineData("- ", false)]
    public void VisibleTextCountsTheRenderersOwnGlyphs(string source, bool visible) =>
        Assert.Equal(visible, HudMarkdown.RendersVisibleText(source));

    [Fact]
    public void AParserFailureLeavesEachSourceLineAPlainRow()
    {
        var lines = HudMarkdown.Lines("a\n**b**\tc", _ => throw new InvalidOperationException("parser gave up"));

        Assert.Equal(new[] { "a", "**b** c" }, lines.Select(line => Text(line.Runs)));
        Assert.All(lines, line => Assert.Equal(HudMarkdownLineKind.Text, line.Kind));
    }
}
