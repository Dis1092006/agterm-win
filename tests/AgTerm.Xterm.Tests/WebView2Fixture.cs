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
