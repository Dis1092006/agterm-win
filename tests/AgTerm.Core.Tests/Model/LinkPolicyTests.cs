using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

public class LinkPolicyTests
{
    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com/path?q=1#frag")]
    [InlineData("HTTPS://EXAMPLE.COM")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("ftp://host/file.txt")]
    public void Permitted_urls_open(string target) =>
        Assert.Equal(new HudLinkAction.Open(target), LinkPolicy.ActionFor(target));

    [Theory]
    [InlineData(@"C:\Users\me\scratchpad\snapshot.txt", @"C:\Users\me\scratchpad\snapshot.txt")]
    [InlineData(@"C:\my files\файл.txt", @"C:\my files\файл.txt")]
    [InlineData(@"C:/tmp/../my files/x.txt", @"C:\my files\x.txt")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData("file:///C:/Users/me/a%20b.txt", @"C:\Users\me\a b.txt")]
    [InlineData("file:/C:/tmp/../x.txt", @"C:\x.txt")]
    [InlineData("file://localhost/C:/x.txt", @"C:\x.txt")]
    [InlineData("FILE://LOCALHOST./C:/x.txt", @"C:\x.txt")]
    [InlineData("file://MYHOST./C:/x.txt", @"C:\x.txt")]
    [InlineData("file://myhost.local/C:/x.txt", @"C:\x.txt")]
    [InlineData("file://%6Cocalhost/C:/x.txt", @"C:\x.txt")]
    [InlineData("file://%4DYHOST/C:/x.txt", @"C:\x.txt")]
    [InlineData("file:///C:/tmp/%2E%2E/net/x.txt", @"C:\net\x.txt")]
    [InlineData("file:///C:/a%23b%25.txt", @"C:\a#b%.txt")]
    [InlineData("file:///C:/%D1%84.txt", @"C:\ф.txt")]
    public void Local_files_reveal_without_filesystem_access(string target, string path) =>
        Assert.Equal(new HudLinkAction.Reveal(path), LinkPolicy.ActionFor(target, ["myhost.local", "localhost"]));

    [Fact]
    public void The_default_local_hosts_include_the_machine_name() =>
        Assert.Equal(new HudLinkAction.Reveal(@"C:\x.txt"),
            LinkPolicy.ActionFor($"file://{Environment.MachineName}/C:/x.txt"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("javascript:alert(1)")]
    [InlineData("vscode://file/x")]
    [InlineData("ssh://host/x")]
    [InlineData("tel:+15550100")]
    [InlineData("example.com")]
    [InlineData("src/file.txt")]
    [InlineData("/tmp/file.txt")]
    [InlineData(@"C:relative.txt")]
    [InlineData(@"\root-relative.txt")]
    [InlineData(@"\\server\share\x.txt")]
    [InlineData(@"\\?\C:\x.txt")]
    [InlineData(@"\\.\C:\x.txt")]
    [InlineData("https://example.com extra junk")]
    [InlineData("https://example.com\u0000/etc/other")]
    [InlineData("file://otherhost/C:/x.txt")]
    [InlineData("file://localhost")]
    [InlineData("file:relative")]
    [InlineData("file:///tmp/x.txt")]
    [InlineData("file:////server/share/x.txt")]
    [InlineData("file://localhost//server/share/x.txt")]
    [InlineData("file://localhost/%2Fserver/share")]
    [InlineData("file:///%5C%5Cserver%5Cshare")]
    [InlineData("file:///C:/x%00.txt")]
    [InlineData("file:///C:/x%22.txt")]
    [InlineData("file:///C:/x%3Astream")]
    [InlineData("file:///C:/x%ZZ")]
    [InlineData("file:///C:relative.txt")]
    [InlineData(@"C:\x:stream")]
    [InlineData(@"C:\a""b.txt")]
    [InlineData(@"C:\a|b.txt")]
    [InlineData(@"C:\*.txt")]
    [InlineData(@"C:\logs\NUL")]
    [InlineData(@"C:\logs\con.txt")]
    [InlineData(@"C:\logs\COM1\x.txt")]
    [InlineData(@"C:\logs\LPT9.txt")]
    [InlineData("C:\\logs\\COM\u00B9.txt")]
    [InlineData(@"C:\logs\x. ")]
    [InlineData(@"C:\logs\x.")]
    public void Unsafe_or_unsupported_targets_are_ignored(string? target) =>
        Assert.Null(LinkPolicy.ActionFor(target));
}
