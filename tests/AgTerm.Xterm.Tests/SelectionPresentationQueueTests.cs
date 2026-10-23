namespace AgTerm.Xterm.Tests;

public sealed class SelectionPresentationQueueTests
{
    [Fact]
    public async Task Wait_includes_the_expected_snapshot_even_when_delivery_has_not_started()
    {
        var presented = new List<bool>();
        var queue = new SelectionPresentationQueue((_, current, _) =>
        {
            presented.Add(current.SearchActive);
            return Task.CompletedTask;
        });

        var wait = queue.WaitForAsync(snapshot => snapshot.SearchActive);

        Assert.False(wait.IsCompleted);
        queue.Record(new LiveSelectionSnapshot(null, null));
        Assert.False(wait.IsCompleted);
        queue.Record(new LiveSelectionSnapshot(null, null, SearchActive: true));
        await wait;
        Assert.Equal(new[] { false, true }, presented);
    }

    [Fact]
    public async Task Wait_includes_the_matching_snapshots_presentation_not_only_its_delivery()
    {
        var releasePresentation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var queue = new SelectionPresentationQueue((_, current, _) =>
            current.SearchActive ? releasePresentation.Task : Task.CompletedTask);
        queue.Record(new LiveSelectionSnapshot(null, null));

        var wait = queue.WaitForAsync(snapshot => snapshot.SearchActive);
        queue.Record(new LiveSelectionSnapshot(null, null, SearchActive: true));

        try
        {
            Assert.False(wait.IsCompleted);
        }
        finally
        {
            releasePresentation.SetResult();
            await queue.FlushAsync();
        }
        await wait;
    }

    [Fact]
    public async Task A_matching_presentation_failure_is_not_hidden_by_the_wait()
    {
        var failure = new InvalidOperationException("presentation failed");
        var queue = new SelectionPresentationQueue((_, _, _) => Task.FromException(failure));

        var wait = queue.WaitForAsync(snapshot => snapshot.SearchActive);
        queue.Record(new LiveSelectionSnapshot(null, null, SearchActive: true));

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => wait));
    }
}
