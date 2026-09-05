using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace AgTerm.App;

/// <summary>A hit-testable panel that shows a resize cursor while hovered. This WinAppSDK version
/// exposes cursor control only through the protected `UIElement.ProtectedCursor`, settable solely
/// from within the element's own class — a plain `Border` in MainWindow.xaml.cs cannot set it from
/// the outside, and `Border` itself is sealed so it cannot be subclassed. `Grid` is not, and takes
/// the same `Background` attribute and single-child content used for the divider's 1px line. The
/// in-terminal split divider gets its resize cursor for free from CSS (`cursor: col-resize`,
/// terminal.html); this gives the native sidebar divider the same affordance, which it otherwise
/// lacks entirely.</summary>
public sealed class SidebarResizeHandle : Grid
{
    public SidebarResizeHandle()
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
    }
}
