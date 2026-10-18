using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

public sealed class ScrollbackStorageTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("agterm-scrollback-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void Path_is_per_session_and_per_pane_under_the_scrollback_dir()
    {
        var id = Guid.Parse("0a1b2c3d-0000-4000-8000-00000000abcd");
        Assert.Equal(
            Path.Combine(_dir, "scrollback", "0A1B2C3D-0000-4000-8000-00000000ABCD.left.vt"),
            ScrollbackStorage.FilePath(id, StatusPane.Left, _dir));
        Assert.Equal(
            Path.Combine(_dir, "scrollback", "0A1B2C3D-0000-4000-8000-00000000ABCD.right.vt"),
            ScrollbackStorage.FilePath(id, StatusPane.Right, _dir));
    }

    [Fact]
    public void Take_returns_the_written_text_once()
    {
        var id = Guid.NewGuid();
        ScrollbackStorage.Write(id, StatusPane.Left, "PS C:\\> dir\r\n\x1b[32mok\x1b[0m", _dir);

        Assert.Equal("PS C:\\> dir\r\n\x1b[32mok\x1b[0m", ScrollbackStorage.Take(id, StatusPane.Left, _dir));
        Assert.Null(ScrollbackStorage.Take(id, StatusPane.Left, _dir));
        Assert.False(File.Exists(ScrollbackStorage.FilePath(id, StatusPane.Left, _dir)));
    }

    [Fact]
    public void Take_of_a_missing_file_is_null()
    {
        Assert.Null(ScrollbackStorage.Take(Guid.NewGuid(), StatusPane.Right, _dir));
    }

    [Fact]
    public void Writing_empty_text_drops_a_stale_file()
    {
        var id = Guid.NewGuid();
        ScrollbackStorage.Write(id, StatusPane.Left, "old", _dir);
        ScrollbackStorage.Write(id, StatusPane.Left, "", _dir);
        Assert.Null(ScrollbackStorage.Take(id, StatusPane.Left, _dir));
    }

    [Fact]
    public void Write_replaces_the_previous_content_and_leaves_no_temp_file()
    {
        var id = Guid.NewGuid();
        ScrollbackStorage.Write(id, StatusPane.Left, "first", _dir);
        ScrollbackStorage.Write(id, StatusPane.Left, "second", _dir);

        Assert.Single(Directory.GetFiles(ScrollbackStorage.DirectoryPath(_dir)));
        Assert.Equal("second", ScrollbackStorage.Take(id, StatusPane.Left, _dir));
    }

    [Fact]
    public void Remove_deletes_both_panes_of_one_session_only()
    {
        var gone = Guid.NewGuid();
        var kept = Guid.NewGuid();
        ScrollbackStorage.Write(gone, StatusPane.Left, "a", _dir);
        ScrollbackStorage.Write(gone, StatusPane.Right, "b", _dir);
        ScrollbackStorage.Write(kept, StatusPane.Left, "c", _dir);

        ScrollbackStorage.Remove(gone, _dir);
        ScrollbackStorage.Remove(Guid.NewGuid(), _dir);

        Assert.Null(ScrollbackStorage.Take(gone, StatusPane.Left, _dir));
        Assert.Null(ScrollbackStorage.Take(gone, StatusPane.Right, _dir));
        Assert.Equal("c", ScrollbackStorage.Take(kept, StatusPane.Left, _dir));
    }
}
