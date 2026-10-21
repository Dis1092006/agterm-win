using AgTerm.Core.Install;

namespace AgTerm.Core.Tests.Install;

public sealed class CommandPathTests
{
    [Theory]
    [InlineData(@"C:\app;C:\user", @"C:\app", @"C:\app;C:\user")]
    [InlineData(@"C:\user;c:\APP\;C:\other", @"C:\app", @"C:\app;C:\user;C:\other")]
    [InlineData(@"C:\user;;C:\USER\;C:\other;", @"C:\app", @"C:\app;C:\user;C:\other")]
    [InlineData(@"C:\user;C:\other", null, @"C:\user;C:\other")]
    [InlineData(null, @"C:\app", @"C:\app")]
    [InlineData("", null, "")]
    public void Widened_puts_bundled_cli_first_and_keeps_first_of_each_directory(
        string? inherited, string? bundled, string expected)
    {
        Assert.Equal(expected, CommandPath.Widened(inherited, bundled));
    }
}
