namespace AgTerm.Terminal.Tests;

public class ShellCompletionWaitTests
{
    [Fact]
    public async Task Exit_does_not_complete_the_wait_before_output_is_drained()
    {
        var output = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = ShellCompletionWait.WaitAsync(
            Task.FromResult(7), output.Task, TimeSpan.FromSeconds(5), () => "output");
        try
        {
            Assert.False(pending.IsCompleted);
        }
        finally
        {
            output.SetResult();
        }
        Assert.Equal(7, await pending);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_real_timeout_includes_output_and_both_task_states(bool exited)
    {
        var exit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (exited)
            exit.SetResult(0);
        var output = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var error = await Assert.ThrowsAsync<TimeoutException>(() => ShellCompletionWait.WaitAsync(
            exit.Task, output.Task, TimeSpan.FromMilliseconds(100), () => "captured shell output"));

        Assert.Contains($"Exited={exit.Task.Status}", error.Message);
        Assert.Contains($"OutputCompleted={output.Task.Status}", error.Message);
        Assert.Contains("captured shell output", error.Message);
    }

    [Fact]
    public async Task Output_failure_is_not_reported_as_successful_exit()
    {
        var failure = new IOException("output reader failed");

        var actual = await Assert.ThrowsAsync<IOException>(() => ShellCompletionWait.WaitAsync(
            Task.FromResult(0), Task.FromException(failure), TimeSpan.FromSeconds(5), () => ""));

        Assert.Same(failure, actual);
    }

    [Fact]
    public async Task Exit_failure_is_preserved()
    {
        var failure = new IOException("exit watcher failed");

        var actual = await Assert.ThrowsAsync<IOException>(() => ShellCompletionWait.WaitAsync(
            Task.FromException<int>(failure), Task.CompletedTask, TimeSpan.FromSeconds(5), () => ""));

        Assert.Same(failure, actual);
    }
}
