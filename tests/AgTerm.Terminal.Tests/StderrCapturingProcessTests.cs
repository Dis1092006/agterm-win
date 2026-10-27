using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using AgTerm.Core.Keymap;

namespace AgTerm.Terminal.Tests;

public class StderrCapturingProcessTests
{
    private static readonly string Cmd =
        Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";

    private static ProcessStartInfo CmdLine(string line)
    {
        var info = new ProcessStartInfo(Cmd) { Arguments = $"/d /c \"{line}\"" };
        return info;
    }

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    [Fact]
    public async Task The_exit_code_and_stderr_reach_the_caller()
    {
        var process = StderrCapturingProcess.Start(CmdLine("echo boom 1>&2 & exit /b 3"));

        var exit = await process.Exited.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(3, exit.ExitCode);
        Assert.Contains("boom", Text(exit.StderrTail));
    }

    [Fact]
    public async Task A_clean_exit_reports_code_zero()
    {
        var process = StderrCapturingProcess.Start(CmdLine("echo fine 1>&2"));

        var exit = await process.Exited.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(0, exit.ExitCode);
    }

    [Fact]
    public async Task Only_the_last_tail_limit_bytes_are_kept()
    {
        var line = new string('x', 62);
        var script = Path.Combine(Path.GetTempPath(), $"agterm-test-{Guid.NewGuid():N}.cmd");
        // 640 lines of 64 bytes with CRLF = 40 KiB; the numbered last line proves the tail end
        File.WriteAllText(script,
            "@echo off\r\nfor /l %%i in (1,1,639) do echo " + line + " 1>&2\r\necho last-line-marker 1>&2\r\n");
        try
        {
            var info = new ProcessStartInfo(Cmd) { Arguments = $"/d /c \"{script}\"" };
            var process = StderrCapturingProcess.Start(info);

            var exit = await process.Exited.WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Equal(CommandFailure.TailLimit, exit.StderrTail.Length);
            Assert.EndsWith("last-line-marker \r\n", Text(exit.StderrTail));
        }
        finally
        {
            File.Delete(script);
        }
    }

    [Fact]
    public async Task The_temp_file_is_removed_after_exit()
    {
        var before = StderrFiles();

        var process = StderrCapturingProcess.Start(CmdLine("echo gone 1>&2"));
        await process.Exited.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Empty(StderrFiles().Except(before));
    }

    [Fact]
    public async Task A_background_descendant_does_not_delay_the_exit()
    {
        var watch = Stopwatch.StartNew();

        var process = StderrCapturingProcess.Start(CmdLine("start /b ping -n 6 127.0.0.1 >nul & exit /b 2"));
        var exit = await process.Exited.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(2, exit.ExitCode);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task Environment_and_working_directory_are_applied()
    {
        var directory = Directory.CreateTempSubdirectory("agterm-cwd-");
        try
        {
            var info = CmdLine("echo %AGT_PROBE% %CD% 1>&2");
            info.Environment["AGT_PROBE"] = "x1";
            info.WorkingDirectory = directory.FullName;

            var process = StderrCapturingProcess.Start(info);
            var exit = await process.Exited.WaitAsync(TimeSpan.FromSeconds(20));

            var text = Text(exit.StderrTail);
            Assert.Contains("x1", text);
            Assert.Contains(directory.FullName, text, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void A_missing_program_throws_Win32Exception_with_the_system_text()
    {
        var before = StderrFiles();

        var error = Assert.Throws<Win32Exception>(() =>
            StderrCapturingProcess.Start(new ProcessStartInfo(@"C:\no\such\agterm-program.exe")));

        Assert.Equal(new Win32Exception(2).Message, error.Message);
        Assert.Empty(StderrFiles().Except(before));
    }

    [Fact]
    public void An_uncreatable_stderr_file_throws_StderrCaptureUnavailableException_and_starts_nothing()
    {
        var original = StderrCapturingProcess.StderrDirectory;
        StderrCapturingProcess.StderrDirectory = () => Path.Join(Path.GetTempPath(), $"agterm-no-dir-{Guid.NewGuid():N}");
        try
        {
            Assert.Throws<StderrCaptureUnavailableException>(() =>
                StderrCapturingProcess.Start(CmdLine("echo never 1>&2")));
        }
        finally
        {
            StderrCapturingProcess.StderrDirectory = original;
        }
    }

    [Fact]
    public async Task A_failed_tail_read_still_reports_the_exit_code()
    {
        var original = StderrCapturingProcess.TailReader;
        StderrCapturingProcess.TailReader = _ => throw new IOException("scanner holds the file");
        try
        {
            var process = StderrCapturingProcess.Start(CmdLine("echo boom 1>&2 & exit /b 4"));

            var exit = await process.Exited.WaitAsync(TimeSpan.FromSeconds(20));

            Assert.Equal(4, exit.ExitCode);
            Assert.Empty(exit.StderrTail);
        }
        finally
        {
            StderrCapturingProcess.TailReader = original;
        }
    }

    private static string[] StderrFiles() =>
        Directory.GetFiles(Path.GetTempPath(), "agterm-cmd-*.stderr");
}
