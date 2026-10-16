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
    private readonly Thread _uiThread;
    private readonly TaskCompletionSource<(Form, WebView2)> _started = new();
    private Form? _form;

    public WebView2Fixture()
    {
        _uiThread = new Thread(() =>
        {
            try
            {
                var form = new Form
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
                    var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
                        userDataFolder: Path.Combine(Path.GetTempPath(), "agterm-xterm-tests-wv2"));
                    await webView.EnsureCoreWebView2Async(env);
                    _started.TrySetResult((form, webView));
                };
                _form = form;
                Application.Run(form);
            }
            catch (Exception ex)
            {
                _started.TrySetException(ex);
            }
        });
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.IsBackground = true;
        _uiThread.Start();
    }

    /// <summary>A FRESH WebView2 control on the same hidden form (its own page instance) — one per
    /// realized surface, the way the app hosts one WebView2 per session pane.</summary>
    public async Task<WebView2> CreateWebViewAsync()
    {
        var (form, first) = await _started.Task.WaitAsync(TimeSpan.FromSeconds(60));
        var done = new TaskCompletionSource<WebView2>(TaskCreationOptions.RunContinuationsAsynchronously);
        form.BeginInvoke(async () =>
        {
            try
            {
                var webView = new WebView2 { Dock = DockStyle.Fill };
                form.Controls.Add(webView);
                await webView.EnsureCoreWebView2Async(first.CoreWebView2.Environment);
                done.TrySetResult(webView);
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
        });
        return await done.Task;
    }

    /// <summary>A fresh WebView2 with a booted XtermPage on it — the app's one-page-per-window
    /// shape. Panes for many surfaces open on this single page.</summary>
    public async Task<AgTerm.Xterm.XtermPage> CreatePageAsync()
    {
        var view = await CreateWebViewAsync();
        return await RunAsync(async _ =>
        {
            var page = new AgTerm.Xterm.XtermPage(new Win32ScriptHost(view.CoreWebView2));
            await page.InitializeAsync();
            return page;
        });
    }

    /// <summary>Run work on the WebView2 UI thread; the WebView2 is initialized when this returns.</summary>
    public async Task<T> RunAsync<T>(Func<WebView2, Task<T>> work)
    {
        var (form, webView) = await _started.Task.WaitAsync(TimeSpan.FromSeconds(60));
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        form.BeginInvoke(async () =>
        {
            try
            {
                done.TrySetResult(await work(webView));
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
        });
        return await done.Task;
    }

    /// <summary>Run work that has no result on the WebView2 UI thread.</summary>
    public Task RunAsync(Func<WebView2, Task> work) =>
        RunAsync(async view =>
        {
            await work(view);
            return true;
        });

    public void Dispose()
    {
        try
        {
            _form?.BeginInvoke(() => _form.Close());
            _uiThread.Join(TimeSpan.FromSeconds(10));
        }
        catch (Exception)
        {
            // teardown best-effort: the runner is exiting anyway
        }
    }
}

[CollectionDefinition("webview2")]
public class WebView2Collection : ICollectionFixture<WebView2Fixture>;
