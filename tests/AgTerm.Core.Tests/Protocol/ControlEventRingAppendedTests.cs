using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Protocol;

public sealed class ControlEventRingAppendedTests
{
    [Fact]
    public void AppendedFiresWithTheStampedEventAfterTheRingHoldsIt()
    {
        var ring = new ControlEventRing();
        ControlEvent? seen = null;
        var pageSize = -1;
        ring.Appended += e =>
        {
            seen = e;
            pageSize = ring.Read(new ControlEventCursor(ring.Read(null).Batch.Run, 0)).Batch.Items.Length;
        };

        var appended = ring.Append(new ControlEventDraft { Kind = ControlEventKind.AppLaunched });

        Assert.Same(appended, seen);
        Assert.Equal(1, pageSize);
    }

    [Fact]
    public void AppendedIsNotRaisedForReads()
    {
        var ring = new ControlEventRing();
        var count = 0;
        ring.Appended += _ => count++;
        ring.Read(null);
        Assert.Equal(0, count);
    }
}
