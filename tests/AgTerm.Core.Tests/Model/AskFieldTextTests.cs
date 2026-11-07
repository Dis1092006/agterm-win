using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Model;

public class AskFieldTextTests
{
    private const ControlAskInputMode Line = ControlAskInputMode.Line;
    private const ControlAskInputMode Multiline = ControlAskInputMode.Multiline;

    [Fact]
    public void AMultilineBoxBreakIsANewline()
    {
        Assert.Equal("a\nb\nc", AskFieldText.FromTextBox("a\rb\r\nc", Multiline));
        Assert.Equal("a\rb\rc", AskFieldText.ToTextBox("a\nb\nc", Multiline));
    }

    [Fact]
    public void ALineBoxIsTakenAsItIs()
    {
        Assert.Equal("a\rb", AskFieldText.FromTextBox("a\rb", Line));
        Assert.Equal("a\nb", AskFieldText.ToTextBox("a\nb", Line));
    }

    [Theory]
    [InlineData("plain", Line, true)]
    [InlineData("tab\there", Line, false)]
    [InlineData("tab\there", Multiline, true)]
    [InlineData("two\nlines", Multiline, true)]
    [InlineData("del\u007f", Multiline, false)]
    [InlineData("bell\u0007", Multiline, false)]
    public void AnEditIsAcceptedOnlyWithoutForbiddenControls(string text, ControlAskInputMode mode, bool accepted) =>
        Assert.Equal(accepted, AskFieldText.Accepts(text, mode));

    [Fact]
    public void AnEditOverTheCapIsRefused()
    {
        Assert.True(AskFieldText.Accepts(new string('x', AskInputForm.MaxTextBytes), Line));
        Assert.False(AskFieldText.Accepts(new string('x', AskInputForm.MaxTextBytes + 1), Line));
        Assert.False(AskFieldText.Accepts(new string('é', AskInputForm.MaxTextBytes / 2 + 1), Line));
    }

    [Theory]
    [InlineData("one\r\ntwo", Multiline, "one\ntwo")]
    [InlineData("one\ntwo", Multiline, "one\ntwo")]
    [InlineData("one\rtwo", Multiline, null)]
    [InlineData("one\ntwo", Line, null)]
    [InlineData("one\r\ntwo", Line, null)]
    [InlineData("plain", Line, "plain")]
    public void APasteIsTakenWholeOrRefused(string clipboard, ControlAskInputMode mode, string? inserted) =>
        Assert.Equal(inserted, AskFieldText.PasteInsertion(clipboard, mode));
}
