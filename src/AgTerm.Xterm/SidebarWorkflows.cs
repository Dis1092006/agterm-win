namespace AgTerm.Xterm;

public enum SidebarConfirmationKind
{
    CloseSession,
    CloseSessions,
    ClearFlagged,
}

public sealed record SidebarConfirmationRequest(
    SidebarConfirmationKind Kind,
    string Title,
    string Message,
    string PrimaryButtonText);

public readonly record struct SidebarCloseOptions(bool Confirm, bool GraceUndo);