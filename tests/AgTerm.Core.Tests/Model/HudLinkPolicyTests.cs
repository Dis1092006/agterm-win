using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

// The link rule of upstream LinkPolicy.disposition plus HudMarkdown.linkTarget (agtermCore, ed831ab),
// as spec 5.4 narrows it for Windows.
public class HudLinkPolicyTests
{
    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com/path?q=1#frag")]
    [InlineData("HTTPS://EXAMPLE.COM")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("ftp://host/file.txt")]
    [InlineData("https://example.com/a%1Bb")]
    [InlineData("https://example.com/%D1%84")]
    [InlineData("file:///tmp/x.md")]
    [InlineData("file:/tmp/x.md")]
    [InlineData("file://localhost/tmp/x.md")]
    [InlineData("file://LOCALHOST/tmp/x.md")]
    [InlineData("file://localhost./tmp/x.md")]
    // upstream normalizedHost drops one trailing dot, so "." is an empty host
    [InlineData("file://./x")]
    [InlineData("FILE:///tmp/x.md")]
    [InlineData("file:///tmp/../tmp/x.md")]
    [InlineData("file:///tmp/a%20b.md")]
    [InlineData("file:///C:/Users/me/build.log")]
    public void AnAcceptedLinkIsItsOwnTarget(string url) => Assert.Equal(url, HudLinkPolicy.Target(url));

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("x-custom://do")]
    [InlineData("vscode://file/x")]
    [InlineData("tel:+15550100")]
    [InlineData("relative/path.md")]
    [InlineData("example.com")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://example.com extra junk")]
    [InlineData("https://example.com\u0000/etc/other")]
    [InlineData("https://example.com/\u0444")]
    [InlineData("file://otherhost/tmp/x.md")]
    [InlineData("file://other.host/tmp/x")]
    [InlineData("file://remote.example.com/share/x.md")]
    [InlineData("file://localhost")]
    [InlineData("file:relative")]
    [InlineData("file:////server/share/x.md")]
    [InlineData("file://localhost//server/share/x.md")]
    [InlineData("file://localhost/%2Fserver/share")]
    [InlineData("file:///%5C%5Cserver%5Cshare")]
    [InlineData("file:///tmp/a%00b")]
    [InlineData("file://.../x")]
    [InlineData("file://../x")]
    [InlineData("file://localhost../x")]
    public void AnythingElseIsAPlainLabel(string url) => Assert.Null(HudLinkPolicy.Target(url));

    [Fact]
    public void ANullUrlIsAPlainLabel() => Assert.Null(HudLinkPolicy.Target(null));

    // Upstream also takes this machine's own host name (gethostname); spec 5.4 takes an empty or localhost host only.
    [Fact]
    public void TheMachinesOwnHostNameIsNotLocalHere() =>
        Assert.Null(HudLinkPolicy.Target("file://" + Environment.MachineName + "/tmp/x.md"));

    // Upstream ignores the macOS auto-mount roots /net, /Network and /home; on Windows they are ordinary folders.
    [Fact]
    public void MacAutoMountRootsAreOrdinaryPathsHere() =>
        Assert.Equal("file:///net/server/share", HudLinkPolicy.Target("file:///net/server/share"));

    [Theory]
    [InlineData("https://example.com/a")]
    [InlineData("mailto:someone@example.com")]
    public void AWebOrMailLinkOpensWithTheSystemHandler(string url) =>
        Assert.Equal(new HudLinkAction.Open(url), HudLinkPolicy.ActionFor(url));

    [Theory]
    [InlineData("file:///C:/Users/me/a%20b.txt", @"C:\Users\me\a b.txt")]
    [InlineData("file://localhost/C:/tmp/../tmp/x.md", @"C:\tmp\x.md")]
    [InlineData("file:///C:", @"C:\")]
    [InlineData("FILE:///D:/logs/build.log", @"D:\logs\build.log")]
    public void ALocalFileLinkIsRevealedAtItsWindowsPath(string url, string path) =>
        Assert.Equal(new HudLinkAction.Reveal(path), HudLinkPolicy.ActionFor(url));

    // a path with no drive is rooted on the current drive, as Windows reads \tmp\x.md
    [Fact]
    public void APathWithNoDriveIsRootedOnTheCurrentDrive() =>
        Assert.Equal(new HudLinkAction.Reveal(Path.GetFullPath(@"\tmp\x.md")), HudLinkPolicy.ActionFor("file:///tmp/x.md"));

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file://otherhost/tmp/x.md")]
    [InlineData("file:///C:/a%22b")]
    [InlineData("file:///C:x")]
    [InlineData("file:///C:..")]
    // a colon after the drive names an NTFS stream, never a path to reveal
    [InlineData("file:///C:/a%20b/c:")]
    [InlineData("file:///C:/a,C:/Windows/System32/calc.exe,x:")]
    [InlineData("file:///C:/x:stream")]
    public void AnythingElseDoesNothing(string url) => Assert.Null(HudLinkPolicy.ActionFor(url));

    // Windows argument splitting reads a backslash right before a closing quote as an escaped quote,
    // so "C:\" would swallow the quote; a path with no space needs no quotes at all
    [Theory]
    [InlineData(@"C:\Users\me\a b.txt", @"/select,""C:\Users\me\a b.txt""")]
    [InlineData(@"D:\logs\build.log", @"/select,""D:\logs\build.log""")]
    [InlineData(@"C:\", @"/select,C:\")]
    [InlineData(@"C:\my dir\", @"/select,""C:\my dir""")]
    [InlineData(@"C:\logs\", @"/select,""C:\logs""")]
    [InlineData(@"C:\a b\c", @"/select,""C:\a b\c""")]
    [InlineData(@"C:\a,C:\Windows\System32\calc.exe,x", @"/select,""C:\a,C:\Windows\System32\calc.exe,x""")]
    [InlineData(@"C:\a b\c:", @"/select,""C:\a b\c:""")]
    public void ExplorerSelectsTheRevealedPathWithNoBackslashBeforeTheClosingQuote(string path, string arguments) =>
        Assert.Equal(arguments, HudLinkPolicy.ExplorerSelectArguments(path));

    [Fact]
    public void ADriveRootLinkSelectsTheDriveInExplorer() =>
        Assert.Equal(@"/select,C:\",
            HudLinkPolicy.ExplorerSelectArguments(((HudLinkAction.Reveal)HudLinkPolicy.ActionFor("file:///C:")!).Path));
}
