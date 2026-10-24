using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// Hosts ONE real WebView2 (hidden off-screen WinForms window, dedicated STA thread with a message
/// loop) for the whole collection — the closest headless-ish stand-in for the app's WebView2 pane.
/// Tests marshal onto the UI thread via RunAsync and get a fresh navigation per test.
/// </summary>
public sealed class WebView2Fixture : IDisposable
{
    private static readonly TimeSpan StartupBudget = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan UiCallBudget = TimeSpan.FromSeconds(60);
    private static readonly ConcurrentDictionary<WebView2Fixture, byte> LiveFixtures = new();
    private readonly Thread _uiThread;
    // UI thread only
    private readonly List<WebView2> _testViews = new();
    private readonly TaskCompletionSource<(Form, WebView2)> _started =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _userDataFolder =
        Path.Combine(Path.GetTempPath(), $"agterm-xterm-tests-wv2-{Guid.NewGuid():N}");
    private string _startupStage = "Waiting for STA thread";
    private Form? _form;
    private Process? _browserProcess;
    private int _disposeRequested;

    public WebView2Fixture() : this(null) { }

    internal WebView2Fixture(Func<WebView2, Task>? beforeInitialize)
    {
        LiveFixtures.TryAdd(this, 0);
        _uiThread = new Thread(() =>
        {
            try
            {
                Volatile.Write(ref _startupStage, "STA thread entered");
                using var form = new Form
                {
                    StartPosition = FormStartPosition.Manual,
                    Location = new System.Drawing.Point(-32000, -32000),
                    ShowInTaskbar = false,
                    Width = 1000,
                    Height = 700,
                };
                var webView = new WebView2 { Dock = DockStyle.Fill };
                form.Controls.Add(webView);
                form.Load += async (_, _) =>
                {
                    try
                    {
                        Volatile.Write(ref _startupStage, "Form.Load entered");
                        if (beforeInitialize is not null)
                            await beforeInitialize(webView);
                        Volatile.Write(ref _startupStage, "Creating WebView2 environment");
                        var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
                            userDataFolder: _userDataFolder);
                        Volatile.Write(ref _startupStage, "Ensuring WebView2 controller");
                        await webView.EnsureCoreWebView2Async(env);
                        _browserProcess = Process.GetProcessById(checked((int)webView.CoreWebView2.BrowserProcessId));
                        _ = _browserProcess.SafeHandle;
                        Volatile.Write(ref _startupStage, "Ready");
                        _started.TrySetResult((form, webView));
                    }
                    catch (Exception ex)
                    {
                        _started.TrySetException(ex);
                    }
                };
                _ = form.Handle;
                Volatile.Write(ref _form, form);
                if (Volatile.Read(ref _disposeRequested) == 0)
                    Application.Run(form);
            }
            catch (Exception ex)
            {
                if (!_started.TrySetException(ex))
                    Log($"UI thread failed: {ex}");
            }
            finally
            {
                _started.TrySetException(new InvalidOperationException(
                    $"UI loop exited before startup completed. {StartupDiagnostics()}"));
                // Let the STA exit and release its COM references before waiting for the browser.
                _ = Task.Factory.StartNew(CleanupProfile, CancellationToken.None,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
        });
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.IsBackground = true;
        _uiThread.Start();
    }

    /// <summary>A FRESH WebView2 control on the same hidden form (its own page instance) — one per
    /// realized surface, the way the app hosts one WebView2 per session pane.</summary>
    public Task<WebView2> CreateWebViewAsync() => CreateWebViewAsync(UiCallBudget);

    internal Task<WebView2> CreateWebViewAsync(TimeSpan budget) =>
        InvokeAsync(async (form, first) =>
        {
            var webView = new WebView2 { Dock = DockStyle.Fill };
            form.Controls.Add(webView);
            _testViews.Add(webView);
            await webView.EnsureCoreWebView2Async(first.CoreWebView2.Environment);
            return webView;
        }, budget);

    /// <summary>Disposes the controls CreateWebViewAsync made. Each one has its own renderer process:
    /// a full run that kept them all ended with over 300 renderers.</summary>
    internal Task ReleaseTestViewsAsync()
    {
        if (!_started.Task.IsCompletedSuccessfully)
            return Task.CompletedTask;
        return InvokeAsync((_, _) =>
        {
            foreach (var view in _testViews)
                view.Dispose();
            _testViews.Clear();
            return Task.FromResult(true);
        }, UiCallBudget);
    }

    internal static void ReleaseTestViewsOfLiveFixtures()
    {
        foreach (var fixture in LiveFixtures.Keys)
            fixture.ReleaseTestViewsAsync().GetAwaiter().GetResult();
    }

    /// <summary>A fresh WebView2 with a booted XtermPage on it — the app's one-page-per-window
    /// shape. Panes for many surfaces open on this single page.</summary>
    public async Task<AgTerm.Xterm.XtermPage> CreatePageAsync()
    {
        var view = await CreateWebViewAsync().ConfigureAwait(false);
        return await RunAsync(async _ =>
        {
            var page = new AgTerm.Xterm.XtermPage(new Win32ScriptHost(view.CoreWebView2));
            await page.InitializeAsync();
            return page;
        }).ConfigureAwait(false);
    }

    /// <summary>Run work on the WebView2 UI thread; the WebView2 is initialized when this returns.</summary>
    public Task<T> RunAsync<T>(Func<WebView2, Task<T>> work) => RunAsync(work, UiCallBudget);

    internal Task<T> RunAsync<T>(Func<WebView2, Task<T>> work, TimeSpan budget) =>
        InvokeAsync((_, webView) => work(webView), budget);

    // a page answer that never comes otherwise hangs the whole testhost until the blame timeout,
    // and the hang dump names no test
    private async Task<T> InvokeAsync<T>(Func<Form, WebView2, Task<T>> work, TimeSpan budget)
    {
        var (form, webView) = await WaitForStartupAsync(StartupBudget).ConfigureAwait(false);
        var started = 0;
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        form.BeginInvoke(async () =>
        {
            Volatile.Write(ref started, 1);
            try
            {
                done.TrySetResult(await work(form, webView));
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
        });
        try
        {
            return await done.Task.WaitAsync(budget).ConfigureAwait(false);
        }
        catch (TimeoutException ex) when (!done.Task.IsCompleted)
        {
            var state = Volatile.Read(ref started) == 1
                ? "The work is running on the UI thread and has not finished"
                : "The UI thread has not started the work";
            throw new TimeoutException(
                $"WebView2 UI call did not finish within {budget}. {state}. {StartupDiagnostics()}", ex);
        }
    }

    /// <summary>Run work that has no result on the WebView2 UI thread.</summary>
    public Task RunAsync(Func<WebView2, Task> work) =>
        RunAsync(async view =>
        {
            await work(view);
            return true;
        });

    internal async Task<(Form, WebView2)> WaitForStartupAsync(TimeSpan budget)
    {
        try
        {
            return await _started.Task.WaitAsync(budget).ConfigureAwait(false);
        }
        catch (TimeoutException ex) when (!_started.Task.IsFaulted)
        {
            throw new TimeoutException($"WebView2 startup timed out. {StartupDiagnostics()}", ex);
        }
    }

    private string StartupDiagnostics() =>
        $"Stage: {Volatile.Read(ref _startupStage)}; profile: {_userDataFolder}; " +
        $"STA thread {_uiThread.ManagedThreadId}: alive={_uiThread.IsAlive}, state={_uiThread.ThreadState}.";

    private void Log(string message) => Console.Error.WriteLine($"WebView2 fixture: {message} {StartupDiagnostics()}");

    private void CleanupProfile()
    {
        try
        {
            if (!Directory.Exists(_userDataFolder))
                return;
            if (_browserProcess is null || !_browserProcess.WaitForExit(10_000))
            {
                Log("Keeping profile: browser exit has not been verified.");
                return;
            }
            Directory.Delete(_userDataFolder, recursive: true);
        }
        catch (Exception ex)
        {
            Log($"Profile cleanup failed: {ex}");
        }
        finally
        {
            _browserProcess?.Dispose();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeRequested, 1) != 0)
            return;
        LiveFixtures.TryRemove(this, out _);
        try
        {
            var form = Volatile.Read(ref _form);
            form?.BeginInvoke(() =>
            {
                try
                {
                    form.Close();
                }
                catch (Exception ex)
                {
                    Log($"Form.Close failed: {ex}");
                }
            });
        }
        catch (Exception ex)
        {
            Log($"Posting Form.Close failed: {ex}");
        }
        try
        {
            if (!_uiThread.Join(TimeSpan.FromSeconds(10)))
                Log("STA thread did not stop within the shutdown budget.");
        }
        catch (Exception ex)
        {
            Log($"Joining STA thread failed: {ex}");
        }
    }
}

[CollectionDefinition("webview2")]
[ReleaseWebViewsAfterTest]
public class WebView2Collection : ICollectionFixture<WebView2Fixture>;
