namespace AgTerm.Core.Install;

/// <summary><see cref="ISkillInstallFileSystem"/> over System.IO. Link checks use the attributes of the
/// entry itself, which Windows reports without following the last link, so a dangling symlink or
/// junction counts as present.</summary>
public sealed class PhysicalSkillFileSystem : ISkillInstallFileSystem
{
    public bool Exists(string path)
    {
        if (!TryGetAttributes(path, out var attributes))
            return false;
        if (!attributes.HasFlag(FileAttributes.ReparsePoint))
            return true;
        FileSystemInfo link = attributes.HasFlag(FileAttributes.Directory) ? new DirectoryInfo(path) : new FileInfo(path);
        try
        {
            // null: a reparse point that is not a link (for example a cloud placeholder) is the entry itself
            var final = link.ResolveLinkTarget(returnFinalTarget: true);
            return final is null || final.Exists;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public bool EntryExists(string path) => TryGetAttributes(path, out _);

    public string? ReadText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void Remove(string path)
    {
        if (!TryGetAttributes(path, out var attributes))
            return;
        var isDirectory = attributes.HasFlag(FileAttributes.Directory);
        if (attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            // a non-recursive delete removes the link and leaves its target alone
            if (isDirectory)
                Directory.Delete(path);
            else
                File.Delete(path);
        }
        else if (isDirectory)
        {
            Directory.Delete(path, recursive: true);
        }
        else
        {
            File.Delete(path);
        }
    }

    public void WriteFile(string path, byte[] contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, contents);
    }

    /// <summary>The attributes of the entry itself. Errors other than "not found" count as present, so an
    /// entry the installer cannot inspect is never treated as absent and overwritten.</summary>
    private static bool TryGetAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            attributes = default;
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            attributes = FileAttributes.Normal;
            return true;
        }
    }
}
