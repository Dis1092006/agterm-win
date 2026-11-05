namespace AgTerm.Core.Model;

/// <summary>The pane part of a message address: a pane id or a role. Text is the 32-digit id the port's
/// pane tokens use, or the role's wire name.</summary>
public readonly record struct MessageAddressPane
{
    public Guid? PaneId { get; private init; }
    public StatusPane? PaneRole { get; private init; }

    public static MessageAddressPane Id(Guid id) => new() { PaneId = id };
    public static MessageAddressPane Role(StatusPane role) => new() { PaneRole = role };

    public string Text => PaneId is { } id ? id.ToString("N") : StatusPanes.Wire(PaneRole!.Value);
}

/// <summary>
/// A message bus SESSION:PANE address. A recipient may name the pane by role, resolved once at send time;
/// an identity must name the pane id, since a swap re-points every role.
/// </summary>
public sealed record MessageAddress(string Session, MessageAddressPane Pane)
{
    /// <summary>Parses an address a message is sent to: a pane id or a role.</summary>
    public static MessageAddress Recipient(string text) => Parse(text);

    /// <summary>Parses the address a caller acts as; a role is refused.</summary>
    public static MessageAddress Identity(string text)
    {
        var address = Parse(text);
        if (address.Pane.PaneId is null)
            throw new MessageAddressException($"identity \"{text}\" must name a pane id, not a role");
        return address;
    }

    /// <summary>The address a resolved pane is reported under: the uppercase session id and the pane token.</summary>
    public static string Canonical(Guid session, Guid pane) => $"{session.ToString("D").ToUpperInvariant()}:{pane:N}";

    /// <summary>A pane id in the token form AGTERM_PANE_ID carries or the dashed form, in any case.</summary>
    public static Guid? ParsePaneId(string text) =>
        Guid.TryParseExact(text, "N", out var id) || Guid.TryParseExact(text, "D", out id) ? id : null;

    private static MessageAddress Parse(string text)
    {
        var parts = text.Split(':');
        if (parts.Length == 1 && text.Length > 0)
            throw new MessageAddressException($"address \"{text}\" names a session, not a pane");
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0) throw Malformed(text);
        if (ParsePaneId(parts[1]) is { } id) return new MessageAddress(parts[0], MessageAddressPane.Id(id));
        return StatusPanes.FromControlName(parts[1]) is { } role
            ? new MessageAddress(parts[0], MessageAddressPane.Role(role))
            : throw Malformed(text);
    }

    private static MessageAddressException Malformed(string text) =>
        new($"invalid address \"{text}\": expected SESSION:PANE");
}

/// <summary>An address the bus refused; Message is the wire error text.</summary>
public sealed class MessageAddressException(string message) : Exception(message);
