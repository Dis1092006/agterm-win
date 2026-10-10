using System.Text;
using AgTerm.Cli;
using AgTerm.Core.Protocol;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli.Tests.Ported;

// Ported from agtermctlKitTests CommandsTests.swift pick cases (upstream ed831ab).
public class PickCommandsTests
{
    private static PickCommands.Open ParseOpen(params string[] argv) =>
        Assert.IsType<PickCommands.Open>(Agtermctl.ParseAsRoot(argv));

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public void PickDefaultsToOpen()
    {
        Assert.IsType<PickCommands.Open>(Agtermctl.ParseAsRoot(new[] { "pick" }));
        Assert.IsType<PickCommands.Open>(Agtermctl.ParseAsRoot(new[] { "pick", "open" }));
    }

    [Fact]
    public void OpenSniffsAJsonArray()
    {
        var request = ParseOpen("pick").MakeRequest(Utf8("  \n[{\"id\":\"a\",\"label\":\"Alpha\",\"subtitle\":\"s\"}]"));
        Assert.Equal(WireCommand.PickOpen, request.Cmd);
        Assert.Equal(new ControlPickItem { Id = "a", Label = "Alpha", Subtitle = "s" }, Assert.Single(request.Args!.Items!));
    }

    [Fact]
    public void OpenTakesBareLinesAsIdAndLabelAndDropsBlankLines()
    {
        var items = ParseOpen("pick").MakeRequest(Utf8("one\r\n  \n two " + (char)0x2028 + "three\n")).Args!.Items!;
        Assert.Equal(new[] { "one", " two ", "three" }, items.Select(i => i.Id));
        Assert.Equal(items.Select(i => i.Id), items.Select(i => i.Label));
    }

    [Fact]
    public void OpenSplitsOnVerticalTabAndFormFeed()
    {
        var items = ParseOpen("pick").MakeRequest(Utf8("a\u000Bb\u000Cc")).Args!.Items!;
        Assert.Equal(new[] { "a", "b", "c" }, items.Select(i => i.Id));
    }

    [Fact]
    public void OpenDropsLinesOfOnlyUnicodeSpaceSeparatorsOrTabs()
    {
        var items = ParseOpen("pick").MakeRequest(Utf8("a\n\u00A0\n\u3000\t \n\u00A0b\u3000\n")).Args!.Items!;
        Assert.Equal(new[] { "a", "\u00A0b\u3000" }, items.Select(i => i.Id));
    }

    [Fact]
    public void OpenSendsEmptyItemsForEmptyStdin()
    {
        var request = ParseOpen("pick").MakeRequest(Array.Empty<byte>());
        Assert.NotNull(request.Args!.Items);
        Assert.Empty(request.Args!.Items!);
    }

    [Fact]
    public void OpenDropsAUtf8BomBeforeSniffing()
    {
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var items = ParseOpen("pick").MakeRequest(bom.Concat(Utf8("[{\"id\":\"a\",\"label\":\"A\"}]")).ToArray()).Args!.Items!;
        Assert.Equal("a", Assert.Single(items).Id);
        Assert.Equal("x", Assert.Single(ParseOpen("pick").MakeRequest(bom.Concat(Utf8("x")).ToArray()).Args!.Items!).Id);
    }

    [Theory]
    [InlineData("[{\"id\":\"a\"}]")]
    [InlineData("[not json")]
    [InlineData("[null]")]
    public void OpenRejectsMalformedJson(string stdin)
    {
        var error = Assert.ThrowsAny<Exception>(() => ParseOpen("pick").MakeRequest(Utf8(stdin)));
        Assert.Equal("stdin must be UTF-8 text or a JSON item array", Agtermctl.Message(error));
    }

    [Fact]
    public void OpenRejectsInvalidUtf8Lines()
    {
        var error = Assert.ThrowsAny<Exception>(() => ParseOpen("pick").MakeRequest(new byte[] { 0x61, 0xFF }));
        Assert.Equal("stdin must be UTF-8 text or a JSON item array", Agtermctl.Message(error));
    }

    [Fact]
    public void OpenMapsEveryOptionToTheRequest()
    {
        var command = ParseOpen("pick", "--prompt", "Choose", "--query", "q", "--allow-custom", "--select", "a",
            "--follow", "--no-block", "--window", "w1");
        var args = command.MakeRequest(Utf8("a\n")).Args!;

        Assert.Equal("Choose", args.Prompt);
        Assert.Equal("q", args.Query);
        Assert.True(args.AllowCustom);
        Assert.Equal("a", args.Selection);
        Assert.True(args.Follow);
        Assert.Equal("w1", args.Window);
        Assert.True(command.NoBlock);
    }

    [Fact]
    public void OpenOmitsFalseFlagsAndUnsetOptions()
    {
        var args = ParseOpen("pick").MakeRequest(Utf8("a\n")).Args!;
        Assert.Null(args.AllowCustom);
        Assert.Null(args.Follow);
        Assert.Null(args.Prompt);
        Assert.Null(args.Query);
        Assert.Null(args.Selection);
        Assert.Null(args.Window);
    }

    [Fact]
    public void OpenHasNoTimeoutOption()
    {
        Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(new[] { "pick", "--timeout", "5" }));
    }

    [Fact]
    public void ResultAndCancelMapIdAndWindow()
    {
        var result = Agtermctl.ParseAsRoot(new[] { "pick", "result", "P", "--window", "w" }).MakeRequest();
        Assert.Equal(WireCommand.PickResult, result.Cmd);
        Assert.Equal("P", result.Target);
        Assert.Equal("w", result.Args!.Window);

        var cancel = Agtermctl.ParseAsRoot(new[] { "pick", "cancel", "P" }).MakeRequest();
        Assert.Equal(WireCommand.PickCancel, cancel.Cmd);
        Assert.Equal("P", cancel.Target);
        Assert.Null(cancel.Args);
    }
}
