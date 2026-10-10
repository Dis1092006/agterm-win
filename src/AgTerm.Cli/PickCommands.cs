using System.CommandLine;
using System.Text;
using System.Text.Json;
using AgTerm.Core.Protocol;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli;

/// <summary>Port of upstream's pick stdin: a JSON item array when the first non-whitespace byte is
/// '[', else one item per non-blank UTF-8 line.</summary>
internal static class PickStdin
{
    public const string InvalidInput = "stdin must be UTF-8 text or a JSON item array";

    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // Upstream splits with components(separatedBy: .newlines): CR, LF, VT, FF, NEL, LS and PS.
    private static readonly char[] LineBreaks = { '\r', '\n', '\u000B', '\u000C', '\u0085', '\u2028', '\u2029' };

    private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

    public static ControlPickItem[] Parse(byte[] input)
    {
        var bytes = StripBom(input);
        var first = bytes.IndexOfAnyExcept(" \t\n\r"u8);
        return first >= 0 && bytes[first] == (byte)'['
            ? ParseJson(bytes)
            : ParseLines(bytes);
    }

    // Windows PowerShell 5.1 and Notepad write a BOM; left in, a JSON file is read as lines
    // and the first id starts with U+FEFF.
    private static ReadOnlySpan<byte> StripBom(byte[] input)
    {
        ReadOnlySpan<byte> bytes = input;
        return bytes.StartsWith(Utf8Bom) ? bytes[Utf8Bom.Length..] : bytes;
    }

    private static ControlPickItem[] ParseLines(ReadOnlySpan<byte> bytes)
    {
        string text;
        try
        {
            text = Strict.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new CliValidationException(InvalidInput);
        }
        return text.Split(LineBreaks)
            .Where(line => !IsBlank(line))
            .Select(line => new ControlPickItem { Id = line, Label = line })
            .ToArray();
    }

    // Upstream trims CharacterSet.whitespaces: Unicode space separators and tab. The item keeps the line verbatim.
    private static bool IsBlank(string line) =>
        line.All(c => c == '\t' || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.SpaceSeparator);

    private static ControlPickItem[] ParseJson(ReadOnlySpan<byte> bytes)
    {
        ControlPickItem[]? items;
        try
        {
            items = JsonSerializer.Deserialize<ControlPickItem[]>(bytes, ControlJson.Options);
        }
        catch (JsonException)
        {
            throw new CliValidationException(InvalidInput);
        }
        // Required members only reject a missing key; an explicit null still deserializes.
        if (items is null || items.Any(item => item is null || item.Id is null || item.Label is null))
            throw new CliValidationException(InvalidInput);
        return items;
    }
}

/// <summary>Port of upstream's pick ModalCommandRunner: open, then poll pick.result by id until a
/// terminal outcome. The server never blocks, so other clients keep working while a user decides.</summary>
internal static class PickRunner
{
    public static TimeSpan PollDelay(int pendingPolls) =>
        pendingPolls < 10 ? TimeSpan.FromSeconds(0.1) : TimeSpan.FromSeconds(0.5);

    public static int ExitCode(ControlPickOutcome outcome) => outcome switch
    {
        ControlPickOutcome.Picked or ControlPickOutcome.Custom => 0,
        ControlPickOutcome.Cancelled => 2,
        _ => 1,
    };

    public static int RunOpen(CliEnvironment env, IConnectionOptions options, ControlRequest open, bool noBlock)
    {
        var pipe = options.PipeName(env.GetEnv);
        var opened = env.Send(pipe, open);
        if (!opened.Ok)
        {
            ClientOutput.PrintResponse(env, opened, options.Json);
            return 1;
        }
        if (opened.Result?.Id is not { } id)
        {
            env.Error.WriteLine("error: pick.open result missing id");
            return 1;
        }
        if (noBlock)
        {
            env.Out.WriteLine(ControlJson.Serialize(new ControlResult { Id = id }));
            return 0;
        }

        for (var pendingPolls = 0; ; pendingPolls++)
        {
            ControlResponse polled;
            try
            {
                // no window: the id is global, so a frontmost change cannot make the poll miss it
                polled = env.Send(pipe, new ControlRequest { Cmd = WireCommand.PickResult, Target = id });
            }
            catch (AgTerm.ControlServer.PipeClientException)
            {
                CancelBestEffort(env, pipe, id);
                throw;
            }
            if (!polled.Ok)
            {
                // the server no longer has the pick, so there is nothing to cancel
                ClientOutput.PrintResponse(env, polled, options.Json);
                return 1;
            }
            if (polled.Result?.Pick is not { } result)
            {
                env.Error.WriteLine("error: pick.result missing result");
                CancelBestEffort(env, pipe, id);
                return 1;
            }
            if (result.Result != ControlPickOutcome.Pending)
            {
                env.Out.WriteLine(ControlJson.Serialize(result));
                return ExitCode(result.Result);
            }
            env.Sleep(PollDelay(pendingPolls));
        }
    }

    public static int PrintResult(CliEnvironment env, IConnectionOptions options, ControlRequest request)
    {
        var response = env.Send(options.PipeName(env.GetEnv), request);
        if (!response.Ok)
        {
            ClientOutput.PrintResponse(env, response, options.Json);
            return 1;
        }
        if (response.Result?.Pick is not { } result)
        {
            env.Error.WriteLine("error: pick.result missing result");
            return 1;
        }
        env.Out.WriteLine(ControlJson.Serialize(result));
        return ExitCode(result.Result);
    }

