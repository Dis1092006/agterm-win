using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

// The read grant of an HTML file page (upstream HtmlOverlay.grantError, 11fe64eb), with Windows
// paths: a drive root is refused like "/", and the user profile like the home directory.
public class HtmlOverlayGrantTests
{
    private const string Home = @"C:\Users\me";

    [Fact]
    public void ARelativeFileIsRefused() =>
        Assert.Equal("html file must be an absolute path", HtmlOverlay.GrantError(@"page\r.html", null, Home));

    [Fact]
    public void AFileWithoutAGrantPasses() =>
        Assert.Null(HtmlOverlay.GrantError(@"C:\work\r.html", null, Home));

    [Fact]
    public void ARelativeGrantIsRefused() =>
        Assert.Equal("cwd must be an absolute path", HtmlOverlay.GrantError(@"C:\work\r.html", "work", Home));

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"D:\")]
    [InlineData(@"C:\Users\me")]
    [InlineData(@"c:\users\ME\")]
    public void ADriveRootOrTheHomeDirectoryIsRefused(string grant) =>
        Assert.Equal("cwd must not be / or the home directory",
                     HtmlOverlay.GrantError(@"C:\Users\me\r.html", grant, Home));

    [Fact]
    public void TheFileItselfIsNotAGrant() =>
        Assert.Equal("cwd must be a directory containing the html file",
                     HtmlOverlay.GrantError(@"C:\work\r.html", @"C:\work\r.html", Home));

    [Theory]
    [InlineData(@"C:\work\r.html", @"C:\other")]
    [InlineData(@"C:\workshop\r.html", @"C:\work")]
    [InlineData(@"D:\work\r.html", @"C:\work")]
    public void AFileOutsideTheGrantIsRefused(string file, string grant) =>
        Assert.Equal("html file is outside cwd", HtmlOverlay.GrantError(file, grant, Home));

    [Theory]
    [InlineData(@"C:\work\r.html", @"C:\work")]
    [InlineData(@"C:\work\docs\deep\r.html", @"C:\work\")]
    [InlineData(@"C:\Work\R.html", @"c:\work")]
    [InlineData(@"C:\work\docs\..\r.html", @"C:\work\.\")]
    public void AFileInsideTheGrantPasses(string file, string grant) =>
        Assert.Null(HtmlOverlay.GrantError(file, grant, Home));

    [Fact]
    public void ContainsComparesWholeComponents()
    {
        Assert.True(HtmlOverlay.Contains(@"C:\work", @"C:\work\a\b.html"));
        Assert.False(HtmlOverlay.Contains(@"C:\work", @"C:\workshop\b.html"));
        Assert.True(HtmlOverlay.Contains(@"C:\work", @"C:\work"));
    }
}
