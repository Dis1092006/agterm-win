namespace AgTerm.Core.Model;

/// <summary>
/// A relative step through the flattened session list for keyboard navigation. Next/Previous step
/// one; First/Last jump to a tree end. NextAttention/PreviousAttention step through only the
/// sessions needing attention (status blocked or completed), wrapping around.
/// </summary>
public enum SessionNavigation { Next, Previous, First, Last, NextAttention, PreviousAttention }

public static class SessionNavigations
{
    /// <summary>Maps a control-channel direction string to a value. The CLI uses "prev"; both
    /// spellings are accepted. Null for an unknown string.</summary>
    public static SessionNavigation? FromWire(string wire) => wire switch
    {
        "next" => SessionNavigation.Next,
        "prev" or "previous" => SessionNavigation.Previous,
        "first" => SessionNavigation.First,
        "last" => SessionNavigation.Last,
        "next-attention" => SessionNavigation.NextAttention,
        "prev-attention" or "previous-attention" => SessionNavigation.PreviousAttention,
        _ => null,
    };
}
