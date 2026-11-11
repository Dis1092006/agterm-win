using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

/// <summary>A scripted IHtmlPageView: it records what the page asks of it, and the test raises
/// the events a web view would.</summary>
internal sealed class FakeHtmlPageView : IHtmlPageView
{
    public bool? Javascript { get; private set; }
    public List<(string Host, string Folder)> Mapped { get; } = [];
    public List<string> DocumentScripts { get; } = [];
    public List<string> Calls { get; } = [];
    public bool Closed { get; private set; }

    public bool CanGoBack { get; set; }
    public bool CanGoForward { get; set; }
    public string? Source { get; set; } = "about:blank";
    public string? DocumentTitle { get; set; }

    public event Action<HtmlPageNavigationStarting>? NavigationStarting;
    public event Action? ContentLoading;
    public event Action<HtmlPageNavigationCompleted>? NavigationCompleted;
    public event Action? PageChanged;
    public event Action? ProcessFailed;
    public event Action? UserInput;

    public bool Subscribed => NavigationStarting is not null;

    public void Configure(bool javascript) => Javascript = javascript;

    public void MapVirtualHost(string host, string folder) => Mapped.Add((host, folder));

    public Task SetDocumentScriptAsync(string script)
    {
        DocumentScripts.Add(script);
        return Task.CompletedTask;
    }

    public void Navigate(string url) => Calls.Add("navigate " + url);

    public void NavigateToString(string html) => Calls.Add("text " + html);

    public void Reload() => Calls.Add("reload");

    public void GoBack() => Calls.Add("back");

    public void GoForward() => Calls.Add("forward");

    public void Stop() => Calls.Add("stop");

    public void Close() => Closed = true;

    /// <summary>Raises a navigation start and says whether the page cancelled it.</summary>
    public bool Start(string url, HtmlNavigationTarget target = HtmlNavigationTarget.MainFrame, bool user = false)
    {
        var args = new HtmlPageNavigationStarting(url, target, user);
        NavigationStarting?.Invoke(args);
        return args.Cancel;
    }

    public void Commit(string source, string? title)
    {
        Source = source;
        DocumentTitle = title;
        PageChanged?.Invoke();
        ContentLoading?.Invoke();
    }

    public void Complete(bool success = true, bool canceled = false, string? error = null) =>
        NavigationCompleted?.Invoke(new HtmlPageNavigationCompleted(success, canceled, error));

    /// <summary>A whole main-frame load: allowed start, commit and success.</summary>
    public void Load(string url, string source, string? title)
    {
        Assert.False(Start(url), $"{url} was cancelled");
        Commit(source, title);
        Complete();
    }

    public void Change() => PageChanged?.Invoke();

    public void Crash() => ProcessFailed?.Invoke();

    public void Input() => UserInput?.Invoke();
}

internal sealed class FakeHtmlBrowser : IHtmlBrowser
{
    public List<Uri> Prompts { get; } = [];
    public List<Action<bool>> Answers { get; } = [];
    public int Dismissals { get; private set; }
    public List<Uri> Opened { get; } = [];
    public List<string> Revealed { get; } = [];
    public bool Available { get; set; } = true;

    public Action? Confirm(Uri url, Action<bool> done)
    {
        Prompts.Add(url);
        Answers.Add(done);
        return () => Dismissals += 1;
    }

    public bool Open(Uri url)
    {
        if (!Available) return false;
        Opened.Add(url);
        return true;
    }

    public void Reveal(string path) => Revealed.Add(path);
}
