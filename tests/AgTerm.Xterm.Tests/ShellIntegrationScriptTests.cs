using System.Text.RegularExpressions;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// The shipped integration scripts, checked as the contract they are: each one's cwd report has to
/// survive CwdReport.Parse, and each one has to stay a no-op outside agterm.
///
/// These are content assertions rather than a live shell run — spawning pwsh/cmd/bash from a unit
/// test would depend on which shells the machine has. The payload SHAPES are pinned here and the
/// end-to-end behaviour was verified by hand against a real Oh My Posh profile.
/// </summary>
public class ShellIntegrationScriptTests
{
    private static string ReadScript(string name)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);

        var path = Path.Combine(dir!, "src", "AgTerm.Xterm", "Integration", name);
        Assert.True(File.Exists(path), $"integration script missing: {path}");
        return File.ReadAllText(path);
    }

    [Theory]
    [InlineData("integration.ps1")]
    [InlineData("integration.cmd")]
    [InlineData("integration.bash")]
    public void Every_script_no_ops_outside_agterm(string name)
    {
        // Sourced outside agterm (a user's own shell, a --command spawn) the script must leave the
        // shell exactly as it found it, the same contract upstream's integration.sh keeps. The gate is
        // AGTERM_ENABLED: the quick terminal sources the integration too but carries no pane id.
        var script = ReadScript(name);

        Assert.Contains("AGTERM_ENABLED", script);
        Assert.DoesNotContain("AGTERM_PANE_ID", script);
    }

    [Theory]
    [InlineData("integration.ps1")]
    [InlineData("integration.cmd")]
    [InlineData("integration.bash")]
    public void No_script_emits_a_title_sequence(string name)
    {
        // Session.DisplayName prefers oscTitle over the cwd basename, so an OSC 0/1/2 title would
        // overwrite every sidebar row with a path. Upstream disables its shell integration's
        // auto-title for exactly this reason (shell-integration-features = no-title).
        var script = ReadScript(name);

        Assert.DoesNotMatch(new Regex(@"\]\s*[012]\s*;"), script);
    }

    [Fact]
    public void The_powershell_script_wraps_the_existing_prompt_instead_of_replacing_it()
    {
        var script = ReadScript("integration.ps1");

        // Capturing $function:prompt as a value is what makes the wrapper possible: resolving
        // `prompt` by name inside the wrapper would find the wrapper and recurse forever.
        Assert.Contains("$function:prompt", script);
        Assert.Contains("OriginalPrompt", script);

        // The report must go to the console, NOT into the returned string: PSReadLine measures the
        // returned prompt to place the cursor, and invisible bytes would corrupt line editing.
        Assert.Contains("[Console]::Write", script);
    }

    [Fact]
    public void The_powershell_script_wraps_the_readline_function_and_keeps_the_last_status()
    {
        var script = ReadScript("integration.ps1");

        // The same value capture as prompt: the wrapper calls the original, not itself.
        Assert.Contains("$function:PSConsoleHostReadLine", script);
        Assert.Contains("OriginalReadLine", script);

        // The page must stop rewriting keys once ReadLine returns, also on Ctrl+C or a throw.
        Assert.Matches(
            new Regex(@"finally\s*\{[^}]*agterm-readline=0", RegexOptions.Singleline), script);

        // Every statement resets $?, so the wrapper reads it before anything else.
        Assert.Matches(
            new Regex(@"function global:PSConsoleHostReadLine\s*\{\s*\[System\.Diagnostics\.DebuggerHidden\(\)\]\s*param\(\)\s*\$lastRunStatus\s*=\s*\$\?"),
            script);
    }

    [Fact]
    public void The_bash_script_sources_the_users_rc_that_rcfile_displaced()
    {
        var script = ReadScript("integration.bash");

        // --rcfile REPLACES ~/.bashrc rather than adding to it, so without this the shell would
        // silently lose whatever the user's rc sets up (PATH, aliases, completion).
        Assert.Contains("~/.bashrc", script);

        // PROMPT_COMMAND is appended to, never assigned: the user's rc may already own it.
        Assert.Contains("PROMPT_COMMAND", script);
    }

    [Fact]
    public void The_bash_script_reports_a_uri_the_parser_accepts()
    {
        var script = ReadScript("integration.bash");

        // Three slashes and a literal ':' are load-bearing. Measured alternatives that FAIL:
        //   file://C%3A%5Cx  -> the drive letter lands in the host position, parsed as null
        //   file:///C%3A/x   -> parses, but yields "/C:/x" with a leading slash
        Assert.Contains("file:///", script);

        // ':' must be in the unreserved set of the encoder, or the drive colon gets escaped. The
        // check targets the character class itself: the comments above it name the broken forms.
        Assert.Matches(new Regex(@"\[[^\]]*:\]\)"), script);

        // Round-trip the documented shape through the real parser.
        Assert.Equal(@"C:\git\x", CwdReport.Parse("file:///C:/git/x"));
        Assert.Equal(@"C:\a b", CwdReport.Parse("file:///C:/a%20b"));
    }

    [Fact]
    public void The_cmd_script_prefixes_the_existing_prompt_template()
    {
        var script = ReadScript("integration.cmd");

        // cmd has no prompt hook, but PROMPT is a template re-expanded on every redraw, so $P is
        // always the live directory — embedding the escape in the template IS the per-prompt hook.
        Assert.Contains("$E]9;9;", script);
        Assert.Contains("%PROMPT%", script);

        // An unset PROMPT must be seeded with cmd's built-in default, or the prefix would leave
        // the user with an invisible prompt.
        Assert.Contains("$P$G", script);
    }

    [Fact]
    public void The_cmd_payload_quotes_the_path_and_parses_back()
    {
        // cmd paths routinely contain spaces, so the payload is quoted; the parser strips the
        // quotes (the Windows Terminal documented form).
        Assert.Equal(@"C:\Program Files\x", CwdReport.Parse("\"C:\\Program Files\\x\""));
    }
}
