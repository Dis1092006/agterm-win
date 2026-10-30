using AgTerm.App;

namespace AgTerm.Xterm.Tests;

public class PasteConfirmationTests
{
    [Fact]
    public void Without_bracketed_paste_the_message_warns_that_each_line_can_run()
    {
        Assert.Equal("Paste this text?", PasteConfirmation.Title);
        Assert.Contains("each line", PasteConfirmation.Message(bracketed: false));
        Assert.Contains("end of a bracketed paste", PasteConfirmation.Message(bracketed: true));
        Assert.Equal("Paste", PasteConfirmation.PasteButton);
    }

    [Fact]
    public void The_preview_shows_escape_and_keeps_line_breaks()
    {
        Assert.Equal("a\n\u241B[201~b", PasteConfirmation.Preview("a\n\u001b[201~b"));
    }

    [Fact]
    public void The_preview_is_cut_after_its_limit()
    {
        var preview = PasteConfirmation.Preview(new string('x', PasteConfirmation.PreviewLimit + 5));
        Assert.Equal(new string('x', PasteConfirmation.PreviewLimit) + "\u2026", preview);
    }
}
