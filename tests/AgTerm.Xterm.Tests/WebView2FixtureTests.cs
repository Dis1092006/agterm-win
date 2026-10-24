using System.Diagnostics;
using System.Windows.Forms;

namespace AgTerm.Xterm.Tests;

[Collection("webview2")]
public sealed class WebView2FixtureTests
{
    [Fact]
    public async Task Separate_fixtures_use_separate_profiles_and_browser_processes()
    {
        using var first = new WebView2Fixture();
        using var second = new WebView2Fixture();
        var firstIdentity = await first.RunAsync(view => Task.FromResult((
            view.CoreWebView2.Environment.UserDataFolder, view.CoreWebView2.BrowserProcessId)));
        var secondIdentity = await second.RunAsync(view => Task.FromResult((
            view.CoreWebView2.Environment.UserDataFolder, view.CoreWebView2.BrowserProcessId)));

        Assert.NotEqual(firstIdentity.UserDataFolder, secondIdentity.UserDataFolder);
        Assert.NotEqual(firstIdentity.BrowserProcessId, secondIdentity.BrowserProcessId);
    }

    [Fact]
    public async Task Disposal_removes_the_owned_profile_after_its_browser_exits()
    {
        using var fixture = new WebView2Fixture();
        var identity = await fixture.RunAsync(view => Task.FromResult((
            view.CoreWebView2.Environment.UserDataFolder, view.CoreWebView2.BrowserProcessId)));
        using var browser = Process.GetProcessById(checked((int)identity.BrowserProcessId));
        _ = browser.SafeHandle;

        await fixture.RunAsync(view =>
        {
            view.Dispose();
            return Task.CompletedTask;
        });
        await browser.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
        fixture.Dispose();

        Assert.True(SpinWait.SpinUntil(
            () => !Directory.Exists(identity.UserDataFolder), TimeSpan.FromSeconds(5)));
        Assert.True(browser.HasExited);
    }

    [Fact]
    public async Task An_async_Load_error_faults_startup_with_the_original_error()
    {
        var expected = new IOException("Injected asynchronous startup failure.");
        using var fixture = new WebView2Fixture(async view =>
        {
            ThreadExceptionEventHandler escaped = (_, args) =>
                Console.Error.WriteLine($"Escaped Load error: {args.Exception}");
            Application.ThreadException += escaped;
            view.FindForm()!.FormClosed += (_, _) => Application.ThreadException -= escaped;
            await Task.Yield();
            throw expected;
        });

        var actual = await Assert.ThrowsAsync<IOException>(
            () => fixture.CreateWebViewAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task A_startup_timeout_reports_the_stage_profile_and_thread()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = new WebView2Fixture(_ =>
        {
            entered.SetResult();
            return release.Task;
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var error = await Assert.ThrowsAsync<TimeoutException>(
                () => fixture.WaitForStartupAsync(TimeSpan.FromMilliseconds(100)));

            Assert.Contains("Form.Load", error.Message);
            Assert.Contains("agterm-xterm-tests-wv2-", error.Message);
            Assert.Contains("STA thread", error.Message);
            Assert.Contains("alive=True", error.Message);
        }
        finally
        {
            release.TrySetResult();
            await fixture.WaitForStartupAsync(TimeSpan.FromSeconds(60));
        }
    }

    [Fact]
    public async Task Exiting_the_UI_loop_before_initialization_faults_startup()
    {
        using var fixture = new WebView2Fixture(async view =>
        {
            await Task.Yield();
            view.FindForm()!.Close();
            await new TaskCompletionSource().Task;
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.CreateWebViewAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Contains("UI loop exited before startup completed", error.Message);
    }
}
