using AgTerm.Cli;
using AgTerm.Cli.Tests.TestSupport;
using AgTerm.Core.Protocol;
using Xunit;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli.Tests.Ported;

// Port of upstream RecentCommandsTests.swift and the recent cases of SocketClientTests.swift at v0.40.0.
public class RecentCommandsTests
{
    private static ControlRequest Request(params string[] argv) =>
        Agtermctl.ParseAsRoot(argv).MakeRequest();

    private static string Format(ControlResult result) =>
        ClientOutput.FormatResponse(new ControlResponse { Ok = true, Result = result });

    private static ControlRecentItem Item(string id, string kind, string title, string? subtitle,
                                          params string[] sessionIds) => new()
    {
        Id = id,
        Kind = kind,
        Title = title,
        Subtitle = subtitle,
        ClosedAt = 1,
        WorkspaceId = "W1",
        SessionIds = sessionIds,
    };

    [Fact]
    public void ListAndClearCarryNoArgs()
    {
        CanonicalJson.AssertEqual(new ControlRequest { Cmd = WireCommand.RecentList }, Request("recent", "list"));
        CanonicalJson.AssertEqual(new ControlRequest { Cmd = WireCommand.RecentClear }, Request("recent", "clear"));
    }

    [Theory]
    [InlineData("list")]
    [InlineData("clear")]
    public void ListAndClearRejectAWindowSelector(string sub)
    {
        Assert.ThrowsAny<Exception>(() => Agtermctl.ParseAsRoot(new[] { "recent", sub, "--window", "w1" }));
    }

    [Fact]
    public void OpenWithoutAnIdAsksForTheNewestEntry()
    {
        CanonicalJson.AssertEqual(new ControlRequest { Cmd = WireCommand.RecentOpen }, Request("recent", "open"));
    }

    [Fact]
    public void OpenCarriesTheIdAndWindow()
    {
        CanonicalJson.AssertEqual(
            new ControlRequest
            {
                Cmd = WireCommand.RecentOpen,
                Target = "ab12",
                Args = new ControlArgs { Window = "w1" },
            },
            Request("recent", "open", "ab12", "--window", "w1"));
    }

    [Fact]
    public void FormatsRecentRowsWithFullIdsAndAnEmptyList()
    {
        var session = Item("R1", "session", "api", "project", "S1");
        var workspace = Item("R2", "workspace", "project", null, "S1", "S2");

        Assert.Equal("R1  session  api (project)\nR2  workspace  project",
            Format(new ControlResult { Recent = new[] { session, workspace } }));
        Assert.Equal("no recently closed items", Format(new ControlResult { Recent = Array.Empty<ControlRecentItem>() }));
    }

    [Fact]
    public void FormatsRecentOpenAndSaysWhenTheEntryStays()
    {
        var item = Item("R1", "session", "api", null, "S1");

        Assert.Equal("reopened session api",
            Format(new ControlResult { RecentOpen = new ControlRecentOpen { Item = item, Window = "WIN", Complete = true } }));
        Assert.Equal("reopened session api (incomplete: the entry stays in the list)",
            Format(new ControlResult { RecentOpen = new ControlRecentOpen { Item = item, Window = "WIN", Complete = false } }));
    }

    [Fact]
    public void FormatRecentCountsAParentEntrysCompanions()
    {
        var items = new[]
        {
            Item("R1", "session", "api", "work", "P", "C1", "C2"),
            Item("R2", "session", "db", null, "S"),
            Item("R3", "workspace", "work", "2 sessions", "A", "B"),
        };

        Assert.Equal("R1  session  api (work) +2 companion(s)\nR2  session  db\nR3  workspace  work (2 sessions)",
            ClientOutput.FormatRecent(items));
    }
}
