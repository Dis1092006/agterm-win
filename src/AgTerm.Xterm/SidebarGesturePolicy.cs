using AgTerm.Core.Model;

namespace AgTerm.Xterm;

public enum SidebarPointerGesture
{
    Tap,
    DoubleTap,
}

public enum SidebarKeyboardGesture
{
    Enter,
    Space,
    F2,
}

public enum SidebarGestureAction
{
    None,
    SelectSession,
    ActivateSession,
    ToggleWorkspace,
    Rename,
}

public static class SidebarGesturePolicy
{
    public static SidebarGestureAction Decide(
        SidebarRowKind kind,
        SidebarMode mode,
        SidebarPointerGesture gesture)
    {
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(mode) || !Enum.IsDefined(gesture))
        {
            return SidebarGestureAction.None;
        }

        if (gesture == SidebarPointerGesture.DoubleTap)
        {
            return SidebarGestureAction.None;
        }

        if (kind == SidebarRowKind.Session)
        {
            return SidebarGestureAction.ActivateSession;
        }

        if (kind == SidebarRowKind.Workspace && mode == SidebarMode.Tree)
        {
            return SidebarGestureAction.ToggleWorkspace;
        }

        return SidebarGestureAction.None;
    }

    public static SidebarGestureAction DecideKeyboard(
        SidebarRowKind kind,
        SidebarMode mode,
        SidebarKeyboardGesture gesture)
    {
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(mode) || !Enum.IsDefined(gesture))
        {
            return SidebarGestureAction.None;
        }

        if (gesture == SidebarKeyboardGesture.F2)
        {
            if (kind == SidebarRowKind.Session)
            {
                return SidebarGestureAction.Rename;
            }

            if (kind == SidebarRowKind.Workspace && mode == SidebarMode.Tree)
            {
                return SidebarGestureAction.Rename;
            }

            return SidebarGestureAction.None;
        }

        if (gesture == SidebarKeyboardGesture.Space)
        {
            if (kind == SidebarRowKind.Workspace && mode == SidebarMode.Tree)
            {
                return SidebarGestureAction.ToggleWorkspace;
            }

            return SidebarGestureAction.None;
        }

        return Decide(kind, mode, SidebarPointerGesture.Tap);
    }

    public static SidebarGestureAction DecideKeyboard(
        SidebarRowKind kind,
        SidebarMode mode,
        SidebarKeyboardGesture gesture,
        bool isSelected)
    {
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(mode) || !Enum.IsDefined(gesture))
        {
            return SidebarGestureAction.None;
        }

        if (kind == SidebarRowKind.Session && gesture == SidebarKeyboardGesture.Enter)
        {
            return isSelected
                ? SidebarGestureAction.ActivateSession
                : SidebarGestureAction.SelectSession;
        }

        return DecideKeyboard(kind, mode, gesture);
    }
}
