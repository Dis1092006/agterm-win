using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

/// <summary>The app-wide selection buffer and its middle-click paste (docs/PLATFORM-NOTES.md
/// "Selection buffer and middle-click paste"; Ghostty Surface.zig setSelectionAndCopy and the
/// middle-press paste).</summary>
public class SelectionBufferTests
{
    [Theory]
    [InlineData(CopyOnSelectMode.Selection, "abc", false)]
    [InlineData(CopyOnSelectMode.Clipboard, "abc", true)]
    [InlineData(CopyOnSelectMode.Off, null, false)]
    public void A_selection_is_kept_by_mode_and_the_clipboard_gets_it_only_in_clipboard_mode(
        CopyOnSelectMode mode, string? kept, bool toClipboard)
    {
        var buffer = new SelectionBuffer();

        Assert.Equal(toClipboard, buffer.Store("abc", mode));
        Assert.Equal(kept, buffer.Text);
    }

    [Fact]
    public void An_empty_selection_changes_nothing()
    {
        var buffer = new SelectionBuffer();
        buffer.Store("abc", CopyOnSelectMode.Selection);

        Assert.False(buffer.Store("", CopyOnSelectMode.Clipboard));
        Assert.Equal("abc", buffer.Text);
    }

    [Fact]
    public void A_later_selection_replaces_the_earlier_one()
    {
        var buffer = new SelectionBuffer();
        buffer.Store("first", CopyOnSelectMode.Selection);
        buffer.Store("second", CopyOnSelectMode.Clipboard);

        Assert.Equal("second", buffer.Text);
    }

    [Theory]
    [InlineData(CopyOnSelectMode.Selection, "kept")]
    [InlineData(CopyOnSelectMode.Off, "kept")]
    [InlineData(CopyOnSelectMode.Clipboard, "from clipboard")]
    public async Task Middle_click_pastes_the_buffer_or_in_clipboard_mode_the_clipboard(CopyOnSelectMode mode, string pasted)
    {
        var buffer = new SelectionBuffer();
        buffer.Store("kept", CopyOnSelectMode.Selection);
        var sent = new List<string>();

        await buffer.PasteAsync(mode, bracketed: false, () => Task.FromResult<string?>("from clipboard"),
            _ => throw new InvalidOperationException("a single line needs no confirmation"),
            text => { sent.Add(text); return Task.CompletedTask; });

        Assert.Equal([pasted], sent);
    }

    [Theory]
    [InlineData(CopyOnSelectMode.Selection)]
    [InlineData(CopyOnSelectMode.Clipboard)]
    public async Task Nothing_to_paste_pastes_nothing(CopyOnSelectMode mode)
    {
        var buffer = new SelectionBuffer();
        var sent = new List<string>();

        await buffer.PasteAsync(mode, bracketed: false, () => Task.FromResult<string?>(null),
            _ => throw new InvalidOperationException("nothing to confirm"),
            text => { sent.Add(text); return Task.CompletedTask; });

        Assert.Empty(sent);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task A_line_break_without_bracketed_paste_asks_first(bool confirmed, bool pasted)
    {
        var buffer = new SelectionBuffer();
        buffer.Store("ls\nrm -rf x", CopyOnSelectMode.Selection);
        var asked = new List<string>();
        var sent = new List<string>();

        await buffer.PasteAsync(CopyOnSelectMode.Selection, bracketed: false, () => Task.FromResult<string?>(null),
            text => { asked.Add(text); return Task.FromResult(confirmed); },
            text => { sent.Add(text); return Task.CompletedTask; });

        Assert.Equal(["ls\nrm -rf x"], asked);
        Assert.Equal(pasted ? ["ls\nrm -rf x"] : [], sent);
    }

    [Theory]
    [InlineData("one line", false, true)]
    [InlineData("two\nlines", false, false)]
    [InlineData("carriage\rreturn", false, false)]
    [InlineData("two\nlines", true, true)]
    [InlineData("end \u001b[201~ of frame", true, false)]
    [InlineData("end \u001b[201~ of frame", false, false)]
    [InlineData("tab\tinside", false, true)]
    public void A_paste_is_safe_unless_it_can_run_a_command(string text, bool bracketed, bool safe)
    {
        Assert.Equal(safe, PasteProtection.IsSafe(text, bracketed));
    }
}
