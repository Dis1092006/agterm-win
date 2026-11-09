using Xunit;

namespace AgTerm.Cli.Tests.TestSupport;

/// <summary>The classes that render `--help` swap the process-wide Console.Out, so they share one
/// collection and xUnit runs them one at a time.</summary>
[CollectionDefinition(Name)]
public sealed class ConsoleCollection
{
    public const string Name = "Console";
}
