namespace AgTerm.Terminal.Tests;

/// <summary>
/// The default-shell policy (PLATFORM-NOTES: Windows has no login-shell semantics / $SHELL):
/// an explicitly configured shell wins verbatim; otherwise PowerShell 7 (pwsh.exe) when it is
/// on PATH, falling back to cmd (%COMSPEC%, or literal "cmd.exe" when even that is unset).
/// Probes are injected so the policy is unit-testable without touching the machine's PATH.
/// </summary>
public class ShellResolverTests
{
    private static string? NoWhich(string _) => null;

    [Fact]
    public void Configured_shell_wins_verbatim()
    {
        var shell = ShellResolver.Resolve(
            configured: "C:\\tools\\nushell\\nu.exe",
            which: _ => "C:\\Program Files\\PowerShell\\7\\pwsh.exe",
            env: _ => null);
        Assert.Equal("C:\\tools\\nushell\\nu.exe", shell);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_configured_shell_prefers_pwsh_from_path(string? configured)
    {
        var shell = ShellResolver.Resolve(
            configured,
            which: name => name == "pwsh.exe" ? "C:\\Program Files\\PowerShell\\7\\pwsh.exe" : null,
            env: _ => null);
        Assert.Equal("C:\\Program Files\\PowerShell\\7\\pwsh.exe", shell);
    }

    [Fact]
    public void Without_pwsh_falls_back_to_comspec()
    {
        var shell = ShellResolver.Resolve(
            configured: null,
            which: NoWhich,
            env: name => name == "COMSPEC" ? "C:\\Windows\\system32\\cmd.exe" : null);
        Assert.Equal("C:\\Windows\\system32\\cmd.exe", shell);
    }

    [Fact]
    public void Without_pwsh_and_comspec_falls_back_to_literal_cmd()
    {
        Assert.Equal("cmd.exe", ShellResolver.Resolve(null, NoWhich, _ => null));
    }
}
