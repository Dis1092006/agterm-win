using System.Globalization;
using System.Text;
using AgTerm.Core.Keymap;
using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Keymap;

public class CommandFailureTests
{
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public void DetailTakesTheLastNonblankLine() =>
        Assert.Equal("second failure", CommandFailure.Detail(Bytes("first failure\nsecond failure\n\n   \n")));

    [Fact]
    public void DetailReadsALastLineWithNoNewline() =>
        Assert.Equal("only line", CommandFailure.Detail(Bytes("only line")));

    [Fact]
    public void DetailIsNullWithoutUsableOutput()
    {
        Assert.Null(CommandFailure.Detail([]));
        Assert.Null(CommandFailure.Detail(Bytes("\n  \n")));
    }

    [Fact]
    public void DetailStripsEscapeSequencesThatWouldPaintOutsideThePanel() =>
        Assert.Equal("red error", CommandFailure.Detail(Bytes("\u001b[31mred error\u001b[0m")));

    [Fact]
    public void DetailSkipsALastLineThatIsNothingButAColourReset() =>
        Assert.Equal("real error", CommandFailure.Detail(Bytes("real error\n\u001b[0m\n")));

    [Fact]
    public void DetailSkipsALastLineThatIsNothingButControlCharacters() =>
        Assert.Equal("real error", CommandFailure.Detail(Bytes("real error\n\u0007\n")));

    [Fact]
    public void DetailDropsAnOscSequenceWholeRatherThanShowingItsPayload() =>
        Assert.Equal("plain error", CommandFailure.Detail(Bytes("\u001b]0;a title\u0007plain error")));

    [Fact]
    public void DetailDecodesATailCutMidScalar()
    {
        var detail = CommandFailure.Detail(Bytes("ошибка").AsSpan(1));
        Assert.NotNull(detail);
        Assert.Contains("шибка", detail);
    }

    [Fact]
    public void DetailReadsAnOemEncodedLineUnderARussianConsole()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var oem = Encoding.GetEncoding(866).GetBytes("ошибка сборки");

        var detail = UnderCulture("ru-RU", () => CommandFailure.Detail(oem));

        Assert.Equal("ошибка сборки", detail);
    }

    [Fact]
    public void DetailStillReadsUtf8UnderARussianConsole()
    {
        var detail = UnderCulture("ru-RU", () => CommandFailure.Detail(Bytes("ошибка сборки")));

        Assert.Equal("ошибка сборки", detail);
    }

    [Fact]
    public void DetailReadsAnOemEncodedLineCutMidLineUnderARussianConsole()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var oem = Encoding.GetEncoding(866).GetBytes("первая\nошибка сборки");

        var detail = UnderCulture("ru-RU", () => CommandFailure.Detail(oem.AsSpan(3)));

        Assert.Equal("ошибка сборки", detail);
    }

    private static T UnderCulture<T>(string name, Func<T> run)
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
        try
        {
            return run();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void DetailOfALongSingleLineTailIsCapped()
    {
        var line = new string('a', CommandFailure.TailLimit - 3) + "END";

        var detail = CommandFailure.Detail(Bytes(line));

        Assert.NotNull(detail);
        Assert.True(HudLayout.TextLength(detail) <= HudSpec.MaxTextLength);
        Assert.StartsWith("aaa", detail);
    }

    // the exact cap re-measures the whole string per removed element, so a full tail must reach it
    // already cut; a wall-clock bound here failed under a parallel test run
    [Fact]
    public void ALongLineIsCutToTheBoundBeforeTheExactCap()
    {
        var line = new string('a', CommandFailure.TailLimit);

        Assert.Equal(CommandFailure.CutBeforeCapping, CommandFailure.Bounded(line).Length);
    }

    [Fact]
    public void TheCutBeforeTheCapKeepsAGraphemeWhole()
    {
        var family = "👨‍👩‍👧";
        var line = new string('a', CommandFailure.CutBeforeCapping - 2) + family + new string('b', 100);

        Assert.Equal(new string('a', CommandFailure.CutBeforeCapping - 2), CommandFailure.Bounded(line));
    }

    [Fact]
    public void DetailIsCappedToWhatAHudAccepts()
    {
        var detail = CommandFailure.Detail(Bytes(new string('e', 400)));
        Assert.NotNull(detail);
        Assert.True(HudLayout.TextLength(detail) <= HudSpec.MaxTextLength);
    }

    [Fact]
    public void DetailCapsAMultiScalarGraphemeByScalarsNotCharacters()
    {
        // a ZWJ family is several scalars after NFC, so 100 of them far exceed the 256 cap.
        var family = string.Concat(Enumerable.Repeat("👨‍👩‍👧", 100));
        var detail = CommandFailure.Detail(Bytes(family));
        Assert.NotNull(detail);
        Assert.NotEmpty(detail);
        Assert.True(HudLayout.TextLength(detail) <= HudSpec.MaxTextLength);
        Assert.True(HudLayout.CellCount(detail) < HudLayout.CellCount(family));
    }

    [Fact]
    public void MessageNamesTheCommandAndTheReason() =>
        Assert.Equal("Agent Reset: exit 69", CommandFailure.Message("Agent Reset", "exit 69"));

    [Fact]
    public void MessageStripsControlCharactersFromTheName() =>
        Assert.Equal("AgentReset: exit 1", CommandFailure.Message("Agent\u0007Reset", "exit 1"));

    [Fact]
    public void MessageTruncatesTheNameAndKeepsTheReason()
    {
        var message = CommandFailure.Message(new string('n', 400), "exit 1");
        Assert.True(HudLayout.TextLength(message) <= HudSpec.MaxTextLength);
        Assert.EndsWith(": exit 1", message);
        Assert.StartsWith("nnn", message);
    }

    [Fact]
    public void MessageKeepsTheReasonAloneWhenItFillsTheCapByItself()
    {
        var message = CommandFailure.Message("probe", new string('r', 400));
        Assert.True(HudLayout.TextLength(message) <= HudSpec.MaxTextLength);
        Assert.StartsWith("rrr", message);
    }
}
