namespace AgTerm.Xterm;

public abstract record TerminalLayout(long Generation)
{
    public sealed record Empty(long Generation) : TerminalLayout(Generation);

    public sealed record Single(long Generation, string PaneId, string Title, string? WorkspaceName = null)
        : TerminalLayout(Generation);
}