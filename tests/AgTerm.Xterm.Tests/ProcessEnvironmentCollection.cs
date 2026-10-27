namespace AgTerm.Xterm.Tests;

/// <summary>
/// Tests that change the environment of the test process. A child spawned by a test in another
/// collection at the same time would inherit the change, so this collection runs alone.
/// </summary>
[CollectionDefinition("process-environment", DisableParallelization = true)]
[OneLiveTestPerMachine]
public class ProcessEnvironmentCollection : ICollectionFixture<WebView2Fixture>;
