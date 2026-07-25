using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Protocol;

public class ControlEventRingTests
{
    private static readonly Guid Run = Guid.Parse("5D3C67F2-4E58-4E12-9DFB-0DDD64FEC792");

    private static ControlEventRing NewRing(int capacity = ControlEventRing.DefaultCapacity) =>
        new(capacity, Run, () => DateTimeOffset.FromUnixTimeMilliseconds(1_721_900_000_500));

    private static ControlEventDraft Draft(ControlEventKind kind = ControlEventKind.TreeChanged) =>
        new() { Kind = kind };

    [Fact]
    public void AppendAssignsMonotonicSequenceAndUnixTimestamp()
    {
        var ring = NewRing();
        var first = ring.Append(Draft());
        var second = ring.Append(Draft());

        Assert.Equal(1ul, first.Seq);
        Assert.Equal(2ul, second.Seq);
        Assert.Equal(1_721_900_000.5, first.Ts);
    }

    [Fact]
    public void BootstrapReadAnchorsAtTailWithNoHistory()
    {
        var ring = NewRing();
        ring.Append(Draft());
        ring.Append(Draft());

        var result = ring.Read(cursor: null);

        Assert.Null(result.Error);
        Assert.Equal(Run, result.Batch.Run);
        Assert.Equal(2ul, result.Batch.Next);
        Assert.Empty(result.Batch.Items);
    }

    [Fact]
    public void ReadAfterCursorReturnsNewerEventsAndAdvancesNext()
    {
        var ring = NewRing();
        ring.Append(Draft());
        ring.Append(Draft(ControlEventKind.Status));
        ring.Append(Draft(ControlEventKind.Notify));

        var result = ring.Read(new ControlEventCursor(Run, After: 1));

        Assert.Null(result.Error);
        Assert.Equal(new ulong[] { 2, 3 }, result.Batch.Items.Select(e => e.Seq));
        Assert.Equal(3ul, result.Batch.Next);
    }

    [Fact]
    public void LimitStopsThePageAtTheLastReturnedSeq()
    {
        var ring = NewRing();
        for (var i = 0; i < 5; i++) ring.Append(Draft());

        var result = ring.Read(new ControlEventCursor(Run, After: 0), limit: 2);

        Assert.Equal(new ulong[] { 1, 2 }, result.Batch.Items.Select(e => e.Seq));
        Assert.Equal(2ul, result.Batch.Next);
    }

    [Fact]
    public void KindFilterSkipsEntriesButNextCountsScannedOnes()
    {
        var ring = NewRing();
        ring.Append(Draft(ControlEventKind.Status));
        ring.Append(Draft(ControlEventKind.Notify));
        ring.Append(Draft(ControlEventKind.Status));

        var result = ring.Read(new ControlEventCursor(Run, After: 0),
            kinds: new HashSet<ControlEventKind> { ControlEventKind.Notify });

        Assert.Equal(new ulong[] { 2 }, result.Batch.Items.Select(e => e.Seq));
        // next scans through the filtered-out third event too.
        Assert.Equal(3ul, result.Batch.Next);
    }

    [Fact]
    public void ForeignRunFailsWithRunChangedAndCarriesTheAnchor()
    {
        var ring = NewRing();
        ring.Append(Draft());

        var result = ring.Read(new ControlEventCursor(Guid.NewGuid(), After: 0));

        Assert.Equal(ControlEventReadError.RunChanged, result.Error);
        Assert.Equal(Run, result.Batch.Run);
        Assert.Equal(1ul, result.Batch.Next);
        Assert.Empty(result.Batch.Items);
    }

    [Fact]
    public void CursorAheadOfCurrentSequenceFails()
    {
        var ring = NewRing();
        ring.Append(Draft());

        var result = ring.Read(new ControlEventCursor(Run, After: 5));

        Assert.Equal(ControlEventReadError.CursorAhead, result.Error);
    }

    [Fact]
    public void OverwrittenCursorPositionFailsWithCursorExpired()
    {
        var ring = NewRing(capacity: 2);
        for (var i = 0; i < 4; i++) ring.Append(Draft()); // retained: seq 3, 4

        var expired = ring.Read(new ControlEventCursor(Run, After: 1));
        Assert.Equal(ControlEventReadError.CursorExpired, expired.Error);

        // after = oldest - 1 is still exactly readable: nothing was missed.
        var boundary = ring.Read(new ControlEventCursor(Run, After: 2));
        Assert.Null(boundary.Error);
        Assert.Equal(new ulong[] { 3, 4 }, boundary.Batch.Items.Select(e => e.Seq));
    }

    [Fact]
    public void CapacityEvictsOldestEntries()
    {
        var ring = NewRing(capacity: 3);
        for (var i = 0; i < 5; i++) ring.Append(Draft());

        var result = ring.Read(new ControlEventCursor(Run, After: 2));

        Assert.Equal(new ulong[] { 3, 4, 5 }, result.Batch.Items.Select(e => e.Seq));
    }

    [Fact]
    public void DraftFieldsArePreservedOnTheAppendedEvent()
    {
        var ring = NewRing();
        var draft = new ControlEventDraft
        {
            Kind = ControlEventKind.Status,
            Window = "w", Workspace = "ws", Session = "s",
            Payload = new ControlEventPayload { Status = "blocked", Blink = true },
        };

        var evt = ring.Append(draft);

        Assert.Equal("w", evt.Window);
        Assert.Equal("ws", evt.Workspace);
        Assert.Equal("s", evt.Session);
        Assert.Equal("blocked", evt.Payload.Status);
    }
}
