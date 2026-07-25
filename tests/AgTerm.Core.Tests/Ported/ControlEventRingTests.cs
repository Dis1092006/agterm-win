using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.TestSupport;

namespace AgTerm.Core.Tests.Ported;

/// <summary>Ported 1:1 from agtermCore/Tests/agtermCoreTests/ControlEventRingTests.swift.
/// Batch equality is asserted via canonical JSON (C# arrays have no structural record equality).</summary>
public class ControlEventRingTests
{
    private static readonly Guid RunId = Guid.Parse("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE");
    private static readonly DateTimeOffset FixedDate = DateTimeOffset.FromUnixTimeMilliseconds(1_783_969_641_380);
    private static double FixedTs => 1_783_969_641.38;

    private static ControlEventRing Ring(int capacity = 4_096) => new(capacity, RunId, () => FixedDate);

    private static ControlEventDraft Draft(ControlEventKind kind, string name = "api-fix") => new()
    {
        Kind = kind,
        Window = "window",
        Workspace = "workspace",
        Session = "session",
        Payload = new ControlEventPayload { Name = name },
    };

    private static ControlEvent Event(ulong seq, ControlEventDraft draft) => new()
    {
        Seq = seq, Ts = FixedTs, Kind = draft.Kind, Window = draft.Window,
        Workspace = draft.Workspace, Session = draft.Session, Payload = draft.Payload,
    };

    private static ControlEventBatch Batch(ulong next, params ControlEvent[] items) =>
        new() { Run = RunId, Next = next, Items = items };

    [Fact]
    public void AppendAssignsMonotonicSequenceAndTimestamp()
    {
        var subject = Ring();

        var first = subject.Append(Draft(ControlEventKind.Status));
        var second = subject.Append(Draft(ControlEventKind.Notify));

        Assert.Equal(1ul, first.Seq);
        Assert.Equal(2ul, second.Seq);
        Assert.Equal(FixedTs, first.Ts);
        Assert.Equal(ControlEventKind.Status, first.Kind);
        Assert.Equal("window", first.Window);
        Assert.Equal("api-fix", first.Payload.Name);
    }

    [Fact]
    public void BootstrapAnchorsAtTailWithoutReplayingHistory()
    {
        var subject = Ring();
        subject.Append(Draft(ControlEventKind.Status));
        subject.Append(Draft(ControlEventKind.Notify));

        var result = subject.Read(cursor: null);

        Assert.Null(result.Error);
        Assert.Equal(CanonicalJson.Of(Batch(next: 2)), CanonicalJson.Of(result.Batch));
    }

    [Fact]
    public void CapacityEvictsOldestAndKeepsExactBoundaryReadable()
    {
        var subject = Ring(capacity: 2);
        subject.Append(Draft(ControlEventKind.Status, "one"));
        subject.Append(Draft(ControlEventKind.Notify, "two"));
        subject.Append(Draft(ControlEventKind.SessionCreated, "three"));

        var boundary = subject.Read(new ControlEventCursor(RunId, After: 1));
        var expired = subject.Read(new ControlEventCursor(RunId, After: 0));

        Assert.Null(boundary.Error);
        Assert.Equal(
            CanonicalJson.Of(Batch(3,
                Event(2, Draft(ControlEventKind.Notify, "two")),
                Event(3, Draft(ControlEventKind.SessionCreated, "three")))),
            CanonicalJson.Of(boundary.Batch));
        Assert.Equal(ControlEventReadError.CursorExpired, expired.Error);
        Assert.Equal(CanonicalJson.Of(Batch(next: 3)), CanonicalJson.Of(expired.Batch));
    }

    [Fact]
    public void ReadsAreNonDestructiveAndIndependent()
    {
        var subject = Ring();
        subject.Append(Draft(ControlEventKind.Status));

        var cursor = new ControlEventCursor(RunId, After: 0);
        var first = subject.Read(cursor);
        var second = subject.Read(cursor);

        Assert.Equal(CanonicalJson.Of(first.Batch), CanonicalJson.Of(second.Batch));
        Assert.Equal(first.Error, second.Error);
        Assert.Equal(new ulong[] { 1 }, first.Batch.Items.Select(e => e.Seq));
    }

    [Fact]
    public void FilterAdvancesAcrossNonmatchingEvents()
    {
        var subject = Ring();
        subject.Append(Draft(ControlEventKind.Notify));
        subject.Append(Draft(ControlEventKind.Status));
        subject.Append(Draft(ControlEventKind.SessionClosed));

        var result = subject.Read(new ControlEventCursor(RunId, After: 0),
            kinds: new HashSet<ControlEventKind> { ControlEventKind.Status });

        Assert.Equal(new[] { ControlEventKind.Status }, result.Batch.Items.Select(e => e.Kind));
        Assert.Equal(3ul, result.Batch.Next);
    }

    [Fact]
    public void EmptyFilteredBatchAdvancesToTail()
    {
        var subject = Ring();
        subject.Append(Draft(ControlEventKind.Notify));
        subject.Append(Draft(ControlEventKind.SessionClosed));

        var result = subject.Read(new ControlEventCursor(RunId, After: 0),
            kinds: new HashSet<ControlEventKind> { ControlEventKind.Status });

        Assert.Null(result.Error);
        Assert.Equal(CanonicalJson.Of(Batch(next: 2)), CanonicalJson.Of(result.Batch));
    }

    [Fact]
    public void LimitStopsAtLastReturnedMatchWithoutDroppingLaterMatches()
    {
        var subject = Ring();
        subject.Append(Draft(ControlEventKind.Notify, "skip"));
        subject.Append(Draft(ControlEventKind.Status, "first"));
        subject.Append(Draft(ControlEventKind.Notify, "skip-two"));
        subject.Append(Draft(ControlEventKind.Status, "second"));

        var statusOnly = new HashSet<ControlEventKind> { ControlEventKind.Status };
        var first = subject.Read(new ControlEventCursor(RunId, After: 0), statusOnly, limit: 1);
        var second = subject.Read(new ControlEventCursor(RunId, first.Batch.Next), statusOnly, limit: 1);

        Assert.Equal(2ul, first.Batch.Next);
        Assert.Equal(new[] { "first" }, first.Batch.Items.Select(e => e.Payload.Name));
        Assert.Equal(4ul, second.Batch.Next);
        Assert.Equal(new[] { "second" }, second.Batch.Items.Select(e => e.Payload.Name));
    }

    [Fact]
    public void RunMismatchReturnsCurrentAnchor()
    {
        var subject = Ring();
        subject.Append(Draft(ControlEventKind.Status));
        var otherRun = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var result = subject.Read(new ControlEventCursor(otherRun, After: 0));

        Assert.Equal(ControlEventReadError.RunChanged, result.Error);
        Assert.Equal(CanonicalJson.Of(Batch(next: 1)), CanonicalJson.Of(result.Batch));
    }

    [Fact]
    public void CursorAheadReturnsCurrentAnchor()
    {
        var subject = Ring();
        subject.Append(Draft(ControlEventKind.Status));

        var result = subject.Read(new ControlEventCursor(RunId, After: 2));

        Assert.Equal(ControlEventReadError.CursorAhead, result.Error);
        Assert.Equal(CanonicalJson.Of(Batch(next: 1)), CanonicalJson.Of(result.Batch));
    }
}
