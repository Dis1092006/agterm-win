# Sidebar agent icons

## Goal

Show a vector icon before a session's name in the sidebar when Claude Code, GitHub Copilot CLI, or Codex runs in the session's focused pane. Detect the running agent from the pane's ConPTY job object so the icon appears at launch, clears on exit or kill, and needs no agent hooks.

## Decisions

- `SessionAgent` (`Claude`, `Copilot`, `Codex`) lives in `AgTerm.Core.Model`. `Session` tracks ephemeral `Agent` and `SplitAgent`, with `FocusedAgent` following `SplitFocused` and `SplitSurface` the same way `FocusedOscTitle` does.
- `CloseSplit`, `ClosePrimaryPane`, and `SwapPanes` clear, promote, and swap `Agent` and `SplitAgent` alongside `OscTitle` and `SplitTitle`.
- `SidebarRow` carries `Agent` and `HasCustomName`. `Session.DisplayName` and the wire `tree` output do not change.
- `InterfaceElement.AgentIcons` (`agentIcons`) controls visibility in Settings > Interface. It is shown by default and hides independently of `sessionIcons` and `sessionFlags`.
- On a session row, the 14x14 stroked `Path` sits before the session name in the inner title grid, after the 16x15 terminal or split icon. It uses `IconForeground` so row themes and icon tints apply to it, and shows `"Claude Code"`, `"Copilot CLI"`, or `"Codex"` as its tooltip.
- While the Claude icon is shown on a session without `CustomName`, `SidebarAgentIcon.FormatLabel` strips a leading `✳` (and optional variation selector plus spaces) from the displayed session name when non-whitespace text follows it, so Claude's OSC title does not show two stars in a row. Hiding `agentIcons` restores the raw label.
- `PaneDescendants` inspects live processes in the pane's job object whose image file name is `claude.exe`, `copilot.exe`, or `codex.exe` and that are attached to the pane's console host. Detached background processes such as `codex app-server` are ignored. When multiple matching processes run in the same pane, the earliest-created one wins so a nested `copilot -p` or `codex exec` tool call does not replace the outer agent's icon.
- `ConPtySession` probes on start and on throttled pty input or output only while no agent is tracked, with one trailing check after output settles so wrapper scripts (`codex.ps1` -> `node.exe` -> `codex.exe`) are caught. Once an agent process is found, `ThreadPool.RegisterWaitForSingleObject` watches its process handle with zero polling while it runs and re-probes as soon as it exits.

## Parts

1. Core model and settings (`SessionAgent`, `InterfaceElement.AgentIcons`, `Session`, `AppStore.Panes`, `SidebarProjection`) and unit tests in `AgTerm.Core.Tests`.
2. ConPTY process detection and exit watch (`ConPtyNative`, `PaneDescendants`, `ConPtySession`) and unit plus live pseudoconsole tests in `AgTerm.Terminal.Tests`.
3. Sidebar presentation and live wiring (`SidebarAgentIcon`, `TerminalPane`, `LiveControlHost`, `SidebarRowViewModel`, `SidebarView.xaml`) and tests in `AgTerm.Xterm.Tests`.
4. Documentation (`docs/PLATFORM-NOTES.md`), app version bump in `Directory.Build.props`, and build/test verification.
