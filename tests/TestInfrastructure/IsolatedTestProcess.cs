using System.Collections.Concurrent;
using System.Diagnostics;

namespace AgTerm.TestInfrastructure;

internal static partial class IsolatedTestProcess
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1)
                throw new ArgumentException("Expected one isolated test scenario.");
            using var context = new TestSynchronizationContext();
            context.Run(() => RunScenarioAsync(args[0]));
            Console.WriteLine($"Passed: {args[0]}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static partial Task RunScenarioAsync(string scenario);

    internal static Task RunAsync(string scenario, TimeSpan budget) =>
        Task.Factory.StartNew(() =>
        {
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add(typeof(IsolatedTestProcess).Assembly.Location);
            start.ArgumentList.Add(scenario);
            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("Could not start isolated test process.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            var exited = process.WaitForExit((int)budget.TotalMilliseconds);
            if (!exited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
            var output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            Assert.True(exited, $"Isolated scenario {scenario} exceeded {budget}:\n{output}");
            Assert.True(process.ExitCode == 0,
                $"Isolated scenario {scenario} exited with {process.ExitCode}:\n{output}");
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    internal static void LimitWorkerPool()
    {
        ThreadPool.GetMaxThreads(out _, out var io);
        Assert.True(ThreadPool.SetMinThreads(1, 1));
        Assert.True(ThreadPool.SetMaxThreads(1, io));
    }

    internal static async Task WithBlockedWorkerAsync(Func<Task> scenario, TimeSpan budget)
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var blocker = Task.Run(() =>
        {
            started.Set();
            release.Wait();
        });
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)), "Pool blocker did not start.");
        var watchdog = new Thread(() =>
        {
            if (!release.Wait(budget))
                release.Set();
        }) { IsBackground = true, Name = "isolated-test-watchdog" };
        watchdog.Start();
        try
        {
            await scenario();
        }
        finally
        {
            release.Set();
            watchdog.Join();
            blocker.GetAwaiter().GetResult();
        }
    }

    private sealed class TestSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback callback, object? state) => _queue.Add((callback, state));

        internal void Run(Func<Task> scenario)
        {
            var previous = Current;
            SetSynchronizationContext(this);
            try
            {
                var completion = CompleteAsync();
                // Test continuations must not occupy the pool being constrained by the scenario.
                foreach (var work in _queue.GetConsumingEnumerable())
                    work.Callback(work.State);
                completion.GetAwaiter().GetResult();
            }
            finally
            {
                SetSynchronizationContext(previous);
            }

            async Task CompleteAsync()
            {
                try
                {
                    await scenario();
                }
                finally
                {
                    _queue.CompleteAdding();
                }
            }
        }

        public void Dispose() => _queue.Dispose();
    }
}
