using System.Diagnostics;
using System.Text;

namespace AgTerm.Xterm;

public sealed record TerminalOscReport(int Identifier, string Data, bool Focused, bool Oversized = false);

public enum NotificationPane { Main, Split }

public sealed record LiveNotificationTarget(Guid WindowId, Guid SessionId, NotificationPane Pane)
{
    private string Role => Pane == NotificationPane.Split ? "split" : "main";
    public string Identity => $"{WindowId:D}:{SessionId:D}:".ToUpperInvariant() + Role;
    public string ToastGroup => SessionId.ToString("D").ToUpperInvariant();
    public string ToastTag => WindowId.ToString("D").ToUpperInvariant() + ":" + Role;

    public static LiveNotificationTarget? Parse(string identity)
    {
        var parts = identity.Split(':');
        if (parts.Length != 3 || !Guid.TryParseExact(parts[0], "D", out var window)
            || !Guid.TryParseExact(parts[1], "D", out var session)
            || parts[2] is not ("main" or "split"))
            return null;
        return new(window, session, parts[2] == "split" ? NotificationPane.Split : NotificationPane.Main);
    }
}

public sealed record TerminalOscNotification(string Title, string Body)
{
    public const int MaximumPayloadBytes = 2047;

    public static TerminalOscNotification? Parse(int identifier, string payload, out string? error)
    {
        error = null;
        if (identifier is not (9 or 777))
            return null;
        if (Encoding.UTF8.GetByteCount(payload) > MaximumPayloadBytes)
        {
            error = "terminal notification payload exceeds 2047 UTF-8 bytes";
            return null;
        }

        string title;
        string body;
        if (identifier == 9)
        {
            if (IsConEmuCommand(payload))
                return null;
            title = "";
            body = payload;
        }
        else
        {
            if (payload is not "notify" && !payload.StartsWith("notify;", StringComparison.Ordinal))
                return null;
            var separator = payload.IndexOf(';', Math.Min(7, payload.Length));
            if (separator < 0)
            {
                error = "terminal OSC 777 notify is missing its title/body separator";
                return null;
            }
            title = payload[7..separator];
            body = payload[(separator + 1)..];
        }

        return new TerminalOscNotification(Sanitize(title, 63, multiline: false),
            Sanitize(body, 255, multiline: true));
    }

    public string EffectiveTitle(string sessionName) =>
        Title.Length == 0 ? Sanitize(sessionName, 63, multiline: false) : Title;

    private static bool IsConEmuCommand(string data)
    {
        if (data.Length == 0)
            return false;
        return data[0] switch
        {
            '1' when data.Length >= 2 => data[1] switch
            {
                ';' or '2' => true,
                '0' => data.Length == 2 ||
                    (data.Length >= 4 && data[2] == ';' && data[3] is >= '0' and <= '3'),
                '1' => data.Length >= 3 && data[2] == ';',
                _ => false,
            },
            '2' or '3' or '6' or '7' or '8' or '9' => data.Length >= 2 && data[1] == ';',
            '4' => data.Length >= 3 && data[1] == ';' && data[2] is >= '0' and <= '4',
            '5' => true,
            _ => false,
        };
    }

    private static string Sanitize(string value, int maximumBytes, bool multiline)
    {
        var result = new StringBuilder();
        var bytes = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if ((Rune.IsControl(rune) && !(multiline && rune.Value is '\n' or '\t')) ||
                rune.Value is 0x061c or 0x200e or 0x200f or >= 0x202a and <= 0x202e or >= 0x2066 and <= 0x2069)
                continue;
            if (bytes + rune.Utf8SequenceLength > maximumBytes)
                break;
            result.Append(rune.ToString());
            bytes += rune.Utf8SequenceLength;
        }
        return result.ToString();
    }
}

public sealed class TerminalNotificationLimiter
{
    private readonly Lock _gate = new();
    private readonly Func<TimeSpan> _clock;
    private TimeSpan? _lastTime;
    private string? _lastContent;

    public TerminalNotificationLimiter(Func<TimeSpan>? clock = null)
    {
        var start = Stopwatch.GetTimestamp();
        _clock = clock ?? (() => Stopwatch.GetElapsedTime(start));
    }

    public bool Allow(TerminalOscNotification notification)
    {
        lock (_gate)
        {
            var now = _clock();
            var content = notification.Title + notification.Body;
            if (_lastTime is { } last &&
                (now - last < TimeSpan.FromSeconds(1) ||
                 (content == _lastContent && now - last < TimeSpan.FromSeconds(5))))
                return false;
            _lastTime = now;
            _lastContent = content;
            return true;
        }
    }
}
