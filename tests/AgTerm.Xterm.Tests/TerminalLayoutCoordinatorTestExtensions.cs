namespace AgTerm.Xterm.Tests;

internal static class TerminalLayoutCoordinatorTestExtensions
{
    // xUnit runs tests on its own worker threads, one per CPU, and posts async continuations back
    // to them. The coordinator starts its loop with Task.Yield, which posts to the caller's context.
    // On a 4-core CI runner, live tests hold those threads and the loop waits behind them until the
    // test times out. Submitting from the thread pool keeps the loop off xUnit's threads.
    public static Task SubmitFromThreadPoolAsync(
        this TerminalLayoutCoordinator coordinator,
        TerminalLayout layout) =>
        Task.Run(() => coordinator.Submit(layout));
}
