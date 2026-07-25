namespace AgTerm.Core.Tests.TestSupport;

/// <summary>Locates repo files (e.g. parity/surface.json) from the test bin directory.</summary>
public static class Repo
{
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !System.IO.File.Exists(Path.Combine(dir.FullName, "AgTerm.slnx")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("repo root with AgTerm.slnx not found");
        }
    }

    public static string File(string relativePath) => Path.Combine(Root, relativePath);
}
