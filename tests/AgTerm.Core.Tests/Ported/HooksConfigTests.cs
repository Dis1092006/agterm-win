using AgTerm.Core.Hooks;
using AgTerm.Core.Keymap;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Ported;

// Port of HooksTests.swift (upstream a4944436).
public sealed class HooksConfigTests
{
    private static readonly ControlEventKind[] AllKinds = ControlEventKinds.All.ToArray();

    [Fact]
    public void EveryRingKindParsesWithItsRemainder()
    {
        var text = string.Join("\n", AllKinds.Select(k => $"on {k.ToWire()} echo {k.ToWire()}"));
        var (hooks, diagnostics) = HooksParser.Parse(text);

        Assert.Empty(diagnostics);
        Assert.Equal(AllKinds, hooks.Entries.Select(e => e.Kind));
        Assert.Equal(AllKinds.Select(k => $"echo {k.ToWire()}"), hooks.Entries.Select(e => e.Command));
        Assert.Equal(Enumerable.Range(1, AllKinds.Length), hooks.Entries.Select(e => e.Line));
    }

    [Fact]
    public void SeveralLinesPerKindKeepFileOrder()
    {
        var (hooks, diagnostics) = HooksParser.Parse("on status ~/a.sh\non session.closed ~/c.sh\non status ~/b.sh");

        Assert.Empty(diagnostics);
        Assert.Equal(new[] { "~/a.sh", "~/c.sh", "~/b.sh" }, hooks.Entries.Select(e => e.Command));
        Assert.Equal(new[] { ControlEventKind.Status, ControlEventKind.SessionClosed, ControlEventKind.Status },
            hooks.Entries.Select(e => e.Kind));
    }

    [Fact]
    public void BlankAndWholeLineCommentsAreIgnoredAndCrLfIsNormalized()
    {
        var text = "# header\r\n\r\n   # indented comment\r\non status echo a\r\n\r\non notify echo b\r\n";
        var (hooks, diagnostics) = HooksParser.Parse(text);

        Assert.Empty(diagnostics);
        Assert.Equal(new[] { "echo a", "echo b" }, hooks.Entries.Select(e => e.Command));
        Assert.Equal(new[] { 4, 6 }, hooks.Entries.Select(e => e.Line));
    }

    [Theory]
    [InlineData("echo 'fix #42'")]
    [InlineData("echo \"a \\\" b\"")]
    [InlineData("ls | grep x")]
    [InlineData("echo hi >> ~/log.txt")]
    [InlineData("echo $(date) $HOME")]
    [InlineData("printf '%s\\n' a")]
    [InlineData("echo  two  spaces")]
    [InlineData("~/bin/x.sh")]
    [InlineData("echo hi # not a comment")]
    public void ShellRemainderIsPreservedVerbatim(string command)
    {
        var (hooks, diagnostics) = HooksParser.Parse($"on status \t {command} \t ");

        Assert.Empty(diagnostics);
        Assert.Equal(new[] { command }, hooks.Entries.Select(e => e.Command));
    }

    [Fact]
    public void IdentityIsKindPlusCommandRegardlessOfSpacingAndLine()
    {
        var (hooks, diagnostics) = HooksParser.Parse(
            "on   status\t   echo x\n# a comment between them changes the line number, never the identity\non status echo x\non notify echo x");

        Assert.Equal(new[] { "echo x", "echo x" }, hooks.Entries.Select(e => e.Command));
        Assert.Equal(new[] { ControlEventKind.Status, ControlEventKind.Notify }, hooks.Entries.Select(e => e.Kind));
        Assert.Equal(1, hooks.Entries[0].Line);
        Assert.Equal(new[] { new KeymapDiagnostic(3, "hook 'on status echo x' is already defined; hook skipped") },
            diagnostics);
        Assert.Equal(new HookIdentity(ControlEventKind.Status, "echo x"), hooks.Entries[0].Identity);
        Assert.Equal(hooks.Entries[0].Identity, (hooks.Entries[0] with { Line = 99 }).Identity);
    }

    [Fact]
    public void MalformedLinesAreDiagnosedAndLaterLinesStillParse()
    {
        var (hooks, diagnostics) = HooksParser.Parse(
            "map cmd+a toggle_split\non\non bogus echo x\non status\non status \t\non notify echo ok");

        Assert.Equal(new[] { "echo ok" }, hooks.Entries.Select(e => e.Command));
        Assert.Equal(new[] { 6 }, hooks.Entries.Select(e => e.Line));
        Assert.Equal(new[]
        {
            new KeymapDiagnostic(1, "unknown verb 'map'"),
            new KeymapDiagnostic(2, "on requires an event kind"),
            new KeymapDiagnostic(3, "unknown event kind 'bogus'"),
            new KeymapDiagnostic(4, "hook for 'status' has no shell line"),
            new KeymapDiagnostic(5, "hook for 'status' has no shell line"),
        }, diagnostics);
    }

    [Fact]
    public void EmptyTextParsesToNothing()
    {
        var (hooks, diagnostics) = HooksParser.Parse("");

        Assert.Empty(hooks.Entries);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void StoreReadsHooksConfBesideTheKeymap()
    {
        var dir = Directory.CreateTempSubdirectory("agterm-hooks-").FullName;
        try
        {
            var store = new HooksStore(dir);
            Assert.Equal(Path.Combine(dir, "hooks.conf"), store.Path);

            var (missing, missingDiagnostics) = store.Load();
            Assert.Empty(missing.Entries);
            Assert.Empty(missingDiagnostics);

            File.WriteAllText(store.Path, "on status echo a\nbogus\n");
            var (loaded, diagnostics) = store.Load();
            Assert.Equal(new[] { "echo a" }, loaded.Entries.Select(e => e.Command));
            Assert.Equal(new[] { new KeymapDiagnostic(2, "unknown verb 'bogus'") }, diagnostics);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void StoreReportsAnUnreadableFileAsALineZeroDiagnostic()
    {
        var dir = Directory.CreateTempSubdirectory("agterm-hooks-").FullName;
        try
        {
            var store = new HooksStore(dir);
            File.WriteAllBytes(store.Path, new byte[] { 0xFF, 0xFE, 0xFD });

            var (hooks, diagnostics) = store.Load();

            Assert.Empty(hooks.Entries);
            var only = Assert.Single(diagnostics);
            Assert.Equal(0, only.Line);
            Assert.StartsWith("could not read hooks.conf: ", only.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void StarterIsAllCommentsAndNamesEveryKind()
    {
        var starter = HooksStarter.Text();

        var (hooks, diagnostics) = HooksParser.Parse(starter);
        Assert.Empty(hooks.Entries);
        Assert.Empty(diagnostics);
        foreach (var kind in AllKinds) Assert.Contains(kind.ToWire(), starter);
    }

    [Fact]
    public void EnsureStarterWritesOnlyWhenMissing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "agterm-hooks-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new HooksStore(dir);
            Assert.True(store.EnsureStarter());
            Assert.Equal(HooksStarter.Text(), File.ReadAllText(store.Path));

            File.WriteAllText(store.Path, "on status echo mine\n");
            Assert.False(store.EnsureStarter());
            Assert.Equal("on status echo mine\n", File.ReadAllText(store.Path));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
