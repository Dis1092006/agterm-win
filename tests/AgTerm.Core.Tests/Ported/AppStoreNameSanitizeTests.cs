using AgTerm.Core.Model;
using Xunit;

namespace AgTerm.Core.Tests.Ported;

// Ported from agtermCore/Tests/agtermCoreTests/AppStoreNameSanitizeTests.swift (upstream 97d4e6af).
public class AppStoreNameSanitizeTests
{
    [Fact]
    public void AddSessionStripsInteriorControlCharactersFromNameAndCwd()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, cwd: "/work\ntouch /tmp/pwned", name: "demo\ntouch /tmp/pwned")!;
        Assert.Equal("demotouch /tmp/pwned", session.CustomName);
        Assert.Equal("/worktouch /tmp/pwned", session.InitialCwd);
        Assert.Equal("/worktouch /tmp/pwned", session.EffectiveCwd);
    }

    [Fact]
    public void RenameSessionStripsInteriorControlCharacters()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        var session = store.AddSession(ws.Id, "/a")!;
        store.RenameSession(session.Id, "  demo\ntouch /tmp/pwned  ");
        Assert.Equal("demotouch /tmp/pwned", session.CustomName);
    }

    [Fact]
    public void AddWorkspaceStripsInteriorControlCharacters()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("prod\ntouch /tmp/pwned");
        Assert.Equal("prodtouch /tmp/pwned", ws.Name);
    }

    [Fact]
    public void RenameWorkspaceStripsInteriorControlCharacters()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("work");
        store.RenameWorkspace(ws.Id, "prod\ntouch /tmp/pwned");
        Assert.Equal("prodtouch /tmp/pwned", ws.Name);
    }

    [Fact]
    public void WorkspaceNamedSanitizesTheNeedle()
    {
        var store = AppStoreFixtures.MakeStore();
        var ws = store.AddWorkspace("prod\ntouch /tmp/pwned");
        Assert.Equal(ws.Id, store.WorkspaceNamed("prod\ntouch /tmp/pwned")?.Id);
    }

    [Fact]
    public void EnsureWorkspaceWithControlCharacterNameStaysIdempotent()
    {
        var store = AppStoreFixtures.MakeStore();
        var first = store.EnsureWorkspace("prod\ntouch /tmp/pwned")!;
        Assert.Equal(first.Id, store.EnsureWorkspace("prod\ntouch /tmp/pwned")?.Id);
        Assert.Single(store.Workspaces, w => w.Name == "prodtouch /tmp/pwned");
        Assert.Null(store.EnsureWorkspace("\u0007"));
    }
}
