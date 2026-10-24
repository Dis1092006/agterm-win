using System.Security.Cryptography;
using System.Text;

namespace AgTerm.App;

/// <summary>The AppInstance key of one state directory. Two processes on one directory overwrite each
/// other's saved state, so a second launch hands its activation to the first; a process on another
/// AGTERM_STATE_DIR has another key and runs beside it.</summary>
public static class SingleInstanceKey
{
    public static string For(string stateDirectory)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stateDirectory)).ToUpperInvariant();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(path));
        return "agterm-win-" + Convert.ToHexString(digest.AsSpan(0, 12)).ToLowerInvariant();
    }
}
