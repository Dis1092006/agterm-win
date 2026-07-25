using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

public class PersistenceStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "agterm-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private PersistenceStore NewStore() => new(_dir);

    [Fact]
    public void MissingFileLoadsADefaultEmptySnapshot()
    {
        var snapshot = NewStore().Load();
        Assert.Equal(Snapshot.CurrentVersion, snapshot.Version);
        Assert.Empty(snapshot.Workspaces);
        Assert.Null(snapshot.SelectedSessionId);
    }

    [Fact]
    public void CorruptJsonLoadsADefaultEmptySnapshot()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "workspaces.json"), "{not json");
        Assert.Empty(NewStore().Load().Workspaces);
    }

    [Fact]
    public void VersionMismatchLoadsADefaultEmptySnapshot()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "workspaces.json"),
            "{\"version\":999,\"workspaces\":[{\"id\":\"" + Guid.NewGuid() + "\",\"name\":\"w\",\"sessions\":[]}]}");
        Assert.Empty(NewStore().Load().Workspaces);
    }

    [Fact]
    public void SaveThenLoadRoundTripsAndCreatesTheDirectory()
    {
        var store = NewStore();
        var snapshot = new Snapshot
        {
            Version = Snapshot.CurrentVersion,
            Workspaces = new[]
            {
                new WorkspaceSnapshot
                {
                    Id = Guid.NewGuid(), Name = "work",
                    Sessions = new[] { new SessionSnapshot { Id = Guid.NewGuid(), Cwd = "/home" } },
                },
            },
            SidebarWidth = 200.0,
        };

        store.Save(snapshot);
        var loaded = store.Load();

        Assert.Equal(snapshot.Workspaces[0].Id, loaded.Workspaces[0].Id);
        Assert.Equal("/home", loaded.Workspaces[0].Sessions[0].Cwd);
        Assert.Equal(200.0, loaded.SidebarWidth);
    }

    [Fact]
    public void SaveOverwritesThePreviousSnapshot()
    {
        var store = NewStore();
        store.Save(new Snapshot { Version = 1, Workspaces = Array.Empty<WorkspaceSnapshot>() });
        store.Save(new Snapshot
        {
            Version = 1,
            Workspaces = new[]
            {
                new WorkspaceSnapshot { Id = Guid.NewGuid(), Name = "second", Sessions = Array.Empty<SessionSnapshot>() },
            },
        });

        Assert.Equal("second", store.Load().Workspaces[0].Name);
    }

    [Fact]
    public void PerWindowFileNameIsHonored()
    {
        var store = new PersistenceStore(_dir, "windows-test.json");
        store.Save(new Snapshot { Version = 1, Workspaces = Array.Empty<WorkspaceSnapshot>() });
        Assert.True(File.Exists(Path.Combine(_dir, "windows-test.json")));
    }
}