    private static void CancelBestEffort(CliEnvironment env, string pipe, string id)
    {
        try
        {
            env.Send(pipe, new ControlRequest { Cmd = WireCommand.PickCancel, Target = id });
        }
        catch (AgTerm.ControlServer.PipeClientException)
        {
            // the transport already failed once; the caller rethrows the original failure
        }
    }
}

internal static class PickCommands
{
    /// <summary>`agtermctl pick [open]`: show a picker in agterm, wait for the answer, print it.
    /// Items come on stdin.</summary>
    internal sealed class Open : IRequestCommand
    {
        public string? Prompt { get; init; }
        public string? Query { get; init; }
        public string? Select { get; init; }
        public bool AllowCustom { get; init; }
        public bool Follow { get; init; }
        public bool NoBlock { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        /// <summary>Where the items come from; injectable for tests. Upstream reads stdin inside
        /// makeRequest, so the read happens only at request-build time.</summary>
        public Func<Stream> StdinStream { get; init; } = Console.OpenStandardInput;

        public ControlRequest MakeRequest() => MakeRequest(TypedStdin.ReadAll(StdinStream()));

        public int Execute(CliEnvironment env) => PickRunner.RunOpen(env, Options, MakeRequest(), NoBlock);

        internal ControlRequest MakeRequest(byte[] stdin) => new()
        {
            Cmd = WireCommand.PickOpen,
            Args = Client.WithWindow(new ControlArgs
            {
                Items = PickStdin.Parse(stdin),
                Prompt = Prompt,
                Query = Query,
                AllowCustom = AllowCustom ? true : null,
                Selection = Select,
                Follow = Follow ? true : null,
            }),
        };
    }

    /// <summary>`agtermctl pick result ID`: print a picker's current or final result.</summary>
    internal sealed class Result : IRequestCommand
    {
        public required string Id { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.PickResult,
            Target = Id,
            Args = Client.WithWindow(),
        };

        public int Execute(CliEnvironment env) => PickRunner.PrintResult(env, Options, MakeRequest());
    }

    /// <summary>`agtermctl pick cancel ID`: close a pending picker as cancelled.</summary>
    internal sealed class Cancel : IRequestCommand
    {
        public required string Id { get; init; }
        public required ClientOptions Client { get; init; }
        public IConnectionOptions Options => Client;

        public ControlRequest MakeRequest() => new()
        {
            Cmd = WireCommand.PickCancel,
            Target = Id,
            Args = Client.WithWindow(),
        };
    }

    public static System.CommandLine.Command Build(CommandBindings bindings)
    {
        var pick = new System.CommandLine.Command("pick",
            "Show a picker in agterm and print the choice (items on stdin).");
        var open = new System.CommandLine.Command("open", "Show a picker and wait for the answer.");
        BindOpen(bindings, open);
        pick.Subcommands.Add(open);
        pick.Subcommands.Add(BuildIdCommand(bindings, "result", "Print a picker's result.",
            (id, client) => new Result { Id = id, Client = client }));
        pick.Subcommands.Add(BuildIdCommand(bindings, "cancel", "Cancel a pending picker.",
            (id, client) => new Cancel { Id = id, Client = client }));
        // defaultSubcommand: Open, so a bare `agtermctl pick` opens a picker
        BindOpen(bindings, pick);
        return pick;
    }

    private static void BindOpen(CommandBindings bindings, System.CommandLine.Command command)
    {
        var prompt = new Option<string?>("--prompt") { Description = "Placeholder text of the query field." };
        var query = new Option<string?>("--query") { Description = "Text the query field opens with." };
        var allowCustom = new Option<bool>("--allow-custom") { Description = "Accept a query that matches no item." };
        var select = new Option<string?>("--select") { Description = "Item id highlighted when the picker opens." };
        var follow = new Option<bool>("--follow") { Description = "Raise the target window first." };
        var noBlock = new Option<bool>("--no-block") { Description = "Print the picker id and return at once." };
        var client = new ClientOptions.Binder();
        client.AddTo(command);
        foreach (var option in new Option[] { prompt, query, allowCustom, select, follow, noBlock })
            command.Options.Add(option);
        bindings.Register(command, result => new Open
        {
            Prompt = result.GetValue(prompt),
            Query = result.GetValue(query),
            AllowCustom = result.GetValue(allowCustom),
            Select = result.GetValue(select),
            Follow = result.GetValue(follow),
            NoBlock = result.GetValue(noBlock),
            Client = client.Read(result),
        });
    }

    private static System.CommandLine.Command BuildIdCommand(
        CommandBindings bindings, string name, string description,
        Func<string, ClientOptions, IRequestCommand> create)
    {
        var command = new System.CommandLine.Command(name, description);
        var id = new Argument<string>("id") { Description = "The picker id pick.open returned." };
        command.Arguments.Add(id);
        var client = new ClientOptions.Binder();
        client.AddTo(command);
        bindings.Register(command, result => create(result.GetValue(id)!, client.Read(result)));
        return command;
    }
}
