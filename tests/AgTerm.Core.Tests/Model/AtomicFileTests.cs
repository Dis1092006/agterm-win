using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

/// <summary>
/// Every stored file is written to a temp file and renamed into place. On Windows that rename fails
/// with UnauthorizedAccessException while any other process holds the destination open, which a
/// virus scanner or a search indexer does for a moment after a file is written. The save then threw
/// out of the host instead of storing anything: seen as LiveApplicationHost.SaveIndexLocked failing
/// a test run. The lock is brief, so the rename is retried.
/// </summary>
public class AtomicFileTests
{
    private static string NewDirectory() =>
        Directory.CreateTempSubdirectory("agterm-atomic-").FullName;

    [Fact]
    public void Replace_waits_for_a_reader_to_let_go()
    {
        var directory = NewDirectory();
        var destination = Path.Combine(directory, "windows.json");
        File.WriteAllText(destination, "old");
        var temp = Path.Combine(directory, "new.tmp");
        File.WriteAllText(temp, "new");

        var holder = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read);
        // let go while the replace is already retrying, the way a scanner finishes with the file
        var release = Task.Run(async () =>
        {
            await Task.Delay(300);
            holder.Dispose();
        });

        AtomicFile.Replace(temp, destination);
        release.Wait();

        Assert.Equal("new", File.ReadAllText(destination));
    }

    [Fact]
    public void Replace_moves_the_file_when_nothing_holds_it()
    {
        var directory = NewDirectory();
        var destination = Path.Combine(directory, "windows.json");
        var temp = Path.Combine(directory, "new.tmp");
        File.WriteAllText(temp, "new");

        AtomicFile.Replace(temp, destination);

        Assert.Equal("new", File.ReadAllText(destination));
        Assert.False(File.Exists(temp));
    }

    [Fact]
    public void Replace_gives_up_and_reports_the_lock_it_could_not_beat()
    {
        var directory = NewDirectory();
        var destination = Path.Combine(directory, "windows.json");
        File.WriteAllText(destination, "old");
        var temp = Path.Combine(directory, "new.tmp");
        File.WriteAllText(temp, "new");

        // never released: a caller that cannot save must still hear about it
        using var holder = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read);

        Assert.Throws<UnauthorizedAccessException>(
            () => AtomicFile.Replace(temp, destination, TimeSpan.FromMilliseconds(200)));
    }
}
