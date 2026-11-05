using System.Text;

namespace AgTerm.Core.Model;

/// <summary>One styled run of a session info line. Host-free, so the app maps it to fonts and colors.</summary>
public sealed record SessionInfoSpan(string Text, bool Bold = false, bool Italic = false, bool Code = false,
                                     bool Dim = false, TintColor? Color = null)
{
    internal bool SameStyle(SessionInfoSpan other) =>
        Bold == other.Bold && Italic == other.Italic && Code == other.Code && Dim == other.Dim && Color == other.Color;
}

/// <summary>A session's caller-set sidebar info line (session.info, upstream 80f822bf): the source as sent,
/// whether it is markdown, its configured expiry in seconds (0 for none), and the spans the row draws.
/// Runtime-only: never saved, never copied by a duplicate.</summary>
public sealed class SessionInfo : IEquatable<SessionInfo>
{
    /// <summary>Caps the source in UTF-8 bytes; markup syntax counts toward it.</summary>
    public const int MaxSourceBytes = 512;

    private SessionInfo(string source, bool markdown, double hideAfter, IReadOnlyList<SessionInfoSpan> spans)
    {
        Source = source;
        Markdown = markdown;
        HideAfter = hideAfter;
        Spans = spans;
    }

    public string Source { get; }
    public bool Markdown { get; }
    public double HideAfter { get; }
    public IReadOnlyList<SessionInfoSpan> Spans { get; }

    /// <summary>Validates a set and parses its source. Null with the control response message when refused.</summary>
    public static SessionInfo? Make(string source, bool markdown, double hideAfter, out string? rejection)
    {
        ArgumentNullException.ThrowIfNull(source);
        rejection = null;
        if (Encoding.UTF8.GetByteCount(source) > MaxSourceBytes)
        {
            rejection = $"info text is over {MaxSourceBytes} bytes";
            return null;
        }
        if (!HudSpec.IsValidHideAfter(hideAfter))
        {
            rejection = $"--hide-after must be 0...{(int)HudSpec.MaxHideAfter} seconds";
            return null;
        }
        try
        {
            return new SessionInfo(source, markdown, hideAfter, SessionInfoMarkup.Spans(source, markdown));
        }
        catch (SessionInfoParseException error)
        {
            rejection = error.Error.Message;
            return null;
        }
    }

    // the spans follow from the other three, so they need no comparing
    public bool Equals(SessionInfo? other) =>
        other is not null
        && string.Equals(Source, other.Source, StringComparison.Ordinal)
        && Markdown == other.Markdown
        && HideAfter.Equals(other.HideAfter);

    public override bool Equals(object? obj) => Equals(obj as SessionInfo);

    public override int GetHashCode() => HashCode.Combine(Source, Markdown, HideAfter);

    public static bool operator ==(SessionInfo? left, SessionInfo? right) => Equals(left, right);

    public static bool operator !=(SessionInfo? left, SessionInfo? right) => !Equals(left, right);
}
