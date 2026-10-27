using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AgTerm.Core.Install;
using AgTerm.Core.Model;

namespace AgTerm.Cli.Tests;

/// <summary>
/// The agent skill in plugins/agterm/skills/agterm is a contract surface: an agent runs what it says.
/// These tests catch drift between the skill text and the CLI, the parity surface and the session
/// environment.
/// </summary>
public class SkillTextTests
{
    private static readonly string[] SkillFiles = ["SKILL.md", "reference.md", "examples.md", "troubleshooting.md"];

    [Fact]
    public void Skill_frontmatter_names_the_skill_agterm_and_the_marker_is_present()
    {
        var text = ReadSkill("SKILL.md");
        var lines = text.ReplaceLineEndings("\n").Split('\n');

        Assert.Equal("---", lines[0]);
        var end = Array.IndexOf(lines, "---", 1);
        Assert.True(end > 1, "SKILL.md frontmatter has no closing ---");

        var fields = ParseFrontmatter(lines[1..end]);
        Assert.Equal("agterm", fields["name"]);
        Assert.False(string.IsNullOrWhiteSpace(fields["description"]));
        Assert.Equal("Bash(agtermctl *)", fields["allowed-tools"]);

        Assert.Contains("<!-- agterm-skill -->", string.Join('\n', lines[(end + 1)..]));
    }

    // Tokens that only make sense on macOS. Each one was in the upstream text and has no correct use on Windows.
    private static readonly (string Token, string Pattern)[] MacOnlyTokens =
    [
        ("~/Library", @"~/Library"),
        ("log show", @"\blog show\b"),
        ("brew ", @"\bbrew "),
        ("Homebrew", @"(?i)homebrew"),
        ("/opt/homebrew", @"/opt/homebrew"),
        (".sh", @"\.sh\b"),
        ("pbcopy", @"\bpbcopy\b"),
        ("pbpaste", @"\bpbpaste\b"),
        ("open -a", @"\bopen -a\b"),
        ("unix socket", @"(?i)\bunix socket"),
        ("Finder", @"\bFinder\b"),
        ("NSPasteboard", @"\bNSPasteboard\b"),
        ("NSWindow", @"\bNSWindow\b"),
        ("osascript", @"\bosascript\b"),
        ("launchd", @"\blaunchd\b"),
        ("/Applications", @"/Applications\b"),
        ("zsh -lc", @"\bzsh -lc\b"),
        ("ps eww", @"\bps eww\b"),
        ("Command-", @"\bCommand-"),
        ("⌘", "⌘"),
        ("⌥", "⌥"),
        ("⌃", "⌃"),
        ("⇧", "⇧"),
    ];

    [Fact]
    public void Skill_has_no_macos_only_tokens()
    {
        var hits = new List<string>();
        foreach (var file in SkillFiles)
        {
            var lines = ReadSkill(file).ReplaceLineEndings("\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var (token, pattern) in MacOnlyTokens)
                {
                    if (Regex.IsMatch(lines[i], pattern)) hits.Add($"{file}:{i + 1}: '{token}'");
                }
            }
        }
        Assert.True(hits.Count == 0, "macOS-only tokens:\n" + string.Join('\n', hits));
    }

    [Fact]
    public void Skill_names_exactly_the_agterm_variables_a_spawned_shell_can_get()
    {
        // Every input set, so each conditional name is emitted.
        var emitted = SurfaceEnvironment
            .Session(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), @"C:\state\agterm.sock", StatusPane.Right, "token")
            .Keys
            .Concat(SurfaceEnvironment.QuickTerminal(Guid.NewGuid(), @"C:\state\agterm.sock").Keys)
            .ToHashSet();

        var named = SkillFiles
            .SelectMany(file => Regex.Matches(ReadSkill(file), @"\bAGTERM_[A-Z0-9_]*[A-Z0-9]\b").Select(m => m.Value))
            .ToHashSet();

