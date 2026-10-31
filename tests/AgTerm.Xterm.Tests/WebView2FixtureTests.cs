using System.Reflection;
using System.Diagnostics;
using System.Windows.Forms;

namespace AgTerm.Xterm.Tests;

[Collection("webview2")]
public sealed class WebView2FixtureTests
{
    private readonly WebView2Fixture _shared;

    public WebView2FixtureTests(WebView2Fixture shared) => _shared = shared;

    [Fact]
    public async Task Releasing_test_views_disposes_each_created_view_and_keeps_the_fixture_usable()
    {
        using var fixture = new WebView2Fixture();
        var first = await fixture.CreateWebViewAsync();
        var second = await fixture.CreateWebViewAsync();

        await fixture.ReleaseTestViewsAsync();

        var disposed = await fixture.RunAsync(_ => Task.FromResult(new[] { first.IsDisposed, second.IsDisposed }));
        Assert.Equal(new[] { true, true }, disposed);
        var third = await fixture.CreateWebViewAsync();
        Assert.False(await fixture.RunAsync(_ => Task.FromResult(third.IsDisposed)));
    }

    [Fact]
    public void Views_are_released_after_each_test_of_the_webview2_collection_only()
    {
        // xunit 2 ignores a BeforeAfterTestAttribute on the assembly; on the collection definition it
        // covers that collection's tests and none of the other collections running in parallel
        Assert.NotNull(typeof(WebView2Collection).GetCustomAttribute<ReleaseWebViewsAfterTestAttribute>());
        Assert.Null(typeof(WebView2Fixture).Assembly.GetCustomAttribute<ReleaseWebViewsAfterTestAttribute>());
    }

    [Fact]
    public async Task A_UI_call_whose_work_never_finishes_times_out_as_running()
    {
        var never = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var error = await Assert.ThrowsAsync<TimeoutException>(
                () => _shared.RunAsync(_ => never.Task, TimeSpan.FromMilliseconds(200)));

            Assert.Contains("The work is running on the UI thread", error.Message);
        }
        finally
        {
            never.TrySetResult(true);
        }
    }

    [Fact]
    public async Task A_UI_call_behind_a_blocked_UI_thread_times_out_as_not_started()
    {
        var release = new ManualResetEventSlim();
        var blocking = _shared.RunAsync(_ =>
        {
            release.Wait(TimeSpan.FromSeconds(30));
            return Task.FromResult(true);
        });
        try
        {
            var error = await Assert.ThrowsAsync<TimeoutException>(
                () => _shared.RunAsync(_ => Task.FromResult(true), TimeSpan.FromMilliseconds(200)));

            Assert.Contains("The UI thread has not started the work", error.Message);
        }
        finally
        {
            release.Set();
            await blocking;
        }
    }

    [Fact]
    public async Task Creating_a_web_view_times_out_behind_a_blocked_UI_thread()
    {
        var release = new ManualResetEventSlim();
        var blocking = _shared.RunAsync(_ =>
        {
            release.Wait(TimeSpan.FromSeconds(30));
            return Task.FromResult(true);
        });
        try
        {
            var error = await Assert.ThrowsAsync<TimeoutException>(
                () => _shared.CreateWebViewAsync(TimeSpan.FromMilliseconds(200)));

            Assert.Contains("The UI thread has not started the work", error.Message);
        }
        finally
        {
            release.Set();
            await blocking;
        }
    }

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
        // The browser exits in about 230 ms on an idle machine. When other work keeps every core
        // busy, it took 30 s to 227 s: its threads (and those of its child processes) run at base
        // priority 4 and get CPU time only when Windows boosts starved threads, and the processes
        // cannot finish exiting until those threads run. The exit is correct, only slow.
        await browser.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(300));
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
