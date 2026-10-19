using AgTerm.Core.Install;
using Microsoft.Win32;

namespace AgTerm.Xterm.Tests;

/// <summary>The registry adapter on a scratch key under HKCU\Software, never HKCU\Environment.</summary>
public sealed class WindowsUserPathEnvironmentTests : IDisposable
{
    private readonly ScratchRegistryKey _scratch = new();
    private int _broadcasts;

    public void Dispose() => _scratch.Dispose();

    private WindowsUserPathEnvironment Create() => new(_scratch.Open, () => _broadcasts++);

    private void Seed(string name, string value, RegistryValueKind kind)
    {
        using var key = _scratch.Open();
        key.SetValue(name, value, kind);
    }

    private (string? Value, RegistryValueKind Kind, string[] Names) Stored()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_scratch.Path)!;
        var names = key.GetValueNames();
        if (names.Length == 0)
            return (null, RegistryValueKind.Unknown, names);
        return ((string?)key.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames), key.GetValueKind("Path"), names);
    }

    [Fact]
    public void Variables_expand_from_the_environment_for_the_compare()
    {
        var profile = Environment.GetEnvironmentVariable("USERPROFILE")!;
        Assert.Equal(profile + @"\bin;%AGTERM_NO_SUCH_VARIABLE%", Create().ExpandVariables(@"%USERPROFILE%\bin;%AGTERM_NO_SUCH_VARIABLE%"));
    }

    [Fact]
    public void An_absent_value_reads_as_null_and_is_created_expandable()
    {
        var env = Create();
        Assert.Null(env.ReadUserPath());

        env.WriteUserPath(new UserPathValue(@"C:\Tools", Expandable: true));

        Assert.Equal((@"C:\Tools", RegistryValueKind.ExpandString), (Stored().Value, Stored().Kind));
    }

    [Fact]
    public void An_expandable_value_is_read_without_expanding_and_stays_expandable()
    {
        Seed("Path", @"%USERPROFILE%\bin;C:\x", RegistryValueKind.ExpandString);
        var env = Create();

        var read = env.ReadUserPath();
        Assert.Equal(new UserPathValue(@"%USERPROFILE%\bin;C:\x", Expandable: true), read);

        env.WriteUserPath(read! with { Value = read.Value + @";C:\Tools" });
        Assert.Equal((@"%USERPROFILE%\bin;C:\x;C:\Tools", RegistryValueKind.ExpandString), (Stored().Value, Stored().Kind));
    }

    [Fact]
    public void A_plain_value_stays_plain()
    {
        Seed("Path", @"C:\x", RegistryValueKind.String);
        var env = Create();

        var read = env.ReadUserPath();
        Assert.Equal(new UserPathValue(@"C:\x", Expandable: false), read);

        env.WriteUserPath(read! with { Value = @"C:\x;C:\Tools" });
        Assert.Equal((@"C:\x;C:\Tools", RegistryValueKind.String), (Stored().Value, Stored().Kind));
    }

    [Fact]
    public void A_value_named_in_other_case_is_found_and_not_duplicated()
    {
        Seed("PATH", @"C:\x", RegistryValueKind.ExpandString);
        var env = Create();

        Assert.Equal(@"C:\x", env.ReadUserPath()?.Value);
        env.WriteUserPath(new UserPathValue(@"C:\x;C:\Tools", Expandable: true));

        Assert.Single(Stored().Names);
        Assert.Equal(@"C:\x;C:\Tools", Stored().Value);
    }

    [Fact]
    public void A_value_that_is_not_a_string_fails_the_read()
    {
        using (var key = _scratch.Open())
            key.SetValue("Path", 1, RegistryValueKind.DWord);

        Assert.Throws<InvalidOperationException>(() => Create().ReadUserPath());
    }

    [Fact]
    public void The_install_writes_once_and_broadcasts_once()
    {
        Seed("Path", @"%USERPROFILE%\bin", RegistryValueKind.ExpandString);
        var env = new RecordingProcessPath(Create());

        var first = CliInstall.Install(@"C:\Program Files\agterm-win", toolExists: true, env);
        var second = CliInstall.Install(@"C:\Program Files\agterm-win", toolExists: true, env);

        Assert.Equal("Command Line Tool Installed", first.Title);
        Assert.Equal("Command Line Tool Installed", second.Title);
        Assert.Equal((@"%USERPROFILE%\bin;C:\Program Files\agterm-win", RegistryValueKind.ExpandString), (Stored().Value, Stored().Kind));
        Assert.Equal(1, _broadcasts);
    }

    /// <summary>Keeps the process PATH of the test run out of the install.</summary>
    private sealed class RecordingProcessPath(IUserPathEnvironment inner) : IUserPathEnvironment
    {
        private string? _path = @"C:\Windows";
        public UserPathValue? ReadUserPath() => inner.ReadUserPath();
        public void WriteUserPath(UserPathValue value) => inner.WriteUserPath(value);
        public void BroadcastChange() => inner.BroadcastChange();
        public string? ReadProcessPath() => _path;
        public void WriteProcessPath(string value) => _path = value;
        public string ExpandVariables(string value) => inner.ExpandVariables(value);
    }
}
