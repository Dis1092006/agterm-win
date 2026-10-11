using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.App;

/// <summary>Maps a pending pick into palette rows and options, and an accepted row back into the
/// pick outcome.</summary>
public static class PickPalette
{
    public static string Placeholder(PendingPick pick) => pick.Prompt ?? "Select\u2026";

    public static IReadOnlyList<PaletteItemViewModel> Items(PendingPick pick) =>
        pick.Items.Select((item, index) => new PaletteItemViewModel(item.Id, item.Label, item.Subtitle) { SourceIndex = index })
            .ToArray();

    public static PaletteOpenOptions Options(PendingPick pick) => new()
    {
        PreserveInputOrderForEmptyQuery = true,
        MatchTitleOnly = true,
        InitialQuery = pick.Query ?? string.Empty,
        InitialSelectionId = pick.Selection,
        AllowCustomAnswer = pick.AllowCustom,
    };

    public static ControlPickResult Outcome(PaletteItemViewModel row) => row.CustomAnswer is { } query
        ? ControlPickResult.Custom(query)
        : ControlPickResult.Picked(row.Id, row.Title, row.SourceIndex ?? 0);
}
