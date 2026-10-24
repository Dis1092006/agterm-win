using System.Reflection;
using Xunit.Sdk;

namespace AgTerm.Xterm.Tests;

/// <summary>Disposes the WebView2 controls a test created, once the test ends. On a collection
/// definition xunit applies it to each test of the collection; the webview2 collection runs one test
/// at a time, so no other test still uses the controls.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ReleaseWebViewsAfterTestAttribute : BeforeAfterTestAttribute
{
    public override void After(MethodInfo methodUnderTest) =>
        WebView2Fixture.ReleaseTestViewsOfLiveFixtures();
}
