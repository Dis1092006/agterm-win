using System.CommandLine;
using AgTerm.Core.Protocol;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli;

// Windows-only (parity/windows-only.json): upstream has no workspace icons.
internal static class IconCommands
{
    internal sealed class List : IRequestCommand
    {
        public required BasicOptions Basic { get; init; }
        public IConnectionOptions Options => Basic;

        public ControlRequest MakeRequest() => new() { Cmd = WireCommand.IconList };
    }

    public static System.CommandLine.Command Build(CommandBindings bindings)
    {
        var icon = new System.CommandLine.Command("icon", "Icon commands.");
        icon.Subcommands.Add(BuildList(bindings));
        return icon;
    }

    private static System.CommandLine.Command BuildList(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("list",
            "List the icon names workspace icon accepts (Windows only).");
        var options = new BasicOptions.Binder();
        options.AddTo(cmd);
        bindings.Register(cmd, result => new List { Basic = options.Read(result) });
        return cmd;
    }
}
