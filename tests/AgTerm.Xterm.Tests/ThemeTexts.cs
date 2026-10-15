namespace AgTerm.Xterm.Tests;

/// <summary>Ghostty theme file text for catalog tests that need a small catalog of their own.</summary>
internal static class ThemeTexts
{
    public static string Valid(string background = "#282a36") => string.Join("\n",
    [
        .. Enumerable.Range(0, 16).Select(i => $"palette = {i}=#{i:x2}{i:x2}{i:x2}"),
        $"background = {background}",
        "foreground = #f8f8f2",
        "cursor-color = #f8f8f2",
        "cursor-text = #282a36",
        "selection-background = #44475a",
        "selection-foreground = #ffffff",
    ]);
}
