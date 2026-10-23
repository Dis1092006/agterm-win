using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

// upstream SessionSwitcher's key monitor, on the terminal page: Ctrl+Tab and Ctrl+Shift+Tab are always
// the switcher's, Esc only while a switch is in progress, and releasing Ctrl ends the switch
[Collection("webview2")]
public sealed class LiveSessionSwitcherKeysTests
{
    private readonly WebView2Fixture _webView;

    public LiveSessionSwitcherKeysTests(WebView2Fixture webView)
    {
        _webView = webView;
    }

    [Fact]
    public async Task Ctrl_tab_and_ctrl_shift_tab_report_a_switch_step()
    {
        var (page, view, keys) = await BootAsync("switcher-tab");

        await KeyDownAsync(view, "switcher-tab", "Tab", ctrl: true);
        await KeyDownAsync(view, "switcher-tab", "Tab", ctrl: true, shift: true);
        await WaitUntilAsync(() => keys.Count == 2);

        Assert.Equal([SessionSwitcherKey.Tab, SessionSwitcherKey.ReverseTab], keys);
    }

    [Fact]
    public async Task Escape_is_the_switchers_only_during_a_switch()
    {
        var (page, view, keys) = await BootAsync("switcher-esc");

        await KeyDownAsync(view, "switcher-esc", "Escape");
        await KeyDownAsync(view, "switcher-esc", "Tab", ctrl: true);
        await KeyDownAsync(view, "switcher-esc", "Escape");
        await WaitUntilAsync(() => keys.Count == 2);

        Assert.Equal([SessionSwitcherKey.Tab, SessionSwitcherKey.Escape], keys);
    }

    [Fact]
    public async Task Releasing_ctrl_ends_the_switch_once()
    {
        var (page, view, keys) = await BootAsync("switcher-release");

        await KeyUpAsync(view, "Control");
        await KeyDownAsync(view, "switcher-release", "Tab", ctrl: true);
        await KeyUpAsync(view, "Control");
        await KeyUpAsync(view, "Control");
        await WaitUntilAsync(() => keys.Count >= 2);
        await Task.Delay(200);

        Assert.Equal([SessionSwitcherKey.Tab, SessionSwitcherKey.ControlReleased], keys);
    }

    [Fact]
    public async Task The_host_ending_the_switch_returns_escape_to_the_terminal()
    {
        var (page, view, keys) = await BootAsync("switcher-host");

        await KeyDownAsync(view, "switcher-host", "Tab", ctrl: true);
        await _webView.RunAsync(_ => page.SetSwitcherActiveAsync(false));
        await KeyDownAsync(view, "switcher-host", "Escape");
        await KeyUpAsync(view, "Control");
        await WaitUntilAsync(() => keys.Count >= 1);
        await Task.Delay(200);

        Assert.Equal([SessionSwitcherKey.Tab], keys);
    }

    private async Task<(XtermPage Page, Microsoft.Web.WebView2.WinForms.WebView2 View, EventRecorder<SessionSwitcherKey> Keys)> BootAsync(string paneId)
    {
        var view = await _webView.CreateWebViewAsync();
        var page = await _webView.RunAsync(async _ =>
        {
            var created = new XtermPage(new Win32ScriptHost(view.CoreWebView2));
            await created.InitializeAsync();
            return created;
        });
        await _webView.RunAsync(_ => page.OpenTerminalAsync(paneId));
        var keys = new EventRecorder<SessionSwitcherKey>();
        page.SwitcherKeyPressed += keys.Add;
        return (page, view, keys);
    }

    private Task KeyDownAsync(
        Microsoft.Web.WebView2.WinForms.WebView2 view, string paneId, string key, bool ctrl = false, bool shift = false) =>
        _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync(
            $"agterm.dispatchKey({XtermPage.PaneLiteral(paneId)}, {{key:{XtermPage.PaneLiteral(key)},ctrlKey:{ctrl.ToString().ToLowerInvariant()},shiftKey:{shift.ToString().ToLowerInvariant()}}})"));

    private Task KeyUpAsync(Microsoft.Web.WebView2.WinForms.WebView2 view, string key) =>
        _webView.RunAsync(async _ => await view.CoreWebView2.ExecuteScriptAsync(
            $"agterm.dispatchKeyUp({XtermPage.PaneLiteral(key)})"));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("Condition not met within the timeout.");
            await Task.Delay(25);
        }
    }
}
