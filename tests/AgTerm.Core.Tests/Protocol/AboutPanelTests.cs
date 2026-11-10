using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Protocol;

/// <summary>The About dialog: upstream's standard About panel (version with the build commit in
/// parentheses, a website link, the copyright), plus a line naming agterm.</summary>
public class AboutPanelTests
{
    [Fact]
    public void TheVersionLineCarriesTheCommitInParentheses()
    {
        Assert.Equal("Version 0.1.120 (80f4fed)",
            AboutPanel.VersionLine(new ControlAppIdentity { Version = "0.1.120", Commit = "80f4fed" }));
    }

    [Fact]
    public void AVersionWithoutACommitIsShownAlone()
    {
        Assert.Equal("Version 0.1.120", AboutPanel.VersionLine(new ControlAppIdentity { Version = "0.1.120" }));
    }

    [Fact]
    public void TheLinksPointAtAgtermAndThisRepository()
    {
        Assert.Equal("https://agterm.com", AboutPanel.UpstreamUrl);
        Assert.Equal("https://github.com/Dis1092006/agterm-win", AboutPanel.RepositoryUrl);
        Assert.Equal("github.com/Dis1092006/agterm-win", AboutPanel.RepositoryText);
    }

    [Fact]
    public void TheCopyrightIsTheOneInTheLicense()
    {
        var license = File.ReadAllLines(Path.Combine(RepositoryRoot(), "LICENSE"));
        var line = Assert.Single(license, l => l.StartsWith("Copyright (c) ", StringComparison.Ordinal));

        Assert.Equal(AboutPanel.Copyright, "© " + line["Copyright (c) ".Length..]);
    }

    private static string RepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "AgTerm.slnx")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir!;
    }
}
