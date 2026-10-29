using System.CommandLine;

namespace AgTerm.Cli;

/// <summary>Maps each System.CommandLine leaf to the factory that binds its parsed values into an
/// IRequestCommand — the seam ParseAsRoot and the ported tests drive.</summary>
internal sealed class CommandBindings
{
    private readonly Dictionary<Command, Func<ParseResult, IRequestCommand>> _binders = new();

    public void Register(Command command, Func<ParseResult, IRequestCommand> bind) =>
        _binders[command] = bind;

    public Func<ParseResult, IRequestCommand>? For(Command command) => _binders.GetValueOrDefault(command);
}

/// <summary>The root agtermctl command. Subcommands mirror the control catalog 1:1.</summary>
public static class Agtermctl
{
    /// <summary>Parse argv into a bound subcommand (its Validate already run) — the port of
    /// ArgumentParser's parseAsRoot the tests build requests through. Throws CliParseException for
    /// unknown commands/options/missing arguments and CliValidationException for a validate failure.</summary>
    internal static IRequestCommand ParseAsRoot(string[] argv)
    {
        var (root, bindings) = Build();
        return Bind(root.Parse(argv), bindings);
    }

    private static IRequestCommand Bind(ParseResult result, CommandBindings bindings)
    {
        // a group with a DEFAULT subcommand (quick/sidebar visibility) binds on the group command
        // itself; System.CommandLine still emits "Required command was not provided." for it, so
        // that one error is ignored exactly when a binder exists for the parsed command.
        var errors = bindings.For(result.CommandResult.Command) is null
            ? result.Errors
            : result.Errors.Where(e => e.Message != "Required command was not provided.").ToList();
        if (errors.Count > 0)
        {
            throw new CliParseException(string.Join("\n", errors.Select(e => e.Message)));
        }
        var binder = bindings.For(result.CommandResult.Command)
            ?? throw new CliParseException(
                $"'{result.CommandResult.Command.Name}' requires a subcommand");
        var command = binder(result);
        command.Validate();
        return command;
    }

    /// <summary>The user-facing message for a parse/validation error — the port of
    /// Agtermctl.message(for:).</summary>
    public static string Message(Exception error) => error.Message;

    /// <summary>The process entry: parse, execute, map failures to exit codes (64 = usage, 1 =
    /// server-side error or transport failure).</summary>
    public static int Run(string[] argv, CliEnvironment? env = null)
    {
        env ??= new CliEnvironment();
        var (root, bindings) = Build();
        var result = root.Parse(argv);
        // --help / --version parse to a built-in action — let System.CommandLine render it.
        if (result.Action is not null && result.Errors.Count == 0)
        {
            return result.Invoke();
        }
        IRequestCommand command;
        try
        {
            command = Bind(result, bindings);
        }
        catch (Exception e) when (e is CliParseException or CliValidationException)
        {
            env.Error.WriteLine("error: " + Message(e));
            return 64;
        }
        try
        {
            return command.Execute(env);
        }
        catch (Exception e) when (e is AgTerm.ControlServer.PipeClientException or CliValidationException)
        {
            env.Error.WriteLine("error: " + Message(e));
            return 1;
        }
    }

    private static (RootCommand Root, CommandBindings Bindings) Build()
    {
        var bindings = new CommandBindings();
        return (BuildRoot(bindings), bindings);
    }

    private static RootCommand BuildRoot(CommandBindings bindings)
    {
        var root = new RootCommand("Drive agterm-win over its control named pipe.");
        foreach (var command in new[]
                 {
                     TreeCommand.Build(bindings),
                     EventCommands.Build(bindings),
                     WorkspaceCommands.Build(bindings),
                     SessionCommands.Build(bindings),
                     SurfaceCommands.Build(bindings),
                     DashboardCommand.Build(bindings),
                     WindowCommands.Build(bindings),
                     QuickCommands.Build(bindings),
                     PickCommands.Build(bindings),
                     AskCommands.Build(bindings),
                     SidebarCommands.Build(bindings),
                     NotifyCommand.Build(bindings),
                     FontCommands.Build(bindings),
                     KeymapCommands.Build(bindings),
                     ConfigCommands.Build(bindings),
                     ThemeCommands.Build(bindings),
                     IconCommands.Build(bindings),
                     RestoreCommands.Build(bindings),
                 })
        {
            root.Subcommands.Add(command);
        }
        return root;
    }
}
