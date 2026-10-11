using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

// Ported from agterm AskDialogViewTests.swift buttonLabel cases (upstream ed831ab).
public class AskLabelTests
{
    [Fact]
    public void APlainLabelIsOneRun() =>
        Assert.Equal(new[] { new AskLabelRun("Yes", false) }, AskLabel.Runs("Yes", null, destructivePrefix: false));

    [Fact]
    public void TheFirstMatchingLetterIsUnderlinedIgnoringCase() =>
        Assert.Equal(new[] { new AskLabelRun("N", true), new AskLabelRun("ot now", false) }, AskLabel.Runs("Not now", "n", destructivePrefix: false));

    [Fact]
    public void AnUpperCaseLetterInTheLabelMatchesALowerCaseHotkey() =>
        Assert.Equal(new[] { new AskLabelRun("S", true), new AskLabelRun("ave", false) }, AskLabel.Runs("Save", "s", destructivePrefix: false));

    [Fact]
    public void AMissingLetterIsAppendedUpperCaseAndUnderlined() =>
        Assert.Equal(new[] { new AskLabelRun("Delete (", false), new AskLabelRun("X", true), new AskLabelRun(")", false) },
            AskLabel.Runs("Delete", "x", destructivePrefix: false));

    [Fact]
    public void TheTerminalStylePrefixesADestructiveLabel() =>
        Assert.Equal(new[] { new AskLabelRun("! ", false), new AskLabelRun("D", true), new AskLabelRun("elete", false) },
            AskLabel.Runs("Delete", "d", destructivePrefix: true));
}
