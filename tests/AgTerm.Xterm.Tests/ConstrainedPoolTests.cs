using AgTerm.TestInfrastructure;

namespace AgTerm.Xterm.Tests
{
    public class ConstrainedPoolTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public Task Activation_releases_its_holder_with_one_pool_worker(bool layoutChanges) =>
            IsolatedTestProcess.RunAsync($"activation-{layoutChanges}", TimeSpan.FromSeconds(30));
    }
}

namespace AgTerm.TestInfrastructure
{
    internal static partial class IsolatedTestProcess
    {
        private static partial async Task RunScenarioAsync(string scenario)
        {
            var layoutChanges = scenario switch
            {
                "activation-False" => false,
                "activation-True" => true,
                _ => throw new ArgumentException($"Unknown isolated test scenario: {scenario}"),
            };
            LimitWorkerPool();
            using var test = new AgTerm.Xterm.Tests.LiveWindowControlE2ETests();
            await test.Activation_waits_off_ui_and_ignores_a_replaced_accepted_layout(layoutChanges);
        }
    }
}
