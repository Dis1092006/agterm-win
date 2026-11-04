using AgTerm.TestInfrastructure;

namespace AgTerm.Terminal.Tests
{
    public class ConstrainedPoolTests
    {
        [Fact]
        public Task Readline_status_does_not_time_out_after_the_shell_exits_with_a_blocked_pool() =>
            IsolatedTestProcess.RunAsync("readline-status", TimeSpan.FromSeconds(90));
    }
}

namespace AgTerm.TestInfrastructure
{
    internal static partial class IsolatedTestProcess
    {
        private static partial Task RunScenarioAsync(string scenario)
        {
            if (scenario == AgTerm.Terminal.Tests.PaneProcessHelper.Scenario)
                return AgTerm.Terminal.Tests.PaneProcessHelper.RunAsync();
            if (scenario != "readline-status")
                throw new ArgumentException($"Unknown isolated test scenario: {scenario}");
            LimitWorkerPool();
            return WithBlockedWorkerAsync(async () =>
            {
                foreach (var shell in AgTerm.Terminal.Tests.PowerShellReadlineIntegrationTests.Shells())
                {
                    using var test = new AgTerm.Terminal.Tests.PowerShellReadlineIntegrationTests();
                    await test.The_wrapper_passes_the_last_status_to_the_original_readline((string)shell[0]);
                }
            }, TimeSpan.FromSeconds(65));
        }
    }
}
