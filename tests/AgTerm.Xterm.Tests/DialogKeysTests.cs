using AgTerm.App;

namespace AgTerm.Xterm.Tests;

public sealed class DialogKeysTests
{
    private const int Enter = 0x0D;
    private const int Escape = 0x1B;
    private const int Space = 0x20;

    [Fact]
    public void An_auto_repeated_enter_is_ignored_so_a_held_enter_never_confirms_a_dialog()
    {
        Assert.True(DialogKeys.IgnoresKey(Enter, wasKeyDown: true));
    }

    [Theory]
    [InlineData(Enter, false)]
    [InlineData(Escape, true)]
    [InlineData(Space, true)]
    public void A_fresh_enter_and_other_keys_reach_the_dialog(int key, bool wasKeyDown)
    {
        Assert.False(DialogKeys.IgnoresKey(key, wasKeyDown));
    }

    // MainWindow is a WinUI type the test project does not compile, so the guard on its dialogs is
    // pinned in its source.
    [Fact]
    public void Every_main_window_dialog_is_shown_through_the_held_enter_guard()
    {
        var code = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AgTerm.App", "MainWindow.xaml.cs"));

        var show = code.IndexOf("private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)", StringComparison.Ordinal);
        Assert.True(show >= 0, "ShowDialogAsync not found");
        var body = code[show..];
        body = body[..body.IndexOf("\n    }", StringComparison.Ordinal)];
        Assert.Contains("DialogKeys.IgnoresKey(", body, StringComparison.Ordinal);
        Assert.Contains("dialog.ShowAsync()", body, StringComparison.Ordinal);
        Assert.Contains("_nativeDialogs++;", body, StringComparison.Ordinal);
        Assert.Contains("finally", body, StringComparison.Ordinal);
        Assert.Contains("_nativeDialogs--;", body, StringComparison.Ordinal);
        Assert.Equal(2, Count(body, "RefreshUiAutoFollowSuppression();"));
        Assert.Equal(1, Count(code, ".ShowAsync()"));
        Assert.Equal(Count(code, "new ContentDialog"), Count(code, "ShowDialogAsync(dialog)"));
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not find the repository root.");
    }
}
