using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

/// <summary>
/// The default state dir is ~\.agterm-win: a per-application directory, so a real agterm install
/// never shares state files or a derived pipe name with this port. The home root follows the CLI
/// convention the app is driven by (agtermctl), and the name is distinct from upstream's "agterm".
/// </summary>
public class RealUserDirectoryTests
{
    [Fact]
    public void RealUserDirectoryIsDotAgtermWinUnderTheHomeDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(Path.Combine(home, ".agterm-win"), PersistenceStore.RealUserDirectory);
    }

    [Fact]
    public void RealUserDirectoryDoesNotCollideWithUpstreamAgterm()
    {
        Assert.DoesNotContain(Path.DirectorySeparatorChar + "agterm" + Path.DirectorySeparatorChar,
            PersistenceStore.RealUserDirectory + Path.DirectorySeparatorChar);
    }
}
