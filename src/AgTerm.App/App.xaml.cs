using AgTerm.Xterm;
using Microsoft.UI.Xaml;

namespace AgTerm.App;

public partial class App : Application
{
    private readonly List<MainWindow> _windows = new();
    private LiveApplicationHost? _applicationHost;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var applicationHost = new LiveApplicationHost();
        _applicationHost = applicationHost;
        foreach (var windowId in applicationHost.LaunchWindowIds)
        {
            var mainWindow = new MainWindow(applicationHost, windowId);
            _windows.Add(mainWindow);
            mainWindow.Closed += (_, _) => OnWindowClosed(mainWindow, applicationHost);
            mainWindow.Activate();
        }

        await Task.WhenAll(_windows.Select(window => window.Ready));
        applicationHost.Start();
    }

    private void OnWindowClosed(MainWindow window, LiveApplicationHost applicationHost)
    {
        _windows.Remove(window);
        if (_windows.Count != 0)
            return;

        applicationHost.Dispose();
        if (ReferenceEquals(_applicationHost, applicationHost))
            _applicationHost = null;
    }
}
