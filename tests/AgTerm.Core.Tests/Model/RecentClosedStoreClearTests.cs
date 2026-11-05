using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

public sealed class RecentClosedStoreClearTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "agterm-recent-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static RecentClosedItem Item() => new()
    {
        Id = Guid.NewGuid(),
        Kind = RecentClosedKind.Session,
        Title = "closed",
        ClosedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void AClearThatWritesReportsSuccess()
    {
        var store = new RecentClosedStore(_dir);
        store.Record(Item());

        Assert.True(store.Clear());
        Assert.Empty(store.Load());
    }

    [Fact]
    public void AClearThatCannotWriteReportsTheFailureItself()
    {
        var store = new RecentClosedStore(_dir);
        store.Record(Item());

        // a shared-read handle lets the list still load but blocks the replace
        using (new FileStream(Path.Combine(_dir, "recent-closed.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.False(store.Clear());
    }
}
