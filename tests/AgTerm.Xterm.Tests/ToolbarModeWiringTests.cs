namespace AgTerm.Xterm.Tests;

public sealed class ToolbarModeWiringTests
{
    [Fact]
    public void Normal_toolbar_has_two_stacked_labels_and_keeps_native_chrome()
    {
        var xaml = Source("MainWindow.xaml");
        Assert.Contains("<TitleBar.Content>", xaml);
        Assert.Contains("x:Name=\"ToolbarLabels\"", xaml);
        Assert.Contains("x:Name=\"ToolbarTitle\"", xaml);
        Assert.Contains("x:Name=\"ToolbarSubtitle\"", xaml);
        Assert.Contains("IsHitTestVisible=\"False\"", xaml);
        var window = Source("MainWindow.xaml.cs");
        Assert.Contains("AppTitleBar.Height = height;", window);
        Assert.Contains("TitleBarText.ToolbarHeight(settings)", window);
        Assert.Contains("TitleBarText.Subtitle(", window);
        Assert.Contains("_titleBarState.SubtitleDetail", window);
        Assert.Contains("ToolbarLabels.Visibility = normal ? Visibility.Visible : Visibility.Collapsed;", window);
        Assert.DoesNotContain("SetBorderAndTitleBar(", window);
    }

    [Fact]
    public void Settings_picker_displays_the_effective_choice_before_attaching_the_write_handler()
    {
        var window = Source("SettingsWindow.cs");
        var read = window.IndexOf("var selected = editor.ChoiceValue(field.Key);", StringComparison.Ordinal);
        var select = window.IndexOf("combo.SelectedItem = options.FirstOrDefault(Matches);", StringComparison.Ordinal);
        var write = window.IndexOf("combo.SelectionChanged +=", StringComparison.Ordinal);
        Assert.True(read >= 0);
        Assert.True(select > read && write > select);
        var writeEnd = window.IndexOf("if (combo.IsEditable)", write, StringComparison.Ordinal);
        Assert.Contains("if (closed || !combo.IsLoaded) return;", window[write..writeEnd]);
        Assert.DoesNotContain("editor.SetAsync(\"toolbarMode\"", window);
    }

    private static string Source(string file)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
                return File.ReadAllText(Path.Combine(directory.FullName, "src", "AgTerm.App", file));
        throw new InvalidOperationException("Repository root not found.");
    }
}
