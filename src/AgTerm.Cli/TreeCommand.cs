using System.CommandLine;
using AgTerm.Core.Protocol;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli;

// Port of the tree subcommand from agtermctlKit Commands.swift.

internal static class TreeCommand
{
    internal sealed class Tree : IRequestCommand
    {
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.Tree,
            Args = Client.WithWindow(),
        };
    }

    public static System.CommandLine.Command Build(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("tree", "Print the workspace/session tree.");
        var options = new ClientOptions.Binder();
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Tree { Client = options.Read(result) });
        return cmd;
    }
}
