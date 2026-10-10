using System.CommandLine;
using System.Text;
using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Cli;

/// <summary>A CLI usage/validation failure — the port of ArgumentParser's ValidationError. The
/// message is user-facing; some messages are pinned by the ported tests.</summary>
internal sealed class CliValidationException : Exception
{
    public CliValidationException(string message) : base(message)
    {
    }
}

/// <summary>A parse failure raised by ParseAsRoot for unknown commands/options/missing arguments.</summary>
internal sealed class CliParseException : Exception
{
    public CliParseException(string message) : base(message)
    {
    }
}

/// <summary>The --stdin payload of session type / quick type. Upstream refuses bytes that are
/// not UTF-8 instead of typing a lossy decode (agtermctlKit decodeTypedStdin).</summary>
internal static class TypedStdin
{
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static string Decode(byte[] input)
    {
        try
        {
            return Strict.GetString(input);
        }
        catch (DecoderFallbackException)
        {
            throw new CliValidationException("stdin must be valid UTF-8");
        }
    }

    public static byte[] ReadAll(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}

/// <summary>The process seams a command run touches, injectable for tests: environment lookup,
/// output/error writers, stdin, and the pipe-client factory.</summary>
public sealed class CliEnvironment
{
    public Func<string, string?> GetEnv { get; init; } = Environment.GetEnvironmentVariable;

    public TextWriter Out { get; init; } = Console.Out;

    public TextWriter Error { get; init; } = Console.Error;

    public TextReader In { get; init; } = Console.In;

    /// <summary>Sends one request over one connection — the transport seam. The default connects a
    /// real ControlPipeClient; tests substitute a canned responder.</summary>
    public Func<string, ControlRequest, ControlResponse> Send { get; init; } =
        (pipeName, request) => new ControlPipeClient(pipeName).Send(request);

