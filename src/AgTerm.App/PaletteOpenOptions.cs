namespace AgTerm.App;

public sealed record PaletteOpenOptions
{
    public bool PreserveInputOrderForEmptyQuery { get; init; }

    /// <summary>Caller-supplied pick rows match on the label only, so text in a subtitle cannot
    /// filter a safe row out and leave a destructive one selected (upstream #339).</summary>
    public bool MatchTitleOnly { get; init; }

    public string InitialQuery { get; init; } = string.Empty;

    public string? InitialSelectionId { get; init; }

    public bool AllowCustomAnswer { get; init; }

    public bool Dimmed { get; init; } = true;

    /// <summary>A membership editor runs a row in place: the palette stays open with its query, order and
    /// highlight (upstream keepsOpenOnRun, a2552826).</summary>
    public bool KeepsOpenOnAccept { get; init; }

    /// <summary>A footer line under the rows; null shows none.</summary>
    public string? Hint { get; init; }
}
