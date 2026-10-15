using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

/// <summary>The embedded pack is upstream's set: the same names as the files in the repo, and every
/// one of them parses. A glob that drops a file or a pack update that changes the format fails here.</summary>
public sealed class EmbeddedThemePackTests
{
    private const int UpstreamThemeCount = 603;

    [Fact]
    public void The_embedded_names_equal_the_theme_files_in_the_repo()
    {
        var themes = Path.Combine(FindRepoRoot(), "src", "AgTerm.Xterm", "Themes");
        var expected = Directory.GetFiles(Path.Combine(themes, "ghostty"))
            .Select(path => Path.GetFileName(path))
            .Append("agterm")
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(expected, ThemeCatalog.Default.Names);
    }

    [Fact]
    public void The_pack_has_upstreams_count_and_no_errors()
    {
        Assert.Equal(UpstreamThemeCount, ThemeCatalog.Default.Names.Count);
        Assert.Empty(ThemeCatalog.Default.Errors);
    }

    [Theory]
    [InlineData("agterm")]
    [InlineData("Hopscotch.256")]
    [InlineData("Black Metal (Bathory)")]
    [InlineData("Dracula+")]
    public void Names_with_dots_spaces_parentheses_and_plus_resolve(string name)
    {
        Assert.Equal(name, ThemeCatalog.Default.Resolve(name)?.Name);
    }

    [Fact]
    public void The_license_and_the_source_note_are_not_themes()
    {
        Assert.DoesNotContain("LICENSE", ThemeCatalog.Default.Names);
        Assert.DoesNotContain("SOURCE.md", ThemeCatalog.Default.Names);
        Assert.DoesNotContain("SOURCE", ThemeCatalog.Default.Names);
    }

    [Fact]
    public void Builtin_light_is_ghosttys_file_not_the_old_windows_console_colors()
    {
        var theme = Assert.IsType<TerminalTheme>(ThemeCatalog.Default.Resolve("Builtin Light"));

        Assert.Equal("#ffffff", theme.BackgroundColor);
        Assert.Equal("#bb0000", theme.AnsiColors[1]);
        Assert.Equal("#b5d5ff", theme.SelectionBackgroundColor);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AgTerm.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
