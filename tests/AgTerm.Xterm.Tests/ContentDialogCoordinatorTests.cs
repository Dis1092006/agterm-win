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
}