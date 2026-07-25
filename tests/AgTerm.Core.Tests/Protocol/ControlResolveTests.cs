using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Protocol;

public class ControlResolveTests
{
    private static readonly Guid A = Guid.Parse("AAAA1111-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("AAAA2222-0000-0000-0000-000000000002");
    private static readonly Guid C = Guid.Parse("BBBB3333-0000-0000-0000-000000000003");
    private static readonly Guid[] Candidates = { A, B, C };

    [Fact]
    public void EmptyTargetIsNotFound()
    {
        // an empty prefix would otherwise match everything.
        Assert.Equal(TargetResolution.NotFound(), ControlResolve.Resolve("", Candidates, active: A));
    }

    [Fact]
    public void ActiveResolvesToTheActiveId()
    {
        Assert.Equal(TargetResolution.Resolved(B), ControlResolve.Resolve("active", Candidates, active: B));
    }

    [Fact]
    public void ActiveWithoutAnActiveIdIsNotFound()
    {
        Assert.Equal(TargetResolution.NotFound(), ControlResolve.Resolve("active", Candidates, active: null));
    }

    [Fact]
    public void ActiveIsCaseSensitive()
    {
        // "ACTIVE" is not the keyword; it falls through to prefix matching and misses.
        Assert.Equal(TargetResolution.NotFound(), ControlResolve.Resolve("ACTIVE", Candidates, active: A));
    }

    [Theory]
    [InlineData("AAAA1111-0000-0000-0000-000000000001")]
    [InlineData("aaaa1111-0000-0000-0000-000000000001")]
    [InlineData("AaAa1111-0000-0000-0000-000000000001")]
    public void ExactUuidMatchesCaseInsensitively(string target)
    {
        Assert.Equal(TargetResolution.Resolved(A), ControlResolve.Resolve(target, Candidates, active: null));
    }

    [Theory]
    [InlineData("bbbb")]
    [InlineData("BBBB")]
    [InlineData("bBb")]
    public void UniquePrefixResolves(string target)
    {
        Assert.Equal(TargetResolution.Resolved(C), ControlResolve.Resolve(target, Candidates, active: null));
    }

    [Fact]
    public void UnknownPrefixIsNotFound()
    {
        Assert.Equal(TargetResolution.NotFound(), ControlResolve.Resolve("cccc", Candidates, active: null));
    }

    [Fact]
    public void SharedPrefixIsAmbiguousAndListsHitsInCandidateOrder()
    {
        Assert.Equal(TargetResolution.Ambiguous(new[] { A, B }),
            ControlResolve.Resolve("aaaa", Candidates, active: null));
    }

    [Fact]
    public void NotFoundMessageMatchesUpstreamVerbatim()
    {
        Assert.Equal("no such session: zzz", ControlResolve.NotFoundMessage("session", "zzz"));
    }

    [Fact]
    public void AmbiguousMessageListsFirstEightUppercaseChars()
    {
        Assert.Equal("ambiguous session prefix 'aaaa' → AAAA1111, AAAA2222",
            ControlResolve.AmbiguousMessage("session", "aaaa", new[] { A, B }));
    }

    [Fact]
    public void ErrorMessageMapsAmbiguousToAmbiguousAndEverythingElseToNotFound()
    {
        Assert.Equal("ambiguous workspace prefix 'aa' → AAAA1111, AAAA2222",
            ControlResolve.ErrorMessage("workspace", "aa", TargetResolution.Ambiguous(new[] { A, B })));
        Assert.Equal("no such workspace: zz",
            ControlResolve.ErrorMessage("workspace", "zz", TargetResolution.NotFound()));
        // resolved maps to not-found: a caller that resolved an id but cannot find its owner keeps
        // the same wire contract as a normal miss.
        Assert.Equal("no such workspace: aa",
            ControlResolve.ErrorMessage("workspace", "aa", TargetResolution.Resolved(A)));
    }

    [Fact]
    public void SocketPathPrefersStateDirOverAppSupport()
    {
        Assert.Equal(Path.Combine(@"C:\state", "agterm.sock"),
            ControlResolve.SocketPath(stateDir: @"C:\state", appSupport: @"C:\appsupport"));
        Assert.Equal(Path.Combine(@"C:\appsupport", "agterm.sock"),
            ControlResolve.SocketPath(stateDir: null, appSupport: @"C:\appsupport"));
    }
}
