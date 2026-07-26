using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// Parsing the shell's cwd report into Session.CurrentCwd. Two wire forms reach the bridge
/// (PLATFORM-NOTES): OSC 7 carries a file:// URI (the upstream/macOS convention); on Windows the
/// native convention is ConEmu-style OSC 9;9 with a raw (possibly quoted) path — the bridge
/// forwards both payloads verbatim and this parser normalizes them.
/// </summary>
public class CwdReportTests
{
    [Theory]
    // OSC 7 file URIs: drive paths lose the leading slash and use backslashes
    [InlineData("file:///C:/Users/me", "C:\\Users\\me")]
    [InlineData("file:///C:/a%20b/proj", "C:\\a b\\proj")]
    [InlineData("file://localhost/C:/x", "C:\\x")]
    // a unix-shaped URI path (WSL shell over ssh etc.) stays forward-slashed
    [InlineData("file:///home/u/proj", "/home/u/proj")]
    // OSC 9;9 raw paths: used verbatim, surrounding quotes stripped (WT's documented form quotes it)
    [InlineData("C:\\git\\proj", "C:\\git\\proj")]
    [InlineData("\"C:\\git\\pro j\"", "C:\\git\\pro j")]
    public void Parses_osc7_uris_and_raw_paths(string payload, string expected)
    {
        Assert.Equal(expected, CwdReport.Parse(payload));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("file://otherhost/share/x")] // a foreign-host URI is not a local cwd
    public void Rejects_blank_or_foreign_payloads(string payload)
    {
        Assert.Null(CwdReport.Parse(payload));
    }
}
