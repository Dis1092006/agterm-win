using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Ported;

/// <summary>Ported 1:1 from agtermCore/Tests/agtermCoreTests/ControlResolveTests.swift.</summary>
public class ControlResolveTests
{
    private static readonly Guid A = Guid.Parse("9F3CAAAA-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("9FABBBBB-0000-0000-0000-000000000002");
    private static readonly Guid C = Guid.Parse("1234CCCC-0000-0000-0000-000000000003");

    private static string UuidString(Guid id) => id.ToString("D").ToUpperInvariant();

    [Fact]
    public void ActiveResolvesToActiveID()
    {
        var result = ControlResolve.Resolve("active", new[] { A, B, C }, active: B);
        Assert.Equal(TargetResolution.Resolved(B), result);
    }

    [Fact]
    public void ActiveWithNilActiveIsNotFound()
    {
        var result = ControlResolve.Resolve("active", new[] { A, B, C }, active: null);
        Assert.Equal(TargetResolution.NotFound(), result);
    }

    [Fact]
    public void ExactUUIDResolves()
    {
        var result = ControlResolve.Resolve(UuidString(A), new[] { A, B, C }, active: null);
        Assert.Equal(TargetResolution.Resolved(A), result);
    }

    [Fact]
    public void ExactUUIDIsCaseInsensitive()
    {
        var result = ControlResolve.Resolve(UuidString(A).ToLowerInvariant(), new[] { A, B, C }, active: null);
        Assert.Equal(TargetResolution.Resolved(A), result);
    }

    [Fact]
    public void UniquePrefixResolves()
    {
        // "1234" is unique to c
        var result = ControlResolve.Resolve("1234", new[] { A, B, C }, active: null);
        Assert.Equal(TargetResolution.Resolved(C), result);
    }

    [Fact]
    public void AmbiguousPrefixListsHits()
    {
        // "9f" matches both a and b
        var result = ControlResolve.Resolve("9f", new[] { A, B, C }, active: null);
        Assert.Equal(TargetResolution.Ambiguous(new[] { A, B }), result);
    }

    [Fact]
    public void NoMatchIsNotFound()
    {
        var result = ControlResolve.Resolve("deadbeef", new[] { A, B, C }, active: null);
        Assert.Equal(TargetResolution.NotFound(), result);
    }

    [Fact]
    public void EmptyCandidatesIsNotFound()
    {
        var result = ControlResolve.Resolve("9f", Array.Empty<Guid>(), active: null);
        Assert.Equal(TargetResolution.NotFound(), result);
    }

    [Fact]
    public void EmptyTargetIsNotFound()
    {
        // an empty prefix would otherwise match every candidate — guard it to notFound.
        Assert.Equal(TargetResolution.NotFound(), ControlResolve.Resolve("", new[] { A, B, C }, active: A));
        Assert.Equal(TargetResolution.NotFound(), ControlResolve.Resolve("", new[] { A }, active: null));
    }

    [Fact]
    public void NotFoundMessageUsesControlWireString()
    {
        var message = ControlResolve.NotFoundMessage("session", "deadbeef");
        Assert.Equal("no such session: deadbeef", message);
    }

    [Fact]
    public void AmbiguousMessageUsesControlWireStringWithPrefix8List()
    {
        var message = ControlResolve.AmbiguousMessage("window", "9f", new[] { A, B });
        Assert.Equal("ambiguous window prefix '9f' → 9F3CAAAA, 9FABBBBB", message);
    }

    [Fact]
    public void ErrorMessageUsesAmbiguousWireString()
    {
        var message = ControlResolve.ErrorMessage("workspace", "9f", TargetResolution.Ambiguous(new[] { A, B }));
        Assert.Equal("ambiguous workspace prefix '9f' → 9F3CAAAA, 9FABBBBB", message);
    }

    [Fact]
    public void ErrorMessageMapsNonAmbiguousResultsToNotFoundWireString()
    {
        Assert.Equal("no such session: active",
            ControlResolve.ErrorMessage("session", "active", TargetResolution.NotFound()));
        Assert.Equal("no such session: active",
            ControlResolve.ErrorMessage("session", "active", TargetResolution.Resolved(A)));
    }

    // window-id targets reuse the same pure matcher semantics; these tests keep the raw matcher pinned.
    private static readonly Guid W1 = Guid.Parse("0A11AAAA-0000-0000-0000-000000000011");
    private static readonly Guid W2 = Guid.Parse("0A22BBBB-0000-0000-0000-000000000012");
    private static readonly Guid W3 = Guid.Parse("7B33CCCC-0000-0000-0000-000000000013");

    [Fact]
    public void WindowActiveResolvesToFrontmost()
    {
        var result = ControlResolve.Resolve("active", new[] { W1, W2, W3 }, active: W2);
        Assert.Equal(TargetResolution.Resolved(W2), result);
    }

    [Fact]
    public void WindowExactUUIDResolves()
    {
        var result = ControlResolve.Resolve(UuidString(W1), new[] { W1, W2, W3 }, active: null);
        Assert.Equal(TargetResolution.Resolved(W1), result);
    }

    [Fact]
    public void WindowUniquePrefixResolves()
    {
        // "7b33" is unique to w3
        var result = ControlResolve.Resolve("7b33", new[] { W1, W2, W3 }, active: null);
        Assert.Equal(TargetResolution.Resolved(W3), result);
    }

    [Fact]
    public void WindowAmbiguousPrefixListsHits()
    {
        // "0a" matches both w1 and w2
        var result = ControlResolve.Resolve("0a", new[] { W1, W2, W3 }, active: null);
        Assert.Equal(TargetResolution.Ambiguous(new[] { W1, W2 }), result);
    }

    [Fact]
    public void WindowNoMatchIsNotFound()
    {
        var result = ControlResolve.Resolve("deadbeef", new[] { W1, W2, W3 }, active: null);
        Assert.Equal(TargetResolution.NotFound(), result);
    }

    [Fact]
    public void SocketPathWithStateDir()
    {
        // upstream joins with "/"; the port joins with the platform separator (Path.Combine).
        var path = ControlResolve.SocketPath("/tmp/agterm-state", "/Users/x/Library/Application Support/agterm");
        Assert.Equal(Path.Combine("/tmp/agterm-state", "agterm.sock"), path);
    }

    [Fact]
    public void SocketPathWithoutStateDirUsesAppSupport()
    {
        var path = ControlResolve.SocketPath(null, "/Users/x/Library/Application Support/agterm");
        Assert.Equal(Path.Combine("/Users/x/Library/Application Support/agterm", "agterm.sock"), path);
    }
}
