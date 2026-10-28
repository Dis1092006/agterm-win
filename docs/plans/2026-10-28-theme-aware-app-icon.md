# Theme-aware app icon Implementation Plan

**Goal:** The window icon follows the taskbar color. A light taskbar shows the main icon (dark frame), a dark taskbar shows the inverted icon (light frame).

**Architecture:** `tools/app_icon.py` writes two .ico files into `src/AgTerm.App/Assets`. The app embeds both as managed resources, so they survive single-file publish the same way the exe's own icon does. A pure chooser maps the Windows mode to a resource name, and a pure reader picks one frame out of an .ico. `MainWindow` creates an HICON from that frame and gives it to `AppWindow.SetIcon`, at start and on every system color change.

**Tech Stack:** .NET 10, WinUI 3, xUnit 2, Python 3 with Pillow and resvg-py for the tool.

## Global Constraints

- The taskbar follows the Windows mode, `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\SystemUsesLightTheme`, not the app mode (`AppsUseLightTheme`, which `UISettings` reflects). `1` is a light taskbar. `0` or a missing value is a dark taskbar: Windows 10 1809, the minimum version, has no light taskbar and no value.
- The exe's `ApplicationIcon` stays `Assets\AppIcon.ico`. Explorer, Start and a pinned button of a closed app show it whatever the theme.
- Icon frame choice: the exact size, else the smallest larger frame, else the largest frame. An .ico entry width of 0 means 256.
- Every PR raises `<Version>` in `Directory.Build.props` (main is 0.1.41). The skill does not change, so the plugin version stays.
- TDD, each test seen failing first. Comments say why, not what. Warnings are errors.

## Review Focus

- Whether Windows uses the window icon on a pinned taskbar button while the app runs. Checked live in Task 4.
- Whether `UISettings.ColorValuesChanged` fires when only the Windows mode changes. Checked live in Task 4.

---

### Task 1: Draw the dark-taskbar icon

**Files:**
- Modify: `tools/app_icon.py`
- Create: `src/AgTerm.App/Assets/AppIcon-dark-taskbar.ico`

- [ ] The script takes a palette. The inverted palette swaps frame and pane, the cursor takes the pane color, and the active dot is `#8E8C9C` because `#DBD9E6` disappears on a light frame.
- [ ] `--out` becomes `--out-dir` (default `src/AgTerm.App/Assets`); one run writes `AppIcon.ico` and `AppIcon-dark-taskbar.ico`. `--png-dir` writes the frames of both.
- [ ] `AppIcon.ico` comes out byte for byte the same as on main.

### Task 2: Pick a frame out of an .ico

**Files:**
- Create: `src/AgTerm.App/IconDirectory.cs`
- Test: `tests/AgTerm.Xterm.Tests/IconDirectoryTests.cs` (the source is linked into the test project like the other App helpers)

**Interfaces:** `public static IconFrame? IconDirectory.FrameFor(ReadOnlySpan<byte> ico, int size)`, `public readonly record struct IconFrame(int Size, int Offset, int Length)`.

- [ ] Tests first: the exact size; a size between two frames takes the larger; a size above every frame takes the largest; width byte 0 reads as 256; a truncated header, a wrong type field, or an entry pointing past the end gives null; both committed icons hold 16, 20, 24, 32, 40, 48, 64, 128 and 256.
- [ ] Implement.

### Task 3: Choose the icon for the taskbar

**Files:**
- Create: `src/AgTerm.App/TaskbarAppIcon.cs`
- Test: `tests/AgTerm.Xterm.Tests/TaskbarAppIconTests.cs`

**Interfaces:** `public static string TaskbarAppIcon.ResourceFor(object? systemUsesLightTheme)` returns `AppIcon.ico` for a DWORD 1 and `AppIcon-dark-taskbar.ico` otherwise.

- [ ] Tests first: 1, 0, null, and a value of another type (a string "1" is not a DWORD and reads as dark).
- [ ] Implement.

### Task 4: Set the icon on the window

**Files:**
- Modify: `src/AgTerm.App/AgTerm.App.csproj` (embed both .ico files), `src/AgTerm.App/MainWindow.xaml.cs` (`ApplyExeIcon` becomes `ApplyAppIcon`, also called from `OnSystemColorValuesChanged`), `src/AgTerm.App/WindowAppIcon.cs` (new: the P/Invoke for `CreateIconFromResourceEx`, `GetDpiForWindow`, `GetSystemMetricsForDpi`), `src/AgTerm.App/ShellIconNative.cs` (deleted with `ExtractIconEx`)
- Modify: `Directory.Build.props` (0.1.40), `README.md` if the tool usage changes

- [ ] HICONs are created once per resource and size and kept for the life of the process, because several windows share them and `WM_SETICON` does not copy the handle.
- [ ] One icon at `SM_CXICON` for the window's DPI fills both window slots, through `AppWindow.SetIcon`. A separate `SM_CXSMICON` icon (24 px at 150%) came out blurred on the taskbar and in Alt+Tab, which draw it at 36 px.
- [ ] Build the solution and the app without warnings, run `dotnet test tests/AgTerm.Xterm.Tests`.
- [ ] Live check with `AGTERM_STATE_DIR` set to a temporary directory: the taskbar button on a dark and a light Windows mode, Alt+Tab, a pinned button while the app runs, and a mode switch while the app runs.
