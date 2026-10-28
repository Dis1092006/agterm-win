namespace AgTerm.Xterm.Tests;

// MIT asks for the copyright notices in every copy, and a publish directory or a CI artifact is a
// copy that travels without the repository. publish.proj and the workflow are not compiled by this
// project, so their wiring is pinned in source.
public sealed class DistributionNoticeTests
{
    [Fact]
    public void Publish_copies_the_license_and_the_third_party_notices_beside_the_exes()
    {
        var publish = Source("publish.proj");

        Assert.Contains(
            "<Copy SourceFiles=\"$(MSBuildThisFileDirectory)LICENSE;$(MSBuildThisFileDirectory)THIRD-PARTY-NOTICES.md\" DestinationFolder=\"$(_OutDir)\" />",
            publish, StringComparison.Ordinal);
    }

    [Fact]
    public void Ci_puts_the_license_and_the_third_party_notices_into_the_published_cli_artifact()
    {
        var workflow = Source(".github", "workflows", "tests.yml");

        Assert.Contains(
            "Copy-Item LICENSE, THIRD-PARTY-NOTICES.md \"artifacts\\publish\\agtermctl\\${{ matrix.rid }}\"",
            workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Third_party_notices_carry_the_theme_pack_license_text_themselves()
    {
        // the publish directory has no Themes\LICENSE, so a pointer to it would lead nowhere
        var notices = Source("THIRD-PARTY-NOTICES.md");

        Assert.Equal(3, notices.Split("Permission is hereby granted, free of charge").Length - 1);
        Assert.DoesNotContain("src/AgTerm.Xterm/Themes/LICENSE", notices, StringComparison.Ordinal);
    }

    private static string Source(params string[] parts) =>
        File.ReadAllText(Path.Combine([FindRepoRoot(), .. parts]));

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
