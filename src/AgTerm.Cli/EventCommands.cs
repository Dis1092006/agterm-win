using System.CommandLine;
using System.Globalization;
using AgTerm.Core.Protocol;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli;

// Port of agtermctlKit EventCommands.swift — the events poller. The Swift file's seam types map
// onto the shared CLI framework: EventStreamDependencies → CliEnvironment (Send/Sleep/Out),
// EventStreamError → CliValidationException (Agtermctl.Run prints "error: …" and exits 1, the
// counterpart of ArgumentParser exiting 1 on a thrown run() error).

/// <summary>The cursor-carrying poll state — the port of the Swift EventStreamState struct. Cursor
/// starts from --run/--after (or null for the subscribe-from-now bootstrap) and advances to each
/// batch's {run, next}.</summary>
internal sealed class EventStreamState
{
    public ControlEventCursor? Cursor { get; private set; }

    public IReadOnlySet<ControlEventKind>? Kinds { get; }

    public int? Limit { get; }

    public EventStreamState(ControlEventCursor? cursor, IReadOnlySet<ControlEventKind>? kinds, int? limit)
    {
        Cursor = cursor;
        Kinds = kinds;
        Limit = limit;
    }

    public ControlRequest MakeRequest()
    {
        // allCases order filtered by membership, as in the Swift — the wire kinds are deterministic
        // regardless of set-iteration order.
        var rawKinds = Kinds is null
            ? null
            : Enum.GetValues<ControlEventKind>().Where(Kinds.Contains).Select(EventFormatter.Wire).ToArray();
        if (Cursor is null && rawKinds is null && Limit is null)
            return new ControlRequest { Cmd = WireCommand.EventsRead };
        return new ControlRequest
        {
            Cmd = WireCommand.EventsRead,
            Args = new ControlArgs
            {
                After = Cursor?.After.ToString(CultureInfo.InvariantCulture),
                // Swift UUID.uuidString is the uppercase canonical form.
                Run = Cursor?.Run.ToString("D").ToUpperInvariant(),
                Kinds = rawKinds,
                Limit = Limit,
            },
        };
    }

    /// <summary>Take one response: fail loudly on a control error or a missing batch, otherwise
    /// advance the cursor to the batch's {run, next} and return its items.</summary>
    public ControlEvent[] Consume(ControlResponse response)
    {
        if (!response.Ok)
            throw new CliValidationException(response.Error ?? "events.read failed");
        var batch = response.Result?.Events
            ?? throw new CliValidationException("events.read response missing events");
        Cursor = new ControlEventCursor(batch.Run, batch.Next);
        return batch.Items;
    }
}

/// <summary>Per-event line rendering — the port of the Swift EventFormatter enum.</summary>
internal static class EventFormatter
{
    /// <summary>The kind's wire token (the Swift rawValue).</summary>
    public static string Wire(ControlEventKind kind) => kind switch
    {
        ControlEventKind.Status => "status",
        ControlEventKind.Notify => "notify",
        ControlEventKind.SessionCreated => "session.created",
        ControlEventKind.SessionClosed => "session.closed",
        ControlEventKind.TreeChanged => "tree.changed",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string Json(ControlEvent @event) => ControlJson.Serialize(@event);

    public static string Human(ControlEvent @event, TimeZoneInfo? timeZone = null)
    {
        var instant = DateTimeOffset.FromUnixTimeMilliseconds((long)(@event.Ts * 1000));
        var time = TimeZoneInfo.ConvertTime(instant, timeZone ?? TimeZoneInfo.Local)
            .ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var name = @event.Payload.Name ?? @event.Window ?? "-";
        switch (@event.Kind)
        {
            case ControlEventKind.Status:
                var parts = new List<string> { time, Wire(@event.Kind), name, @event.Payload.Status ?? "idle" };
                if (@event.Payload.Pane is { } pane) parts.Add($"pane={pane}");
                if (@event.Payload.Blink == true) parts.Add("blink");
                if (@event.Payload.Color is { } color) parts.Add($"color={color}");
                if (@event.Payload.Shape is { } shape) parts.Add($"shape={shape}");
                return string.Join(" ", parts);
            case ControlEventKind.Notify:
                return $"{time} {Wire(@event.Kind)} {name} {@event.Payload.Title ?? name}: {@event.Payload.Body ?? ""}";
            default: // sessionCreated, sessionClosed, treeChanged
                return $"{time} {Wire(@event.Kind)} {name}";
        }
    }
}

internal static class EventCommands
{
    public static System.CommandLine.Command Build(CommandBindings bindings)
    {
        var cmd = new System.CommandLine.Command("events", "Continuously print control events.");
        var kind = new Option<string[]>("--kind")
        {
            Description = "Event kind; repeat or comma-separate values.",
            DefaultValueFactory = _ => Array.Empty<string>(),
        };
        var run = new Option<string?>("--run")
        {
            Description = "App-run UUID paired with --after.",
        };
        var after = new Option<string?>("--after")
        {
            Description = "Sequence cursor paired with --run.",
        };
        var limit = new Option<int?>("--limit")
        {
            Description = "Events per read (1...1000).",
        };
        var options = new BasicOptions.Binder();
        cmd.Options.Add(kind);
        cmd.Options.Add(run);
        cmd.Options.Add(after);
        cmd.Options.Add(limit);
        options.AddTo(cmd);
        bindings.Register(cmd, result => new Events
        {
            KindFields = result.GetValue(kind) ?? Array.Empty<string>(),
            RunId = result.GetValue(run),
            After = result.GetValue(after),
            Limit = result.GetValue(limit),
            Basic = options.Read(result),
        });
        return cmd;
    }

