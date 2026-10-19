using AgTerm.Core.Install;

namespace AgTerm.Core.Tests.Install;

public sealed class UserPathTests
{
    [Theory]
    [InlineData(@"C:\a;C:\Tools", @"C:\Tools")]
    [InlineData(@"C:\a;C:\Tools", @"c:\tools")]
    [InlineData(@"C:\a;C:\Tools\", @"C:\Tools")]
    [InlineData(@"C:\a;C:\Tools", @"C:\Tools\")]
    [InlineData(@"C:\a;C:\Tools/", @"C:\Tools")]
    [InlineData(@"C:\Tools;;C:\b;", @"C:\Tools")]
    public void ContainsMatchesCaseInsensitiveAndIgnoresTrailingSeparators(string value, string directory)
    {
        Assert.True(UserPath.Contains(value, directory));
    }

    [Theory]
    [InlineData(@"C:\a;C:\Toolsx", @"C:\Tools")]
    [InlineData(@"C:\a;C:\Tools\bin", @"C:\Tools")]
    [InlineData("", @"C:\Tools")]
    [InlineData(null, @"C:\Tools")]
    [InlineData(";;", @"C:\Tools")]
    public void ContainsIsFalseForOtherEntries(string? value, string directory)
    {
        Assert.False(UserPath.Contains(value, directory));
    }

    [Fact]
    public void ContainsDoesNotExpandVariables()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.False(UserPath.Contains(@"%USERPROFILE%\bin", Path.Join(profile, "bin")));
        Assert.True(UserPath.Contains(@"%USERPROFILE%\bin", @"%userprofile%\BIN\"));
    }

    private static string Expand(string value) =>
        value.Replace("%LOCALAPPDATA%", @"C:\Users\me\AppData\Local", StringComparison.OrdinalIgnoreCase);

    [Theory]
    [InlineData(@"C:\a;%LOCALAPPDATA%\agterm-win", @"C:\Users\me\AppData\Local\agterm-win")]
    [InlineData(@"%localappdata%\AGTERM-WIN\;C:\a", @"c:\users\me\appdata\local\agterm-win")]
    [InlineData(@"C:\Users\me\AppData\Local\agterm-win", @"%LOCALAPPDATA%\agterm-win")]
    public void ContainsComparesExpandedEntriesWhenGivenAnExpander(string value, string directory)
    {
        Assert.True(UserPath.Contains(value, directory, Expand));
    }

    [Fact]
    public void AppendAndRemoveKeepTheRawValueWhenTheyCompareExpanded()
    {
        const string value = @"%LOCALAPPDATA%\agterm-win;C:\b";
        const string directory = @"C:\Users\me\AppData\Local\agterm-win";
        Assert.Same(value, UserPath.Append(value, directory, Expand));
        Assert.Equal(@"%LOCALAPPDATA%\agterm-win;C:\b;C:\Tools", UserPath.Append(value, @"C:\Tools", Expand));
        Assert.Equal(@"C:\b", UserPath.Remove(value, directory, Expand));
    }

    [Theory]
    [InlineData("", @"C:\Tools", @"C:\Tools")]
    [InlineData(null, @"C:\Tools", @"C:\Tools")]
    [InlineData(@"C:\a", @"C:\Tools", @"C:\a;C:\Tools")]
    [InlineData(@"C:\a;", @"C:\Tools", @"C:\a;C:\Tools")]
    [InlineData(@"%USERPROFILE%\bin;;C:\b", @"C:\Tools", @"%USERPROFILE%\bin;;C:\b;C:\Tools")]
    public void AppendAddsOneEntryAndKeepsTheRestVerbatim(string? value, string directory, string expected)
    {
        Assert.Equal(expected, UserPath.Append(value, directory));
    }

    [Theory]
    [InlineData(@"C:\a;C:\Tools")]
    [InlineData(@"C:\a;c:\tools\;C:\b")]
    public void AppendDoesNotDuplicate(string value)
    {
        Assert.Same(value, UserPath.Append(value, @"C:\Tools"));
    }

    [Theory]
    [InlineData(@"C:\a;C:\Tools\;C:\b", @"c:\tools", @"C:\a;C:\b")]
    [InlineData(@"C:\Tools;C:\a;C:\TOOLS", @"C:\Tools", @"C:\a")]
    [InlineData(@"C:\Tools", @"C:\Tools", "")]
    [InlineData(@"C:\Tools;", @"C:\Tools", "")]
    [InlineData(@"C:\a;C:\Tools;", @"C:\Tools", @"C:\a;")]
    [InlineData(@"%X%;;C:\Tools;C:\b", @"C:\Tools", @"%X%;;C:\b")]
    public void RemoveDropsEveryMatchingEntry(string value, string directory, string expected)
    {
        Assert.Equal(expected, UserPath.Remove(value, directory));
    }

    [Fact]
    public void RemoveOfAnAbsentEntryKeepsTheValue()
    {
        const string value = @"C:\a;;C:\b;";
        Assert.Same(value, UserPath.Remove(value, @"C:\Tools"));
    }
}
