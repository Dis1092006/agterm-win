using AgTerm.Core.Model;
using Xunit;

namespace AgTerm.Core.Tests.Model;

public class TerminalTextTests
{
    [Theory]
    [InlineData("a\r\nb", "ab")]
    [InlineData("a\u001B[31mb", "a[31mb")]
    [InlineData("tab\there", "tabhere")]
    [InlineData("del\u007Fx", "delx")]
    [InlineData("café 🚀 ~/项目", "café 🚀 ~/项目")]
    [InlineData("", "")]
    public void Sanitized_strips_C0_and_DEL_only(string input, string expected)
    {
        Assert.Equal(expected, TerminalText.Sanitized(input));
    }
}
