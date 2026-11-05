using System.Text.Json.Nodes;
using AgTerm.ControlServer;
using Xunit;

namespace AgTerm.E2E;

/// <summary>msg send, read and ack through the real agtermctl over the control pipe (upstream b0839692, #847).</summary>
public class MessageBusE2ETests : ControlApiTestCase
{
    /// <summary>A new session whose main pane is live; returns its SESSION:PANE-ID address.</summary>
    private string LivePane()
    {
        var id = SendOk("""{"cmd":"session.new"}""")["id"]!.GetValue<string>();
        var token = Guid.NewGuid().ToString("N");
        Host.RunLocked(() => Host.Store.SessionWithId(Guid.Parse(id))!.Surface = new FakeSurface { PaneToken = token });
        return $"{id}:{token}";
    }

    [Fact]
    public void A_message_goes_from_one_pane_to_another_and_is_acked()
    {
        var sender = LivePane();
        var reader = LivePane();

        var send = RunCtl("msg", "send", "build done", "--to", reader, "--as", sender);
        Assert.Equal(0, send.Exit);
        var id = send.Out.TrimEnd('\n');
        Assert.True(Guid.TryParseExact(id, "D", out _), send.Out);

        var read = RunCtl("msg", "read", "--as", reader);
        Assert.Equal(0, read.Exit);
        Assert.StartsWith($"{id} from ", read.Out);
        Assert.EndsWith("\nbuild done\n", read.Out);

        var ack = RunCtl("msg", "ack", id, "--as", reader);
        Assert.Equal((0, "acked 1\n"), (ack.Exit, ack.Out));
        var empty = RunCtl("msg", "read", "--as", reader);
        Assert.Equal((0, ""), (empty.Exit, empty.Out));
    }

    [Fact]
    public void Read_ack_removes_the_batch_it_prints()
    {
        var reader = LivePane();
        Assert.Equal(0, RunCtl("msg", "send", "one", "--to", reader).Exit);

        var read = RunCtl("msg", "read", "--ack", "--wait", "--as", reader);

        Assert.Equal(0, read.Exit);
        Assert.EndsWith(" from outside agterm\none\n", read.Out);
        Assert.Equal("", RunCtl("msg", "read", "--as", reader).Out);
    }

    [Fact]
    public void A_rejected_recipient_fails_the_send_and_names_the_address()
    {
        var reader = LivePane();
        var session = reader.Split(':')[0];
        var gone = $"{session}:{Guid.NewGuid():N}";

        var send = RunCtl("msg", "send", "hi", "--to", reader, "--to", gone);

        Assert.Equal(1, send.Exit);
        Assert.Contains($"rejected {gone}: unknown pane", send.Error);
        Assert.Equal(0, RunCtl("msg", "read", "--ack", "--as", reader).Exit);
    }

    [Fact]
    public void Read_without_an_identity_is_refused()
    {
        var read = RunCtl("msg", "read");

        Assert.Equal(1, read.Exit);
        Assert.Equal("error: msg.read requires an identity: --as SESSION:PANE-ID", read.Error.TrimEnd());
    }

    [Fact]
    public void A_send_emits_a_message_event_without_the_body()
    {
        var reader = LivePane();
        var boot = SendOk("""{"cmd":"events.read"}""")["events"]!.AsObject();
        var run = boot["run"]!.GetValue<string>();
        var after = boot["next"]!.GetValue<ulong>();

        var id = RunCtl("msg", "send", "secret", "--to", reader).Out.TrimEnd('\n');

        var page = SendOk($$$"""{"cmd":"events.read","args":{"run":"{{{run}}}","after":"{{{after}}}","kinds":["message"]}}""");
        var payload = Assert.Single(page["events"]!["items"]!.AsArray())!["payload"]!.AsObject();
        Assert.Equal(id, payload["messageID"]!.GetValue<string>());
        Assert.Equal(reader.Split(':')[1], payload["paneID"]!.GetValue<string>());
        Assert.False(payload.ContainsKey("from"));
        Assert.DoesNotContain("secret", payload.ToJsonString());
    }
}
