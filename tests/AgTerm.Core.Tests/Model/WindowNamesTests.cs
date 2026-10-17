using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

/// <summary>WindowNames.IsAutoName — the port of agtermCore WindowLibrary.isAutoName: the literal
/// word "window" and a positive integer is an auto-assigned name; anything else is the user's.</summary>
public class WindowNamesTests
{
    [Theory]
    [InlineData("window 1")]
    [InlineData("window 12")]
    [InlineData("window  3")]
    [InlineData(" window 4 ")]
    public void Window_and_a_positive_number_is_an_auto_name(string name)
    {
        Assert.True(WindowNames.IsAutoName(name));
    }

    [Theory]
    [InlineData("window 0")]
    [InlineData("window -1")]
    [InlineData("window x")]
    [InlineData("Window 1")]
    [InlineData("window 1 dev")]
    [InlineData("window")]
    [InlineData("dev")]
    [InlineData("")]
    public void Anything_else_is_a_custom_name(string name)
    {
        Assert.False(WindowNames.IsAutoName(name));
    }
}
