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
}