    internal sealed class Events : IRequestCommand
    {
        public IReadOnlyList<string> KindFields { get; init; } = Array.Empty<string>();
        public string? RunId { get; init; }
        public string? After { get; init; }
        public int? Limit { get; init; }
        public required BasicOptions Basic { get; init; }
        public IConnectionOptions Options => Basic;

        /// <summary>Each --kind value split on commas (empty components kept, as in the Swift, so
        /// they fail validation loudly) and whitespace-trimmed.</summary>
        private string[] RawKinds => KindFields
            .SelectMany(field => field.Split(',').Select(component => component.Trim()))
            .ToArray();

        private ControlEventKind[] NormalizedKinds => RawKinds
            .Select(ControlEventKinds.FromWire)
            .OfType<ControlEventKind>()
            .ToArray();

        public void Validate()
        {
            if ((RunId is null) != (After is null))
                throw new CliValidationException(ControlEventRequestError.CursorPair);
            if (RunId is { } runId && !Guid.TryParseExact(runId, "D", out _))
                throw new CliValidationException(ControlEventRequestError.InvalidRun);
            if (After is { } after && !ulong.TryParse(after, NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out _))
                throw new CliValidationException(ControlEventRequestError.InvalidCursor);
            if (Limit is { } limit && limit is < 1 or > 1_000)
                throw new CliValidationException(ControlEventRequestError.InvalidLimit);
            foreach (var raw in RawKinds)
            {
                if (ControlEventKinds.FromWire(raw) is null)
                    throw new CliValidationException(ControlEventRequestError.InvalidKind(raw));
            }
        }

        /// <summary>The port of makeInitialRequest — the first request the poll loop would send.</summary>
        public ControlRequest MakeRequest() => MakeState().MakeRequest();

        public EventStreamState MakeState()
        {
            ControlEventCursor? cursor = null;
            if (RunId is { } runId && Guid.TryParseExact(runId, "D", out var run)
                && After is { } afterText && ulong.TryParse(afterText, NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out var after))
            {
                cursor = new ControlEventCursor(run, after);
            }
            var normalized = NormalizedKinds;
            var kinds = normalized.Length == 0
                ? null
                : (IReadOnlySet<ControlEventKind>)new HashSet<ControlEventKind>(normalized);
            return new EventStreamState(cursor, kinds, Limit);
        }

        /// <summary>The Swift run() override: poll forever (Ctrl-C terminates the process). Each
        /// poll is one fresh connection via env.Send, per spec §11.</summary>
        public int Execute(CliEnvironment env)
        {
            var state = MakeState();
            while (true)
            {
                Poll(state, env);
            }
        }

        public void Poll(EventStreamState state, CliEnvironment env)
        {
            var response = env.Send(Options.PipeName(env.GetEnv), state.MakeRequest());
            var events = state.Consume(response);
            foreach (var @event in events)
            {
                env.Out.WriteLine(Basic.Json ? EventFormatter.Json(@event) : EventFormatter.Human(@event));
            }
            if (events.Length == 0)
                env.Sleep(TimeSpan.FromMilliseconds(250));
        }
    }
}
