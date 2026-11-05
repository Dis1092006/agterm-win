namespace AgTerm.Xterm;

/// <summary>The banners for hooks.conf: the lines a reload skipped, and a hook that failed
/// (upstream notifyHooksDiagnostics and notifyHookFailure).</summary>
public static class HooksNotices
{
    public static LiveFailureNotice Diagnostics(int count) => new(
        "Hooks have issues",
        count == 1
            ? "1 line in hooks.conf was skipped. Run agtermctl hooks list for details."
            : $"{count} lines in hooks.conf were skipped. Run agtermctl hooks list for details.");

    public static LiveFailureNotice Failure(string kind, string command, string detail) => new(
        $"Hook for {kind} failed",
        $"{command}: {detail}");
}
