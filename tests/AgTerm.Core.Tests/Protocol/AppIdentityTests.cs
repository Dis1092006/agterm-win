using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Protocol;

/// <summary>version (upstream 5d22904a, installed 18da29cb): how the running and the on-disk build
/// identities are read and compared.</summary>
public class AppIdentityTests
{
    [Fact]
    public void ParseSplitsTheVersionFromTheSourceRevision()
    {
        Assert.Equal(new ControlAppIdentity { Version = "0.1.73" }, AppIdentity.Parse("0.1.73"));
        Assert.Equal(new ControlAppIdentity { Version = "0.1.73", Commit = "abcdef1" },
            AppIdentity.Parse("0.1.73+abcdef1234567890"));
        Assert.Equal(new ControlAppIdentity { Version = "0.2.0-beta.1", Commit = "abc" },
            AppIdentity.Parse("0.2.0-beta.1+abc"));
    }

    [Theory]
    [InlineData("0.1.73+")]
    [InlineData("0.1.73+unknown")]
    [InlineData("0.1.73+ ")]
    public void ParseDropsAnEmptyOrUnknownCommit(string informational)
    {
        Assert.Equal(new ControlAppIdentity { Version = "0.1.73" }, AppIdentity.Parse(informational));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("+abc")]
    public void ParseRefusesAnUnreadableVersion(string? informational)
    {
        Assert.Null(AppIdentity.Parse(informational));
    }

    [Fact]
    public void ResultOmitsInstalledWhenItEqualsTheRunningBuild()
    {
        var running = new ControlAppIdentity { Version = "1.0", Commit = "abc" };

        var result = AppIdentity.Result(running, new ControlAppIdentity { Version = "1.0", Commit = "abc" });

        Assert.Equal(running, result.App);
        Assert.Null(result.Installed);
    }

    [Fact]
    public void ResultOmitsInstalledWhenItCannotBeRead()
    {
        var result = AppIdentity.Result(new ControlAppIdentity { Version = "1.0" }, installed: null);

        Assert.Null(result.Installed);
    }

    [Fact]
    public void ResultKeepsInstalledWhenItDiffers()
    {
        var installed = new ControlAppIdentity { Version = "1.1" };

        var result = AppIdentity.Result(new ControlAppIdentity { Version = "1.0" }, installed);

        Assert.Equal(installed, result.Installed);
    }

    [Fact]
    public void RunningReadsTheInformationalVersionOfAnAssembly()
    {
        var running = AppIdentity.Running(typeof(AppIdentity).Assembly);

        Assert.False(string.IsNullOrWhiteSpace(running.Version));
        Assert.DoesNotContain('+', running.Version);
    }

    [Fact]
    public void InstalledReadsTheProductVersionOfAFileOnDisk()
    {
        var assembly = typeof(AppIdentity).Assembly;

        Assert.Equal(AppIdentity.Running(assembly), AppIdentity.Installed(assembly.Location));
    }

    [Fact]
    public void InstalledIsNullForAMissingFileOrANullPath()
    {
        Assert.Null(AppIdentity.Installed(null));
        Assert.Null(AppIdentity.Installed(Path.Combine(Path.GetTempPath(), "agterm-no-such-file.exe")));
    }
}
