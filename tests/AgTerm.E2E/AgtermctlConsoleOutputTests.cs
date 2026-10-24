using System.Text;
using AgTerm.Terminal;
using Xunit;

namespace AgTerm.E2E;

/// <summary>
/// The built agtermctl.exe writing to a real console, here a pseudoconsole. A console takes
/// UTF-16 text, so no output code page may stand between agtermctl and the screen: with 437,
/// the default where the system locale is English, Cyrillic printed as "?". The emoji is in no
/// OEM code page, so the test also fails on a machine whose code page has Cyrillic (866).
/// </summary>
public class AgtermctlConsoleOutputTests : ControlApiTestCase
{
    private const string Text = "Сессия🙂";

    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Stdout_on_a_console_keeps_text_outside_the_code_page()
    {
        Assert.True(SendCommand($"{{\"cmd\":\"session.new\",\"args\":{{\"name\":\"{Text}\"}}}}")["ok"]!.GetValue<bool>());

        var output = await RunOnConsole("tree");

        Assert.Contains(Text, output);
    }

    [Fact]
    public async Task Stderr_on_a_console_keeps_text_outside_the_code_page()
    {
        var output = await RunOnConsole($"tree --{Text}");

        Assert.Contains($"--{Text}", output);
    }

    [Fact]
    public void Redirected_stdout_is_utf8()
    {
        Assert.True(SendCommand($"{{\"cmd\":\"session.new\",\"args\":{{\"name\":\"{Text}\"}}}}")["ok"]!.GetValue<bool>());

        // the human tree: --json escapes the emoji as 🙂 whatever the output encoding
        var (stdout, _) = RunRedirected("tree");

        Assert.Contains(Text, stdout);
    }

    [Fact]
    public void Redirected_stderr_is_utf8()
    {
        var (_, stderr) = RunRedirected($"tree --{Text}");

        Assert.Contains($"--{Text}", stderr);
    }

    [Fact]
    public void Redirected_output_has_no_byte_order_mark()
    {
        // a BOM would be the first character of every captured line and break JSON parsers
        var (stdout, _) = RunRedirected("tree --json");

        Assert.StartsWith("{", stdout);
    }

    private (string Out, string Error) RunRedirected(string arguments)
    {
        var exe = AgtermctlEndToEndTests.LocateAgtermctlExe();
        var info = new System.Diagnostics.ProcessStartInfo(exe, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            UseShellExecute = false,
        };
        info.Environment["AGTERM_CONTROL_SOCKET"] = Host.PipeName;
        using var process = System.Diagnostics.Process.Start(info)!;
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEnd();
        Assert.True(process.WaitForExit(30_000), "agtermctl.exe did not exit");
        return (stdout, stderr.Result);
    }

    private async Task<string> RunOnConsole(string arguments)
    {
        var exe = AgtermctlEndToEndTests.LocateAgtermctlExe();
        Assert.True(File.Exists(exe), $"agtermctl.exe not built at {exe} — run dotnet build first");

        var output = new List<byte>();
        using var pty = ConPtySession.Start(new ConPtyStartInfo
        {
            Argv = [exe, .. arguments.Split(' ')],
            Cols = 200,
            Rows = 30,
            ExtraEnvironment = new Dictionary<string, string> { ["AGTERM_CONTROL_SOCKET"] = Host.PipeName },
            OnOutput = chunk =>
            {
                lock (output) output.AddRange(chunk);
            },
        });
        await pty.Exited.WaitAsync(Budget);
        await pty.OutputCompleted.WaitAsync(Budget);
        lock (output) return Encoding.UTF8.GetString(output.ToArray());
    }
}
