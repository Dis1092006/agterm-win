using System.Text.RegularExpressions;
using AgTerm.App;

namespace AgTerm.Xterm.Tests;

public sealed class SingleInstanceKeyTests
{
    private static readonly string Directory = Path.Combine(Path.GetTempPath(), "agterm-single-instance-key");

    [Fact]
    public void One_state_directory_spelled_differently_gives_one_key()
    {
        var key = SingleInstanceKey.For(Directory);

        Assert.Equal(key, SingleInstanceKey.For(Directory.ToUpperInvariant()));
        Assert.Equal(key, SingleInstanceKey.For(Directory + Path.DirectorySeparatorChar));
        Assert.Equal(key, SingleInstanceKey.For(Directory.Replace('\\', '/')));
    }

    [Fact]
    public void Another_state_directory_gives_another_key()
    {
        Assert.NotEqual(SingleInstanceKey.For(Directory), SingleInstanceKey.For(Directory + "-other"));
    }

    [Fact]
    public void The_key_names_the_app()
    {
        Assert.Matches("^agterm-win-[0-9a-f]{24}$", SingleInstanceKey.For(Directory));
    }
}

public sealed class SingleInstanceSourceTests
{
    private static string Source(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { FindRepoRoot() }.Concat(path).ToArray()));

    [Fact]
    public void The_app_has_its_own_Main()
    {
        var project = Source("src", "AgTerm.App", "AgTerm.App.csproj");
        Assert.Contains("DISABLE_XAML_GENERATED_MAIN", project, StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_launch_on_the_same_state_directory_hands_its_activation_to_the_first()
    {
        var program = Source("src", "AgTerm.App", "Program.cs");
        Assert.Matches(new Regex(
            @"var instance = AppInstance\.FindOrRegisterForKey\(SingleInstanceKey\.For\(PersistenceStore\.DefaultDirectory\)\);\s*if \(!instance\.IsCurrent\)\s*\{(?:\s*//[^\n]*)*\s*RedirectActivationTo\(instance\);\s*return;\s*\}\s*instance\.Activated \+= \(_, activation\) => \(Application\.Current as App\)\?\.HandleActivation\(activation\);\s*Application\.Start\("),
            program);
        // only the process the user just started may hand the foreground to the running one
        Assert.Matches(new Regex(
            @"AllowSetForegroundWindow\(instance\.ProcessId\);[\s\S]*RedirectActivationToAsync\("),
            program);
    }

    [Fact]
    public void The_running_instance_raises_its_frontmost_window()
    {
        var app = Source("src", "AgTerm.App", "App.xaml.cs");
        Assert.Matches(new Regex(
            @"internal void ActivateFrontmostWindow\(\) =>\s*_dispatcherQueue\?\.TryEnqueue\(\(\) =>\s*\{\s*var window = _applicationHost\?\.FrontmostWindowId is \{ \} id && _windows\.TryGetValue\(id, out var frontmost\)\s*\? frontmost\s*: _windows\.Values\.FirstOrDefault\(\);\s*window\?\.ActivateWindow\(\);\s*\}\);"),
            app);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AgTerm.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
