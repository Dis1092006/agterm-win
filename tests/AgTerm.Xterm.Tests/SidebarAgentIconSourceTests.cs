namespace AgTerm.Xterm.Tests;

public sealed class SidebarAgentIconSourceTests
{
    [Fact]
    public void Sidebar_view_binds_the_agent_icon_before_the_session_label()
    {
        var xaml = Source("SidebarView.xaml");
        var comment = xaml.IndexOf("<!-- Windows-only: the detected agent CLI icon", StringComparison.Ordinal);
        var labelBorder = xaml.IndexOf("Text=\"{x:Bind Label, Mode=OneWay}\"", StringComparison.Ordinal);
        Assert.True(comment >= 0);
        Assert.True(comment < labelBorder);

        var path = xaml[comment..xaml.IndexOf("</Path>", comment, StringComparison.Ordinal)];
        Assert.Contains("Data=\"{x:Bind AgentIconData, Mode=OneWay}\"", path, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{x:Bind AgentIconVisibility, Mode=OneWay}\"", path, StringComparison.Ordinal);
        Assert.Contains("local:SidebarRowBrushes.Foreground=\"{x:Bind IconForeground, Mode=OneWay}\"", path, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.ToolTip=\"{x:Bind AgentIconToolTip, Mode=OneWay}\"", path, StringComparison.Ordinal);
    }

    [Fact]
    public void Sidebar_row_view_model_formats_the_label_and_notifies_agent_icon_properties_on_update()
    {
        var model = Source("SidebarRowViewModel.cs");
        Assert.Contains("SidebarAgentIcon.FormatLabel(_row, _mode, _settings.EffectiveFlaggedViewLayout, _settings)",
            model, StringComparison.Ordinal);

        var update = MethodBody(model, "public void Update(");
        Assert.Contains("nameof(AgentIconVisibility)", update, StringComparison.Ordinal);
        Assert.Contains("nameof(AgentIconData)", update, StringComparison.Ordinal);
        Assert.Contains("nameof(AgentIconToolTip)", update, StringComparison.Ordinal);
    }

    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} not found");
        var end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        return end < 0 ? source[start..] : source[start..end];
    }

    private static string Source(string appFile) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AgTerm.App", appFile));

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
