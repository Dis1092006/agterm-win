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
}
