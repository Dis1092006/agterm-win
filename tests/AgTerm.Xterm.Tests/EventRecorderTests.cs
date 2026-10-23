namespace AgTerm.Xterm.Tests;

public sealed class EventRecorderTests
{
    [Fact]
    public async Task An_enumerator_keeps_its_snapshot_when_another_thread_records_an_event()
    {
        var events = new EventRecorder<string>();
        events.Add("first");
        using var snapshot = events.GetEnumerator();
        Assert.True(snapshot.MoveNext());

        await Task.Factory.StartNew(() => events.Add("second"),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        Assert.Equal("first", snapshot.Current);
        Assert.False(snapshot.MoveNext());
        Assert.Equal(["first", "second"], events);
    }

    [Fact]
    public void Clearing_the_recorder_does_not_change_an_existing_enumerator()
    {
        var events = new EventRecorder<string>();
        events.Add("first");
        using var snapshot = events.GetEnumerator();

        events.Clear();

        Assert.True(snapshot.MoveNext());
        Assert.Equal("first", snapshot.Current);
        Assert.False(snapshot.MoveNext());
        Assert.Empty(events);
    }
}