    /// <summary>Sleep between polls (overlay --block, events --follow); injectable for tests.</summary>
    public Action<TimeSpan> Sleep { get; init; } = delay => Thread.Sleep(delay);
}

internal static class CliOptions
{
    /// <summary>A Double option that parses like Swift ArgumentParser: the special spellings
    /// (inf/Infinity/nan, and overflow like 1e999) PARSE into non-finite values so the command's
    /// Validate() stays the rejection point with the pinned upstream message — System.CommandLine's
    /// stock converter would reject them at tokenize time with its own wording.</summary>
    public static Option<double?> LenientDouble(string name, string description)
    {
        var option = new Option<double?>(name) { Description = description };
        option.CustomParser = result =>
        {
            var token = result.Tokens[^1].Value;
            if (double.TryParse(token, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
            if (token.Equals("inf", StringComparison.OrdinalIgnoreCase)
                || token.Equals("+inf", StringComparison.OrdinalIgnoreCase))
            {
                return double.PositiveInfinity;
            }
            if (token.Equals("-inf", StringComparison.OrdinalIgnoreCase))
            {
                return double.NegativeInfinity;
            }
            result.AddError($"Cannot parse argument '{token}' for option '{name}' as expected type Double.");
            return null;
        };
        return option;
    }
}

/// <summary>The connection/print surface every subcommand shares: where to connect and how to
/// print. The default Execute drives only these.</summary>
internal interface IConnectionOptions
{
    /// <summary>Print the raw JSON response instead of a human-readable line.</summary>
    bool Json { get; }

    /// <summary>Resolve the platform-neutral socket path from --socket and the environment —
    /// upstream precedence: --socket → &lt;AGTERM_STATE_DIR&gt;\agterm.sock → the default state dir.</summary>
    string SocketPath(Func<string, string?> env);
}

internal static class ConnectionOptionsExtensions
{
    /// <summary>The pipe name the resolved socket path maps to (see PLATFORM-NOTES): an explicit
    /// --socket derives directly; else AGTERM_CONTROL_SOCKET is the literal pipe-name override; else
    /// the name derives from the state-dir socket path.</summary>
    public static string PipeName(this IConnectionOptions options, Func<string, string?> env)
    {
        if (options is BasicOptions { Socket: { Length: > 0 } explicitSocket })
            return ControlPipeName.Derive(explicitSocket);
        if (options is ClientOptions { Basic.Socket: { Length: > 0 } clientSocket })
            return ControlPipeName.Derive(clientSocket);
        if (env("AGTERM_CONTROL_SOCKET") is { Length: > 0 } pipe) return pipe;
        return ControlPipeName.Derive(options.SocketPath(env));
    }
}

/// <summary>Where to connect and how to print — the options every subcommand accepts. window.*
/// commands use this directly; the session/workspace/tree/font commands layer --window on top via
/// ClientOptions.</summary>
internal sealed class BasicOptions : IConnectionOptions
{
    public string? Socket { get; init; }

    public bool Json { get; init; }

    /// <summary>Resolve the socket path: explicit --socket, else the agtermCore rendezvous resolver.
    /// Precedence: --socket → &lt;AGTERM_STATE_DIR&gt;\agterm.sock → %USERPROFILE%\.agterm-win\agterm.sock
    /// → &lt;temp&gt;\.agterm-win\agterm.sock (the analogue of upstream's $HOME → /tmp fallback; see
    /// PLATFORM-NOTES). Must stay in step with PersistenceStore.RealUserDirectory — the app and the
    /// CLI only meet when both derive the same path. env is injectable so the precedence is
    /// unit-testable.</summary>
    public string SocketPath(Func<string, string?> env)
    {
        if (Socket is not null) return Socket;
        var appSupport = env("USERPROFILE") is { Length: > 0 } home
            ? Path.Combine(home, PersistenceStore.AppDirectoryName)
            : Path.Combine(Path.GetTempPath(), PersistenceStore.AppDirectoryName);
        return ControlResolve.SocketPath(stateDir: env("AGTERM_STATE_DIR"), appSupport: appSupport);
    }

    internal sealed class Binder
    {
        public readonly Option<string?> Socket = new("--socket")
        {
            Description = "Override the control socket path.",
        };

        public readonly Option<bool> Json = new("--json")
        {
            Description = "Print the raw JSON response.",
        };

        public void AddTo(System.CommandLine.Command command)
        {
            command.Options.Add(Socket);
            command.Options.Add(Json);
        }

        public BasicOptions Read(ParseResult result) => new()
        {
            Socket = result.GetValue(Socket),
            Json = result.GetValue(Json),
        };
    }
}

/// <summary>BasicOptions plus the --window selector for the commands that operate on a window's tree.</summary>
internal sealed class ClientOptions : IConnectionOptions
{
    public required BasicOptions Basic { get; init; }

    /// <summary>Target window for session/workspace/tree/font commands: id / prefix / "active"
    /// (=frontmost). Maps to ControlArgs.window.</summary>
    public string? Window { get; init; }

    public bool Json => Basic.Json;

    public string SocketPath(Func<string, string?> env) => Basic.SocketPath(env);

    /// <summary>Fold the --window selector into an existing args bag, or build one carrying only the
    /// window. Returns null when there is no window and no base bag, so the request stays in its
    /// compact form (no empty args object on the wire).</summary>
    public ControlArgs? WithWindow(ControlArgs? @base = null)
    {
        if (Window is null) return @base;
        return (@base ?? new ControlArgs()) with { Window = Window };
    }

    internal sealed class Binder
    {
        public readonly BasicOptions.Binder Basic = new();

        public readonly Option<string?> Window = new("--window")
        {
            Description = "Target window id, unique prefix, or 'active' (defaults to the frontmost).",
        };

        public void AddTo(System.CommandLine.Command command)
        {
            Basic.AddTo(command);
            command.Options.Add(Window);
        }

        public ClientOptions Read(ParseResult result) => new()
        {
            Basic = Basic.Read(result),
            Window = result.GetValue(Window),
        };
    }
}

/// <summary>Options for the commands that address a single session or workspace; --target defaults
/// to "active".</summary>
internal sealed class TargetOptions
{
    public string Target { get; init; } = "active";

    internal sealed class Binder
    {
        public readonly Option<string> Target = new("--target")
        {
            Description = "Target session/workspace id, unique prefix, or 'active'.",
            DefaultValueFactory = _ => "active",
        };

        public void AddTo(System.CommandLine.Command command) => command.Options.Add(Target);

        public TargetOptions Read(ParseResult result) => new() { Target = result.GetValue(Target) ?? "active" };
    }
}

/// <summary>Options for batch-capable session commands. Repeating --target preserves CLI order on
/// the wire.</summary>
internal sealed class BatchTargetOptions
{
    public IReadOnlyList<string> Targets { get; init; } = Array.Empty<string>();

    /// <summary>The batch form (two or more targets), or null for the single/default form.</summary>
    public string[]? BatchTargets => Targets.Count > 1 ? Targets.ToArray() : null;

    internal sealed class Binder
    {
        public readonly Option<string[]> Targets = new("--target")
        {
            Description = "Target session id, prefix, or 'active'. Repeat for a batch.",
            DefaultValueFactory = _ => Array.Empty<string>(),
        };

        public void AddTo(System.CommandLine.Command command) => command.Options.Add(Targets);

        public BatchTargetOptions Read(ParseResult result) => new()
        {
            Targets = result.GetValue(Targets) ?? Array.Empty<string>(),
        };
    }
}

/// <summary>Options for commands that address an individual terminal surface from tree.</summary>
internal sealed class SurfaceTargetOptions
{
    public string Target { get; init; } = "active";

    internal sealed class Binder
    {
        public readonly Option<string> Target = new("--target")
        {
            Description = "Target surface id from tree, 'quick' (the quick terminal), or 'active'.",
            DefaultValueFactory = _ => "active",
        };

        public void AddTo(System.CommandLine.Command command) => command.Options.Add(Target);

        public SurfaceTargetOptions Read(ParseResult result) => new()
        {
            Target = result.GetValue(Target) ?? "active",
        };
    }
}

/// <summary>
/// A subcommand that knows how to build the ControlRequest it should send. The default Execute
/// sends it and prints the response; tests build the request directly via MakeRequest.
/// </summary>
internal interface IRequestCommand
{
    IConnectionOptions Options { get; }

    ControlRequest MakeRequest();

    /// <summary>Whether the human-readable output should echo result.id. Default false — the id is
    /// just noise when the caller already named the target; the create commands (*.new) override it
    /// to true. The id is always present under --json.</summary>
    bool EchoesResultId => false;

    /// <summary>ArgumentParser's validate() seam: runs during ParseAsRoot, before any request is
    /// built or any connection made. Throws CliValidationException with a user-facing message.</summary>
    void Validate()
    {
    }

    /// <summary>The default behavior: send the request once and print the response. Overridden by
    /// the poll-shaped commands (overlay --block, events --follow).</summary>
    int Execute(CliEnvironment env) => this.DefaultRun(env);
}

internal static class RequestCommandExtensions
{
    /// <summary>Send the request once and print the response — the single-round-trip path a
    /// run-overriding command can still reach.</summary>
    public static int DefaultRun(this IRequestCommand command, CliEnvironment env)
    {
        var request = command.MakeRequest();
        var response = env.Send(command.Options.PipeName(env.GetEnv), request);
        ClientOutput.PrintResponse(env, response, command.Options.Json, command.EchoesResultId);
        return response.Ok ? 0 : 1;
    }
}
