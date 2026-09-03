using AgTerm.Core.Model;

namespace AgTerm.Xterm;

public sealed record DirectoryImportRequest(Guid WorkspaceId, string DirectoryPath);

public static class DirectoryImportPolicy
{
    public static IReadOnlyList<DirectoryImportRequest> CreateRequests(
        Guid? workspaceId, IEnumerable<string> droppedPaths)
    {
        ArgumentNullException.ThrowIfNull(droppedPaths);
        if (workspaceId is null) return Array.Empty<DirectoryImportRequest>();

        var requests = new List<DirectoryImportRequest>(SidebarDrop.MaximumDirectoryImportCount);
        foreach (var path in droppedPaths)
        {
            if (requests.Count == SidebarDrop.MaximumDirectoryImportCount) break;
            if (Directory.Exists(path))
                requests.Add(new DirectoryImportRequest(workspaceId.Value, path));
        }
        return requests.AsReadOnly();
    }
}