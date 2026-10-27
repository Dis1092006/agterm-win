using AgTerm.Core.Keymap;

namespace AgTerm.Core.Tests.Keymap;

public class WindowsVirtualKeyNamesTests
{
    [Theory]
    [InlineData(0x41, "a")]
    [InlineData(0x5A, "z")]
    [InlineData(0x30, "0")]
    [InlineData(0x39, "9")]
    [InlineData(0x09, "tab")]
    [InlineData(0x20, "space")]
    [InlineData(0x0D, "return")]
    [InlineData(0x08, "delete")]
    [InlineData(0x25, "left")]
    [InlineData(0x26, "up")]
    [InlineData(0x27, "right")]
    [InlineData(0x28, "down")]
    public void Letters_digits_and_named_keys_keep_their_names(int virtualKey, string name)
    {
        Assert.Equal(name, WindowsVirtualKeyNames.KeyName(virtualKey));
    }

    [Theory]
    [InlineData(0xBA, ";")]
    [InlineData(0xBB, "=")]
    [InlineData(0xBC, ",")]
    [InlineData(0xBD, "-")]
    [InlineData(0xBE, ".")]
    [InlineData(0xBF, "/")]
    [InlineData(0xC0, "`")]
    [InlineData(0xDB, "[")]
    [InlineData(0xDC, "\\")]
    [InlineData(0xDD, "]")]
    [InlineData(0xDE, "'")]
    public void Punctuation_keys_take_their_unshifted_us_name(int virtualKey, string name)
    {
        Assert.Equal(name, WindowsVirtualKeyNames.KeyName(virtualKey));
    }

    [Theory]
    [InlineData(0x6B, "+")]
    [InlineData(0x6D, "-")]
    public void The_numpad_plus_and_minus_are_named_by_their_character(int virtualKey, string name)
    {
        Assert.Equal(name, WindowsVirtualKeyNames.KeyName(virtualKey));
    }

    [Theory]
    [InlineData(0x10)]
    [InlineData(0x11)]
    [InlineData(0x12)]
    [InlineData(0x70)]
    public void Modifiers_and_unbindable_keys_have_no_name(int virtualKey)
    {
        Assert.Null(WindowsVirtualKeyNames.KeyName(virtualKey));
    }
}
