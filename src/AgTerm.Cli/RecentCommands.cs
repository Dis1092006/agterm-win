using System.CommandLine;
using AgTerm.Core.Protocol;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli;

// Port of agtermctlKit RecentCommands.swift (upstream 6b564749): File > Open Recent over the wire.
internal static class RecentCommands
{
    public static System.CommandLine.Command Build(CommandBindings bindings)
    {
        var recent = new System.CommandLine.Command("recent",
            "Recently closed sessions and workspaces (File > Open Recent).");
        recent.Subcommands.Add(BuildList(bindings));
        recent.Subcommands.Add(BuildOpen(bindings));
        recent.Subcommands.Add(BuildClear(bindings));
        return recent;
    }

    internal sealed class List : IRequestCommand
    {
        public required BasicOptions Basic { get; init; }
        public IConnectionOptions Options => Basic;

        public ControlRequest MakeRequest() => new() { Cmd = WireCommand.RecentList };
    }

    private static System.CommandLine.Command BuildList(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("list",
            "List recently closed sessions and workspaces, newest first. One row per entry: its id, kind, "
            + "title and subtitle. The list keeps the last 20 entries and drops the oldest silently; --json "
            + "adds the close time and the session ids each entry restores.");
        var options = new BasicOptions.Binder();
        options.AddTo(cmd);
        bindings.Register(cmd, result => new List { Basic = options.Read(result) });
        return cmd;
    }

    internal sealed class Open : IRequestCommand
    {
        public string? Id { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.RecentOpen,
            Target = Id,
            Args = Client.WithWindow(),
        };
    }

    private static System.CommandLine.Command BuildOpen(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("open",
            "Reopen a recently closed session or workspace (the newest when no id is given). The entry "
            + "comes back with its id, split, flag, context and restore pins. It goes to --window or the "
            + "frontmost window, unless another window already holds its session or workspace; --json "
            + "names the window it went to. The window is not raised. An entry whose restore was "
            + "incomplete stays in the list.");
        var id = new Argument<string?>("id")
        {
            Description = "Entry id or unique prefix, from 'recent list'.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var options = new ClientOptions.Binder();
        cmd.Arguments.Add(id);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Open { Id = result.GetValue(id), Client = options.Read(result) });
        return cmd;
    }

    internal sealed class Clear : IRequestCommand
    {
        public required BasicOptions Basic { get; init; }
        public IConnectionOptions Options => Basic;

        public ControlRequest MakeRequest() => new() { Cmd = WireCommand.RecentClear };
    }

    private static System.CommandLine.Command BuildClear(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("clear", "Empty the recently closed list.");
        var options = new BasicOptions.Binder();
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Clear { Basic = options.Read(result) });
        return cmd;
    }
}
