using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

public sealed class TerminalPageLifecycleTests
{
    [Fact]
    public async Task Failed_initialization_reports_full_exception_and_reloads_only_on_request()
    {
        var attempts = 0;
        var failures = new List<string>();
        var inner = new InvalidOperationException("inner detail");
        var lifecycle = new TerminalPageLifecycle<object>(
            () =>
            {
                attempts++;
                return attempts == 1
                    ? Task.FromException<object>(new ApplicationException("outer detail", inner))
                    : Task.FromResult<object>(new object());
            },
            TimeSpan.FromSeconds(1),
            exception => failures.Add(exception.ToString()));

        await Assert.ThrowsAsync<ApplicationException>(() => lifecycle.Current);
        Assert.Equal(1, attempts);
        Assert.Single(failures);
        Assert.Contains(nameof(ApplicationException), failures[0]);
        Assert.Contains("outer detail", failures[0]);
        Assert.Contains(nameof(InvalidOperationException), failures[0]);
        Assert.Contains("inner detail", failures[0]);

        var page = await lifecycle.ReloadAsync();
        Assert.Same(page, await lifecycle.Current);
        Assert.Equal(2, attempts);
        Assert.Single(failures);
    }

    [Fact]
    public async Task Page_that_never_becomes_ready_times_out_and_can_be_reloaded()
    {
        var attempts = 0;
        var failures = new List<Exception>();
        var neverReady = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lifecycle = new TerminalPageLifecycle<object>(
            () => ++attempts == 1 ? neverReady.Task : Task.FromResult<object>(new object()),
            TimeSpan.FromMilliseconds(30),
            failures.Add);

        await Assert.ThrowsAsync<TimeoutException>(() => lifecycle.Current);
        Assert.Equal(1, attempts);
        Assert.IsType<TimeoutException>(Assert.Single(failures));

        await lifecycle.ReloadAsync();
        neverReady.SetResult(new object());
        Assert.Equal(2, attempts);
        Assert.Single(failures);
    }

    [Fact]
    public async Task Reload_during_a_stalled_start_ignores_the_old_attempt()
    {
        var neverReady = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var failures = new List<Exception>();
        var lifecycle = new TerminalPageLifecycle<object>(
            () => ++attempts == 1 ? neverReady.Task : Task.FromResult<object>(new object()),
            TimeSpan.FromMilliseconds(30),
            failures.Add);
        var oldAttempt = lifecycle.Current;

        await lifecycle.ReloadAsync();
        await Assert.ThrowsAsync<TimeoutException>(() => oldAttempt);

        Assert.Equal(2, attempts);
        Assert.Empty(failures);
    }

    [Fact]
    public async Task Reload_can_replace_a_page_that_became_ready_before_its_renderer_died()
    {
        var attempts = 0;
        var lifecycle = new TerminalPageLifecycle<object>(
            () => Task.FromResult<object>(++attempts),
            TimeSpan.FromSeconds(1),
            _ => throw new Xunit.Sdk.XunitException("No page startup failure expected."));

        Assert.Equal(1, await lifecycle.Current);
        Assert.Equal(2, await lifecycle.ReloadAsync());
        Assert.Equal(2, await lifecycle.Current);
    }

    [Fact]
    public async Task Failed_reload_reports_its_own_error_and_allows_another_manual_attempt()
    {
        var attempts = 0;
        var failures = new List<Exception>();
        var lifecycle = new TerminalPageLifecycle<object>(
            () => ++attempts switch
            {
                1 => Task.FromException<object>(new InvalidOperationException("first")),
                2 => Task.FromException<object>(new InvalidOperationException("second")),
                _ => Task.FromResult<object>(new object()),
            },
            TimeSpan.FromSeconds(1),
            failures.Add);

        await Assert.ThrowsAsync<InvalidOperationException>(() => lifecycle.Current);
        await Assert.ThrowsAsync<InvalidOperationException>(() => lifecycle.ReloadAsync());
        Assert.Equal(2, attempts);
        Assert.Equal(new[] { "first", "second" }, failures.Select(failure => failure.Message));

        await lifecycle.ReloadAsync();
        Assert.Equal(3, attempts);
    }
}
