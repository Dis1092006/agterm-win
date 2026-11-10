using AgTerm.ControlServer;
using AgTerm.Core.Protocol;

namespace AgTerm.Cli.Tests;

/// <summary>Replies for a fake <see cref="CliEnvironment.Send"/>: each carries the response's own
/// encoding as the line it arrived as. The port of upstream's test-only
/// <c>SocketReply(ControlResponse)</c> init.</summary>
internal static class TestReply
{
    public static ControlReply AsReply(this ControlResponse response) => new(response, ControlJson.Serialize(response));

    public static Func<string, ControlRequest, ControlReply> Replying(Func<string, ControlRequest, ControlResponse> respond) =>
        (pipe, request) => respond(pipe, request).AsReply();
}
