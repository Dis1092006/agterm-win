using AgTerm.Core.Install;

namespace AgTerm.Core.Tests.Install;

// The resume setting turns Codex's shared app-server off in ~\.codex\config.toml, so a plain codex
// runs its hooks in its own pane. Windows-only extension.
public class CodexFeatureConfigTests
{
    private const string Owned = "daemon_auto_start = false # agterm-win";

    [Fact]
    public void Enable_on_a_missing_file_writes_a_features_table()
    {
        Assert.Equal("[features]\n" + Owned + "\n", CodexFeatureConfig.Enable(null));
    }

    [Fact]
    public void Enable_appends_the_table_after_other_settings()
    {
        Assert.Equal("model = \"o3\"\n\n[features]\n" + Owned + "\n", CodexFeatureConfig.Enable("model = \"o3\"\n"));
    }

    [Fact]
    public void Enable_adds_the_line_under_an_existing_features_table()
    {
        Assert.Equal("[features]\n" + Owned + "\nhooks = true\n[tui]\n",
            CodexFeatureConfig.Enable("[features]\nhooks = true\n[tui]\n"));
    }

    [Fact]
    public void Enable_keeps_crlf_line_endings()
    {
        var result = CodexFeatureConfig.Enable("model = \"o3\"\r\n[features]\r\nhooks = true\r\n");

        Assert.Equal("model = \"o3\"\r\n[features]\r\n" + Owned + "\r\nhooks = true\r\n", result);
        Assert.DoesNotContain("\n", result!.Replace("\r\n", ""));
    }

    [Fact]
    public void Enable_ends_a_file_without_a_final_newline_before_the_table()
    {
        Assert.Equal("model = \"o3\"\n\n[features]\n" + Owned + "\n", CodexFeatureConfig.Enable("model = \"o3\""));
    }

    [Theory]
    [InlineData("[features]\ndaemon_auto_start = true\n")]
    [InlineData("[features]\ndaemon_auto_start=false\n")]
    [InlineData("features.daemon_auto_start = true\n")]
    public void Enable_leaves_a_user_value_alone(string toml)
    {
        Assert.Null(CodexFeatureConfig.Enable(toml));
    }

    [Fact]
    public void Enable_is_idempotent()
    {
        Assert.Null(CodexFeatureConfig.Enable(CodexFeatureConfig.Enable(null)));
    }

    [Theory]
    [InlineData(null, CodexDaemonSetting.Unset)]
    [InlineData("[tui]\n", CodexDaemonSetting.Unset)]
    [InlineData("[tui]\ndaemon_auto_start = true\n", CodexDaemonSetting.Unset)]
    [InlineData("[features]\n" + Owned + "\n", CodexDaemonSetting.Ours)]
    [InlineData("[features]\ndaemon_auto_start = true\n", CodexDaemonSetting.UserOn)]
    [InlineData("[features]\ndaemon_auto_start=false\n", CodexDaemonSetting.UserOff)]
    [InlineData("features.daemon_auto_start = true\n", CodexDaemonSetting.UserOn)]
    [InlineData("[tui]\nfeatures.daemon_auto_start = true\n", CodexDaemonSetting.Unset)]
    public void Inspect_reports_each_state(string? toml, CodexDaemonSetting expected)
    {
        Assert.Equal(expected, CodexFeatureConfig.Inspect(toml));
    }

    [Fact]
    public void Disable_removes_only_our_line()
    {
        Assert.Equal("[features]\nhooks = true\n", CodexFeatureConfig.Disable("[features]\n" + Owned + "\nhooks = true\n"));
    }

    [Fact]
    public void Disable_keeps_crlf_line_endings()
    {
        Assert.Equal("[features]\r\nhooks = true\r\n", CodexFeatureConfig.Disable("[features]\r\n" + Owned + "\r\nhooks = true\r\n"));
    }

    [Fact]
    public void Disable_leaves_a_user_value_alone()
    {
        Assert.Null(CodexFeatureConfig.Disable("[features]\ndaemon_auto_start = false\n"));
        Assert.Null(CodexFeatureConfig.Disable(null));
    }

    [Fact]
    public void An_inline_features_table_is_refused()
    {
        var error = Assert.Throws<InvalidDataException>(() => CodexFeatureConfig.Enable("features = { hooks = true }\n"));

        Assert.Equal("config.toml sets features inline, so agterm-win left it unchanged.", error.Message);
    }
}
