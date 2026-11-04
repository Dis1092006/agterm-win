namespace AgTerm.Core.Model;

/// <summary>The outcome of resolving a command's --pane-id and --pane pair against a session.</summary>
public abstract record PaneAddress
{
    private PaneAddress()
    {
    }

    /// <summary>Pane is null when neither selector was given and the command's own default applies.</summary>
    public sealed record Resolved(StatusPane? Pane) : PaneAddress;

    public sealed record UnknownToken(string Token) : PaneAddress;
}
