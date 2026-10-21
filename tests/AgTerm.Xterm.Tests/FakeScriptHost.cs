using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

/// <summary>Records every host call and lets a test play the page's side of the protocol.</summary>
internal sealed class FakeScriptHost : IWebViewScriptHost
{
    public readonly List<(string Host, string Folder)> Mapped = new();
    public readonly List<string> Navigated = new();
    public readonly List<string> PostedJson = new();
    public readonly List<string> Scripts = new();

    /// <summary>Script → JSON-encoded result the fake returns (ExecuteScriptAsync semantics).
    /// Defaults to answering openPane with an 80×24 grid so page tests boot terminals freely.</summary>
    public Func<string, string> ScriptResult { get; set; } = script =>
        script.Contains("agterm.openPane") ? """{"cols":80,"rows":24}""" : "null";

    public Func<string, Task<string>>? ScriptTask { get; set; }

    public event Action<string>? JsonMessageReceived;

    public void MapVirtualHostToFolder(string hostName, string folderPath) =>
        Mapped.Add((hostName, folderPath));

    public void Navigate(string url) => Navigated.Add(url);

    public Task<string> ExecuteScriptAsync(string script)
    {
        Scripts.Add(script);
        return ScriptTask?.Invoke(script) ?? Task.FromResult(ScriptResult(script));
    }

    public void PostJson(string json) => PostedJson.Add(json);

    public void RaiseMessage(string json) => JsonMessageReceived?.Invoke(json);
}
