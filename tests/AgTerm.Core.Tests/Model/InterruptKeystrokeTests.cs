using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

public class InterruptKeystrokeTests
{
    [Theory]
    [InlineData(KeyModifiers.None)]
    [InlineData(KeyModifiers.Control)]
    [InlineData(KeyModifiers.Command)]
    public void EscapeInterruptsUnderAnyModifiers(KeyModifiers modifiers)
    {
        Assert.True(InterruptKeystroke.IsInterrupt("Escape", "Escape", modifiers));
    }

    [Theory]
    [InlineData("KeyC", "c")]
    [InlineData("KeyJ", "c")]
    [InlineData("KeyC", "\u0441")]
    [InlineData("KeyC", null)]
    public void BareControlCInterrupts(string code, string? character)
    {
        Assert.True(InterruptKeystroke.IsInterrupt(code, character, KeyModifiers.Control));
    }

    [Fact]
    public void DvorakControlJDoesNotInterrupt()
    {
        Assert.False(InterruptKeystroke.IsInterrupt("KeyC", "j", KeyModifiers.Control));
    }

    [Theory]
    [InlineData("KeyC", "c", KeyModifiers.None)]
    [InlineData("KeyA", "a", KeyModifiers.None)]
    [InlineData("KeyD", "d", KeyModifiers.Control)]
    [InlineData("KeyC", "c", KeyModifiers.Command)]
    [InlineData("KeyC", "c", KeyModifiers.Option)]
    [InlineData("KeyC", "c", KeyModifiers.Control | KeyModifiers.Command)]
    [InlineData("KeyC", "C", KeyModifiers.Control | KeyModifiers.Shift)]
    public void NonInterruptKeystrokesDoNotClear(string code, string character, KeyModifiers modifiers)
    {
        Assert.False(InterruptKeystroke.IsInterrupt(code, character, modifiers));
    }

    [Fact]
    public void KeyModifiersCombine()
    {
        var modifiers = KeyModifiers.Control | KeyModifiers.Shift;
        Assert.True(modifiers.HasFlag(KeyModifiers.Control));
        Assert.True(modifiers.HasFlag(KeyModifiers.Shift));
        Assert.False(modifiers.HasFlag(KeyModifiers.Command));
        Assert.False(modifiers.HasFlag(KeyModifiers.Option));
    }
}
