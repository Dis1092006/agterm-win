using AgTerm.Core.Model;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

public sealed class DirectoryImportPolicyTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("agterm-drop-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void CreateRequests_filters_non_directories_preserves_order_and_caps_the_result()
    {
        var workspaceId = Guid.NewGuid();
        var directories = Enumerable.Range(0, SidebarDrop.MaximumDirectoryImportCount + 2)
            .Select(index => Directory.CreateDirectory(Path.Combine(_root, $"directory-{index:D2}")).FullName)
            .ToList();
        var file = Path.Combine(_root, "file.txt");
        File.WriteAllText(file, "not a directory");
        var droppedPaths = new List<string> { Path.Combine(_root, "missing"), directories[0], file };
        droppedPaths.AddRange(directories.Skip(1));

        var requests = DirectoryImportPolicy.CreateRequests(workspaceId, droppedPaths);

        Assert.Equal(SidebarDrop.MaximumDirectoryImportCount, requests.Count);
        Assert.Equal(directories.Take(SidebarDrop.MaximumDirectoryImportCount),
            requests.Select(request => request.DirectoryPath));
        Assert.All(requests, request => Assert.Equal(workspaceId, request.WorkspaceId));
    }

    [Fact]
    public void CreateRequests_without_a_workspace_returns_no_imports()
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, "directory")).FullName;

        var requests = DirectoryImportPolicy.CreateRequests(null, new[] { directory });

        Assert.Empty(requests);
    }

    [Fact]
    public void Sidebar_view_routes_Explorer_folders_as_copy_imports()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(),
            "src", "AgTerm.App", "SidebarView.xaml.cs"));

        Assert.Contains("StandardDataFormats.StorageItems", source, StringComparison.Ordinal);
        Assert.Contains("DataPackageOperation.Copy", source, StringComparison.Ordinal);
        Assert.Contains("GetStorageItemsAsync", source, StringComparison.Ordinal);
        Assert.Contains("OfType<StorageFolder>()", source, StringComparison.Ordinal);
        Assert.Contains("SidebarDrop.ResolveDirectoryWorkspace", source, StringComparison.Ordinal);
        Assert.Contains("DirectoryImportPolicy.CreateRequests", source, StringComparison.Ordinal);
        Assert.Contains("controller.ImportDirectories", source, StringComparison.Ordinal);
        Assert.Contains("DataPackageOperation.Move", source, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}