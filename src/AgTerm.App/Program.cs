using System.Runtime.InteropServices;
using AgTerm.Core.Model;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace AgTerm.App;

/// <summary>The XAML-generated Main, plus one instance per state directory: Windows starts a new
/// process on every launch, where macOS activates the running app.</summary>
public static class Program
{
    private static readonly TimeSpan RedirectBudget = TimeSpan.FromSeconds(5);

    [STAThread]
    private static void Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        var instance = AppInstance.FindOrRegisterForKey(SingleInstanceKey.For(PersistenceStore.DefaultDirectory));
        if (!instance.IsCurrent)
        {
            // a second process on the same state files would overwrite the first one's saves
            RedirectActivationTo(instance);
            return;
        }
        instance.Activated += (_, activation) => (Application.Current as App)?.HandleActivation(activation);
        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
    }

    private static void RedirectActivationTo(AppInstance instance)
    {
        // Windows lets a background process take the foreground only when the foreground one allows
        // it, and this process is the one the user just started
        AllowSetForegroundWindow(instance.ProcessId);
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        // the redirect must not run on this STA thread; the budget keeps a stuck running instance
        // from leaving this process behind
        Task.Run(() => instance.RedirectActivationToAsync(activation).AsTask()).Wait(RedirectBudget);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