        Assert.Empty(named.Except(emitted).Order());
        Assert.Empty(emitted.Except(named).Order());
    }

    // docs/agterm-windows-spec.md marks debug.appearance as outside the protocol: a UI-test seam with no
    // agtermctl subcommand.
    private static readonly HashSet<string> OutOfParity = ["debug.appearance"];

    [Fact]
    public void Reference_names_every_wire_command_in_parity()
    {
        var root = FindRepoRoot();
        var surface = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "parity", "surface.json")))!;
        var adopted = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "parity", "adopted.json")))!;
        var wire = surface["control"]!["commands"]!.AsArray()
            .Concat(adopted["commands"]!.AsArray())
            .Select(c => (string)c!["wire"]!)
            .Where(w => !OutOfParity.Contains(w))
            .ToList();
        Assert.Equal(76, wire.Count);

        var reference = ReadSkill("reference.md");
        var missing = wire.Where(w => !reference.Contains($"`{w}`", StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0, "reference.md does not name: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_agtermctl_line_in_a_code_block_parses()
    {
        var failures = new List<string>();
        var checkedCount = 0;
        foreach (var file in SkillFiles)
        {
            foreach (var (line, text) in CodeBlockLines(ReadSkill(file)))
            {
                foreach (var argv in AgtermctlInvocations(text))
                {
                    checkedCount++;
                    var error = ParseError(argv);
                    if (error is not null) failures.Add($"{file}:{line}: agtermctl {string.Join(' ', argv)}\n    {error}");
                }
            }
        }
        Assert.True(checkedCount > 50, $"only {checkedCount} agtermctl invocations found; the extractor is broken");
        Assert.True(failures.Count == 0, $"{failures.Count} invocations do not parse:\n" + string.Join('\n', failures));
    }

    [Fact]
    public void Installer_items_named_in_the_skill_are_the_apps_menu_items()
    {
        var named = SkillFiles
            .SelectMany(file => Regex.Matches(ReadSkill(file), @"Help ▸ (Install [^…\n]*…)").Select(m => m.Groups[1].Value))
            .ToHashSet();

        Assert.Contains(InstallerCommands.InstallCommandLineTool, named);
        Assert.Empty(named.Except([InstallerCommands.InstallCommandLineTool, InstallerCommands.InstallAgentSkill]).Order());
    }

    // Section 5: the plugin channel. Each marketplace lists one plugin, agterm, served from plugins/agterm, and
    // each plugin manifest points at the skill this file tests.

    [Fact]
    public void Claude_marketplace_lists_the_agterm_plugin_from_plugins_agterm()
    {
        var marketplace = ReadJson(".claude-plugin", "marketplace.json");
        Assert.Equal("agterm-win", (string?)marketplace["name"]);

        var plugin = Assert.Single(marketplace["plugins"]!.AsArray())!;
        Assert.Equal("agterm", (string?)plugin["name"]);
        Assert.Equal("./plugins/agterm", (string?)plugin["source"]);
        Assert.Equal(PluginDirectory(), Resolve(FindRepoRoot(), (string)plugin["source"]!));
    }

    [Fact]
    public void Codex_marketplace_lists_the_agterm_plugin_from_plugins_agterm()
    {
        var marketplace = ReadJson(".agents", "plugins", "marketplace.json");
        Assert.Equal("agterm-win", (string?)marketplace["name"]);

        var plugin = Assert.Single(marketplace["plugins"]!.AsArray())!;
        Assert.Equal("agterm", (string?)plugin["name"]);
        Assert.Equal("local", (string?)plugin["source"]!["source"]);
        Assert.Equal("./plugins/agterm", (string?)plugin["source"]!["path"]);
        Assert.Equal(PluginDirectory(), Resolve(FindRepoRoot(), (string)plugin["source"]!["path"]!));
    }

    [Fact]
    public void Claude_plugin_manifest_points_at_the_skill_directory()
    {
        var manifest = ReadJson("plugins", "agterm", ".claude-plugin", "plugin.json");
        Assert.Equal("agterm", (string?)manifest["name"]);

        var skill = Assert.Single(manifest["skills"]!.AsArray())!;
        Assert.Equal(SkillDirectory(), Resolve(PluginDirectory(), (string)skill!));
        Assert.True(File.Exists(Path.Combine(SkillDirectory(), "SKILL.md")));
    }

    [Fact]
    public void Codex_plugin_manifest_points_at_the_skills_directory_that_holds_the_skill()
    {
        var manifest = ReadJson("plugins", "agterm", ".codex-plugin", "plugin.json");
        Assert.Equal("agterm", (string?)manifest["name"]);

        var skills = Resolve(PluginDirectory(), (string)manifest["skills"]!);
        Assert.Equal(Path.GetDirectoryName(SkillDirectory()), skills);
        Assert.True(File.Exists(Path.Combine(skills, "agterm", "SKILL.md")));
    }

    [Fact]
    public void App_version_is_three_numbers()
    {
        var props = System.Xml.Linq.XDocument.Load(Path.Combine(FindRepoRoot(), "Directory.Build.props"));
        Assert.Matches(@"^\d+\.\d+\.\d+$", Assert.Single(props.Descendants("Version")).Value);
    }

    // Claude Code takes the plugin version from the manifest first and does not update an installed plugin while
    // that string stays the same. A person raises it by hand when the skill changes, apart from the app version,
    // so a release that leaves the skill alone does not update the plugin.
    [Fact]
    public void Plugin_manifests_carry_one_version()
    {
        var version = (string?)ReadJson("plugins", "agterm", ".claude-plugin", "plugin.json")["version"];
        Assert.Matches(@"^\d+\.\d+\.\d+$", version);

        Assert.Equal(version, (string?)ReadJson("plugins", "agterm", ".codex-plugin", "plugin.json")["version"]);
        var entry = Assert.Single(ReadJson(".claude-plugin", "marketplace.json")["plugins"]!.AsArray())!;
        Assert.Equal(version, (string?)entry["version"]);
    }

    private static JsonNode ReadJson(params string[] path)
    {
        var file = Path.Combine([FindRepoRoot(), .. path]);
        Assert.True(File.Exists(file), $"{string.Join('/', path)} does not exist");
        return JsonNode.Parse(File.ReadAllText(file))!;
    }

    /// <summary>A manifest path: relative, starting with ./ as both agents require, and naming an existing
    /// directory under <paramref name="baseDirectory"/>.</summary>
    private static string Resolve(string baseDirectory, string relative)
    {
        Assert.StartsWith("./", relative);
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(baseDirectory, relative)));
        Assert.True(Directory.Exists(full), $"{relative} does not name a directory under {baseDirectory}");
        return full;
    }

    private static string PluginDirectory() => Path.Combine(FindRepoRoot(), "plugins", "agterm");

    private static string? ParseError(string[] argv)
    {
        // agtermctl reads --file during validation, so the sample path must exist.
        argv = argv.Select((word, i) => i > 0 && argv[i - 1] == "--file" ? SampleFile.Value : word).ToArray();
        if (argv.Contains("--help"))
        {
            var output = new StringWriter();
            var exit = Agtermctl.Run(argv, new CliEnvironment { Out = output, Error = output });
            return exit == 0 ? null : output.ToString().Trim();
        }
        try
        {
            Agtermctl.ParseAsRoot(argv);
            return null;
        }
        catch (Exception e) when (e is CliParseException or CliValidationException)
        {
            return e.Message;
        }
    }

    /// <summary>The lines inside fenced code blocks, with backslash continuations joined and shell comments
    /// dropped. The line number is where the logical line starts.</summary>
    private static IEnumerable<(int Line, string Text)> CodeBlockLines(string markdown)
    {
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        var inBlock = false;
        string? heredoc = null;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inBlock = !inBlock;
                heredoc = null;
                continue;
            }
            if (!inBlock) continue;
            if (heredoc is not null)
            {
                if (lines[i].Trim() == heredoc) heredoc = null;
                continue;
            }
            var start = i;
            var text = lines[i];
            while (text.EndsWith('\\') && i + 1 < lines.Length)
            {
                text = text[..^1] + " " + lines[++i].Trim();
            }
            text = StripComment(text);
            var marker = Regex.Match(text, @"<<-?\s*['""]?(\w+)['""]?");
            if (marker.Success) heredoc = marker.Groups[1].Value;
            yield return (start + 1, text);
        }
    }

    private static string StripComment(string text)
    {
        char? quote = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote is not null)
            {
                if (c == '\\' && quote == '"') i++;
                else if (c == quote) quote = null;
            }
            else if (c is '\'' or '"') quote = c;
            else if (c == '\\') i++;
            else if (c == '#' && (i == 0 || char.IsWhiteSpace(text[i - 1]))) return text[..i];
        }
        return text;
    }

    /// <summary>Every agtermctl command in a shell line, as argv. A command ends at an unquoted |, ;, &amp;,
    /// a redirection, the ) that closes a $(...) it sits in, or a quote it was nested in. Shell
    /// expansions, {AGT_X} tokens and &lt;placeholder&gt; words become sample values.</summary>
    private static IEnumerable<string[]> AgtermctlInvocations(string text)
    {
        text = Regex.Replace(text, @"<[A-Za-z][A-Za-z0-9_|.-]*>", "SAMPLE");
        foreach (Match m in Regex.Matches(text, @"(?<![\w./\\-])agtermctl(?=\s)"))
        {
            var words = Words(text, m.Index + m.Length);
            if (words.Count > 0) yield return words.ToArray();
        }
    }

    private static List<string> Words(string text, int i)
    {
        var words = new List<string>();
        var previous = "";
        while (true)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            if (i >= text.Length || "|;&)<>`".Contains(text[i])) break;
            if (text[i] == '2' && i + 1 < text.Length && text[i + 1] == '>') break;
            var word = new System.Text.StringBuilder();
            var expanded = false;
            var closed = true;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && !"|;&)<>`".Contains(text[i]))
            {
                var c = text[i];
                if (c is '\'' or '"')
                {
                    var end = ClosingQuote(text, i);
                    if (end < 0)
                    {
                        closed = false;
                        break;
                    }
                    var body = text[(i + 1)..end];
                    if (c == '"')
                    {
                        if (body.Contains('$')) expanded = true;
                        body = body.Replace("\\\"", "\"").Replace("\\$", "$").Replace("\\\\", "\\");
                    }
                    word.Append(body);
                    i = end + 1;
                }
                else if (c == '$')
                {
                    expanded = true;
                    i = SkipExpansion(text, i);
                }
                else
                {
                    word.Append(c);
                    i++;
                }
            }
            if (!closed) break;
            var value = word.ToString();
            if (expanded || value.Contains("{AGT_")) value = Sample(previous);
            words.Add(value);
            previous = value;
        }
        return words;
    }

    private static int ClosingQuote(string text, int open)
    {
        var quote = text[open];
        var depth = 0;
        for (var i = open + 1; i < text.Length; i++)
        {
            if (quote == '"' && text[i] == '\\') { i++; continue; }
            if (quote == '"' && text[i] == '$' && i + 1 < text.Length && text[i + 1] == '(') { depth++; i++; continue; }
            if (depth > 0 && text[i] == ')') { depth--; continue; }
            if (depth == 0 && text[i] == quote) return i;
        }
        return -1;
    }

    // $VAR, ${VAR}, $(...) with nesting.
    private static int SkipExpansion(string text, int i)
    {
        i++;
        if (i < text.Length && (text[i] == '(' || text[i] == '{'))
        {
            var open = text[i];
            var close = open == '(' ? ')' : '}';
            var depth = 0;
            for (; i < text.Length; i++)
            {
                if (text[i] == open) depth++;
                else if (text[i] == close && --depth == 0) return i + 1;
            }
            return i;
        }
        while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or ':')) i++;
        return i;
    }

    private static readonly Lazy<string> SampleFile = new(() =>
    {
        var path = Path.Combine(Path.GetTempPath(), "agterm-skill-sample.md");
        File.WriteAllText(path, "sample message\n");
        return path;
    });

    // A value that every option accepting an expansion in the skill text validates.
    private static string Sample(string option) => option switch
    {
        "--split-ratio" or "--opacity" => "0.5",
        "--grow-left" or "--grow-right" => "0.05",
        "--color" or "--background-color" or "--text-color" => "#336699",
        "--pane" => "left",
        _ => "10",
    };

    // The frontmatter uses top-level "key: value" pairs and "key: >" folded blocks indented by two spaces.
    private static Dictionary<string, string> ParseFrontmatter(string[] lines)
    {
        var fields = new Dictionary<string, string>();
        string? folding = null;
        foreach (var line in lines)
        {
            if (folding is not null && line.StartsWith("  ", StringComparison.Ordinal))
            {
                fields[folding] = (fields[folding] + " " + line.Trim()).Trim();
                continue;
            }
            folding = null;
            var colon = line.IndexOf(':');
            Assert.True(colon > 0 && !line.StartsWith(' '), $"frontmatter line is not 'key: value': {line}");
            var key = line[..colon];
            var value = line[(colon + 1)..].Trim();
            Assert.False(fields.ContainsKey(key), $"frontmatter key {key} appears twice");
            if (value == ">")
            {
                folding = key;
                fields[key] = "";
            }
            else
            {
                fields[key] = value;
            }
        }
        return fields;
    }

    private static string ReadSkill(string file) => File.ReadAllText(Path.Combine(SkillDirectory(), file));

    private static string SkillDirectory() => Path.Combine(FindRepoRoot(), "plugins", "agterm", "skills", "agterm");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AgTerm.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root with AgTerm.slnx not found");
    }
}
