namespace AgTerm.Core.Model;

/// <summary>A one-step move of the current workspace through the visible ones (wire workspace.go
/// --to), wrapping at both ends.</summary>
public enum WorkspaceNavigation { Next, Previous }

public static class WorkspaceNavigations
{
    /// <summary>Maps a control-channel direction string to a value, accepting both spellings of
    /// previous. Null for an unknown string.</summary>
    public static WorkspaceNavigation? FromWire(string? wire) => wire switch
    {
        "next" => WorkspaceNavigation.Next,
        "prev" or "previous" => WorkspaceNavigation.Previous,
        _ => null,
    };
}
