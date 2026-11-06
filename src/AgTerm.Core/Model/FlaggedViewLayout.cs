namespace AgTerm.Core.Model;

/// <summary>How the flagged sidebar view lays out its sessions (upstream 902cd853 + f8c3eba3): a flat list
/// with "name : workspace" labels, a flat list with names only, or a workspace tree that lists only the
/// workspaces with a flagged session. App-wide; stored raw in AppSettings.flaggedViewLayout.</summary>
public enum FlaggedViewLayout
{
    Flat,
    Plain,
    Tree,
}

public static class FlaggedViewLayouts
{
    public static IReadOnlyList<FlaggedViewLayout> All { get; } =
        [FlaggedViewLayout.Flat, FlaggedViewLayout.Plain, FlaggedViewLayout.Tree];

    public static string RawValue(this FlaggedViewLayout layout) => layout switch
    {
        FlaggedViewLayout.Plain => "plain",
        FlaggedViewLayout.Tree => "tree",
        _ => "flat",
    };

    /// <summary>The Settings picker's label for the value.</summary>
    public static string DisplayName(this FlaggedViewLayout layout) => layout switch
    {
        FlaggedViewLayout.Plain => "Flat list, names only",
        FlaggedViewLayout.Tree => "Workspace tree",
        _ => "Flat list",
    };

    /// <summary>A known raw value matched exactly, as upstream's enum decode does; anything else is null.</summary>
    public static FlaggedViewLayout? FromRaw(string? raw) => raw switch
    {
        "flat" => FlaggedViewLayout.Flat,
        "plain" => FlaggedViewLayout.Plain,
        "tree" => FlaggedViewLayout.Tree,
        _ => null,
    };

    /// <summary>The settings with this layout stored: flat, the default, as absent (upstream
    /// SettingsModel.setFlaggedViewLayout).</summary>
    public static AppSettings WriteTo(this FlaggedViewLayout layout, AppSettings settings) =>
        settings with { FlaggedViewLayout = layout == FlaggedViewLayout.Flat ? null : layout.RawValue() };
}
