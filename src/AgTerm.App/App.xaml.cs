using Microsoft.UI.Xaml;

namespace AgTerm.App;

public partial class App : Application
{
    /// <summary>The single main window (pickers need its HWND for InitializeWithWindow).</summary>
    public static Window? Window { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        Window.Activate();
    }
}
