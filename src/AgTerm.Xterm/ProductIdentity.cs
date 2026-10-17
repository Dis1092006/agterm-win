namespace AgTerm.Xterm;

/// <summary>
/// The name this port shows wherever a user sees the application: window titles, notices, the exe
/// and its version resource. It is agterm-win rather than upstream's agterm so the Windows port never
/// passes for the macOS app (PLATFORM-NOTES). Names on the wire stay upstream's: agtermctl, the
/// AGTERM_* variables and the built-in `agterm` theme are part of the parity contract.
/// </summary>
public static class ProductIdentity
{
    public const string Name = "agterm-win";

    /// <summary>The window caption: the product name alone, or "context — agterm-win".</summary>
    public static string WindowTitle(string? context) =>
        string.IsNullOrEmpty(context) ? Name : $"{context} — {Name}";
}
