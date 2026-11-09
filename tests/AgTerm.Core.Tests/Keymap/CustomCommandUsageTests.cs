using AgTerm.Core.Keymap;

namespace AgTerm.Core.Tests.Keymap;

/// <summary>Port of upstream CustomCommandUsageTests (68e17937), plus the title bar menu's grouping.</summary>
public sealed class CustomCommandUsageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "agterm-usage-" + Guid.NewGuid().ToString("N"));
    private readonly CustomCommand _finder = new("Finder", "open .", "cmd+shift+f");
    private readonly CustomCommand _lazygit = new("Lazygit", "lazygit", "");
    private readonly CustomCommand _cheatsheet = new("Cheatsheet", "less keys.md", "ctrl+a>k");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static string[] Names(IEnumerable<CustomCommand> commands) => commands.Select(c => c.Name).ToArray();

    [Fact]
    public void Record_counts_runs_by_name()
    {
        var usage = new CustomCommandUsage();
        usage.Record(_finder);
        usage.Record(_finder);
        usage.Record(_lazygit);

        Assert.Equal(new Dictionary<string, int> { ["Finder"] = 2, ["Lazygit"] = 1 }, usage.Counts);
    }

    [Fact]
    public void Most_used_orders_by_count_then_keymap_order_and_skips_never_run()
    {
        var usage = new CustomCommandUsage();
        usage.Record(_cheatsheet);
        usage.Record(_lazygit);
        usage.Record(_finder);
        usage.Record(_cheatsheet);
        CustomCommand[] commands = [_finder, _lazygit, _cheatsheet];

        Assert.Equal(["Cheatsheet", "Finder", "Lazygit"], Names(usage.MostUsed(commands, limit: 5)));
        Assert.Equal(["Cheatsheet", "Finder"], Names(usage.MostUsed(commands, limit: 2)));
        Assert.Equal(["Lazygit", "Finder"], Names(usage.MostUsed([_lazygit, _finder], limit: 5)));
        Assert.Empty(usage.MostUsed(commands, limit: 0));
        Assert.Empty(new CustomCommandUsage().MostUsed(commands, limit: 5));
    }

    [Fact]
    public void Most_used_ignores_counts_for_commands_no_longer_in_the_keymap()
    {
        var usage = new CustomCommandUsage { Counts = new() { ["Gone"] = 9, ["Zero"] = 0 } };
        usage.Record(_finder);
        var zero = new CustomCommand("Zero", "true", "");

        Assert.Equal(["Finder"], Names(usage.MostUsed([zero, _finder], limit: 5)));
    }

    [Fact]
    public void Store_round_trips_and_reads_missing_or_foreign_files_as_empty()
    {
        var file = Path.Combine(_dir, "custom-command-usage.json");
        var store = new CustomCommandUsageStore(_dir);
        Assert.Empty(store.Load().Counts);

        store.Record(_finder);
        store.Record(_finder);
        var loaded = store.Load();
        Assert.Equal(CustomCommandUsage.CurrentVersion, loaded.Version);
        Assert.Equal(new Dictionary<string, int> { ["Finder"] = 2 }, loaded.Counts);

        File.WriteAllText(file, $$"""{"counts":{"x":3},"version":{{CustomCommandUsage.CurrentVersion + 1}}}""");
        Assert.Empty(store.Load().Counts);

        File.WriteAllText(file, "not json");
        Assert.Empty(store.Load().Counts);
    }

    [Fact]
    public void Store_writes_the_upstream_file_shape()
    {
        new CustomCommandUsageStore(_dir).Record(_finder);

        var json = File.ReadAllText(Path.Combine(_dir, "custom-command-usage.json"));
        Assert.Equal("""{"counts":{"Finder":1},"version":1}""", json);
    }

    [Fact]
    public void Store_record_survives_an_unwritable_file()
    {
        var store = new CustomCommandUsageStore(_dir);
        store.Record(_finder);

        using (new FileStream(Path.Combine(_dir, "custom-command-usage.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            store.Record(_finder);

        Assert.Equal(1, store.Load().Counts["Finder"]);
    }

    [Fact]
    public void Menu_groups_leave_the_keymap_order_alone_up_to_the_limit()
    {
        var usage = new CustomCommandUsage();
        usage.Record(_cheatsheet);
        CustomCommand[] commands = [_finder, _lazygit, _cheatsheet];

        var groups = CustomCommandMenuGroups.From(commands, usage);

        Assert.Empty(groups.Leading);
        Assert.Equal(["Finder", "Lazygit", "Cheatsheet"], Names(groups.Rest));
    }

    [Fact]
    public void Menu_groups_lead_with_the_most_used_in_keymap_order_past_the_limit()
    {
        var commands = Enumerable.Range(1, 7).Select(i => new CustomCommand($"C{i}", "true", "")).ToArray();
        var usage = new CustomCommandUsage();
        foreach (var (index, runs) in new[] { (6, 3), (2, 2), (0, 1), (3, 1), (4, 1), (5, 1) })
            for (var i = 0; i < runs; i++)
                usage.Record(commands[index]);

        var groups = CustomCommandMenuGroups.From(commands, usage);

        Assert.Equal(5, CustomCommandMenuGroups.MostUsedLimit);
        Assert.Equal(["C1", "C3", "C4", "C5", "C7"], Names(groups.Leading));
        Assert.Equal(["C2", "C6"], Names(groups.Rest));
    }
}
