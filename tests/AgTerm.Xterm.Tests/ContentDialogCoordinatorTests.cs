using AgTerm.App;

namespace AgTerm.Xterm.Tests;

public sealed class ContentDialogCoordinatorTests
{
    [Fact]
    public async Task RunAsync_waits_for_the_active_dialog_before_starting_the_next()
    {
        using var coordinator = new ContentDialogCoordinator();
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = false;

        var first = coordinator.RunAsync(async () =>
        {
            firstStarted.SetResult();
            await releaseFirst.Task;
            return 1;
        });
        await firstStarted.Task;
        var second = coordinator.RunAsync(() =>
        {
            secondStarted = true;
            return Task.FromResult(2);
        });

        await Task.Yield();
        Assert.False(secondStarted);
        releaseFirst.SetResult();
        Assert.Equal(new[] { 1, 2 }, await Task.WhenAll(first, second));
    }

    [Fact]
    public async Task RunAsync_raises_DialogClosed_once_the_dialog_returns()
    {
        using var coordinator = new ContentDialogCoordinator();
        var closed = 0;
        coordinator.DialogClosed += () => closed++;

        var closedWhileShown = -1;
        await coordinator.RunAsync(() =>
        {
            closedWhileShown = closed;
            return Task.FromResult(true);
        });

        Assert.Equal(0, closedWhileShown);
        Assert.Equal(1, closed);
    }

    [Fact]
    public async Task RunAsync_raises_DialogClosed_when_the_dialog_run_faults()
    {
        using var coordinator = new ContentDialogCoordinator();
        var closed = 0;
        coordinator.DialogClosed += () => closed++;

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RunAsync<bool>(
            () => throw new InvalidOperationException("rename failed")));

        Assert.Equal(1, closed);
    }
}