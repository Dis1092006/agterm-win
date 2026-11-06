using System.Collections.ObjectModel;
using AgTerm.Core.Keymap;
using AgTerm.Core.Model;
using AgTerm.Xterm;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.UI;
using Windows.System;

namespace AgTerm.App;

/// <summary>
/// The two-level workspace → session sidebar, rendered as ONE flattened ListView over the Core
/// SidebarProjection rows. THIN by design: every gesture forwards to the SidebarController (the
/// same AppStore mutators the control socket drives); the view only reconciles snapshots — a
/// ShapeKey change rebuilds the collection, anything else diffs row records in place.
/// </summary>
public sealed partial class SidebarView : UserControl
{
    private readonly ObservableCollection<SidebarRowViewModel> _items = new();
    private SidebarController? _controller;
    private SidebarRowThemes? _rowThemes;
    private readonly SidebarRowIcons _rowIcons = new();
    private readonly SolidColorBrush _rowIconFill = new(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));
    private SidebarTintInks _sidebarInks = SidebarTintInks.Dark;
    private string _sidebarForeground = SidebarInfoInks.Default.Foreground;
    private Window? _ownerWindow;
    private AppSettings _settings = new();
    private Keymap _keymap = Keymap.Empty;
    private SidebarSnapshot? _snapshot;
    private string? _shapeKey;
    private long _revealGeneration;
    private bool _syncingSelection;
    private SidebarRowViewModel? _editing;
    private string _editingOriginal = "";
    private IReadOnlyList<Guid> _draggedSessions = Array.Empty<Guid>();
    private Guid? _draggedWorkspace;
    private readonly SidebarSpringLoad _springLoad;

    public SidebarView()
    {
        InitializeComponent();
        RowsList.ItemsSource = _items;
        _springLoad = new SidebarSpringLoad(
            ScheduleOnUi,
            workspace => _controller?.SpringOpenWorkspace(workspace),
            keepOpen => _controller?.EndSpringLoad(keepOpen));
    }

    /// <summary>The session the user activated by clicking a row — MainWindow refocuses the
    /// terminal after the select lands.</summary>
    public event Action? RowActivated;

    public event Action<bool>? RenameStateChanged;
    public event Action<bool>? PopupStateChanged;
    public event Action<bool>? NativeDialogStateChanged;

    /// <summary>The workspace row's Theme… item. MainWindow opens the theme palette for that
    /// workspace.</summary>
    public event Action<Guid>? WorkspaceThemeRequested;

    public event Action<Guid>? WorkspaceIconRequested;

    /// <summary>The empty flagged view's "Add Sessions…" link. MainWindow runs edit_flagged_sessions.</summary>
    public event Action? EditFlaggedSessionsRequested;

    public void Attach(SidebarController controller, AppSettings settings, Window ownerWindow, ThemeCatalog themeCatalog)
    {
        _controller = controller;
        _rowThemes = new SidebarRowThemes(themeCatalog.Resolve);
        _ownerWindow = ownerWindow;
        ApplySettings(settings);
        controller.SnapshotChanged += Apply;
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        for (var i = 0; i < _items.Count; i++)
        {
            var row = _items[i].Row;
            var status = _snapshot is { } snapshot
                ? SurfaceStatusOf(snapshot, row)
                : SidebarSurfaceStatus.Ready;
            _items[i].Update(row, _settings, status, LookOf(row), _rowIcons.IconNameFor(row));
        }

        RefreshRealizedRowAutomationNames();
        // upstream WindowContentView.shows(_:) for the footer buttons the port draws
        NewWorkspaceButton.Visibility = VisibleUnless(settings, InterfaceElement.NewWorkspace);
        NewSessionButton.Visibility = VisibleUnless(settings, InterfaceElement.NewSession);
        FlaggedToggle.Visibility = VisibleUnless(settings, InterfaceElement.FlaggedView);
        FocusFilterToggle.Visibility = VisibleUnless(settings, InterfaceElement.FocusFilter);
    }

    /// <summary>upstream helpHint on the footer buttons: each tooltip names the shortcut that runs it.</summary>
    public void ApplyKeymap(Keymap keymap)
    {
        _keymap = keymap;
        ToolTipService.SetToolTip(NewWorkspaceButton,
            WindowsShortcutText.Hint("New Workspace", keymap, BuiltinAction.NewWorkspace));
        ToolTipService.SetToolTip(NewSessionButton,
            WindowsShortcutText.Hint("New Session", keymap, BuiltinAction.NewSession));
        ApplyFlaggedHint();
        ApplyFocusFilterHint();
    }

    private void ApplyFocusFilterHint()
    {
        var text = _snapshot?.FocusEnabled == true ? "Show all workspaces" : "Show only focused workspaces";
        ToolTipService.SetToolTip(FocusFilterToggle,
            WindowsShortcutText.Hint(text, _keymap, BuiltinAction.ToggleWorkspaceFilter));
    }

    private void ApplyFlaggedHint()
    {
        var text = _snapshot?.Mode == SidebarMode.Flagged ? "Show all sessions" : "Show flagged sessions";
        ToolTipService.SetToolTip(FlaggedToggle,
            WindowsShortcutText.Hint(text, _keymap, BuiltinAction.ToggleFlaggedView));
    }

    private static Visibility VisibleUnless(AppSettings settings, InterfaceElement element) =>
        settings.IsInterfaceElementHidden(element) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Reconcile a fresh snapshot on the UI thread (the controller marshals here).</summary>
    public void Apply(SidebarSnapshot snapshot)
    {
        _snapshot = snapshot;
        _rowThemes?.Observe(snapshot.Generation);
        _rowIcons.Observe(snapshot.Generation);
        if (_shapeKey != snapshot.ShapeKey)
        {
            _shapeKey = snapshot.ShapeKey;
            CancelRename();
            _items.Clear();
            foreach (var row in snapshot.Rows)
                _items.Add(new SidebarRowViewModel(row, _settings, snapshot.Mode, snapshot.RendersWorkspaceRows, SurfaceStatusOf(snapshot, row), LookOf(row),
                    _rowIcons.IconNameFor(row)) { DefaultIconFill = _rowIconFill });
        }
        else
        {
            for (var i = 0; i < snapshot.Rows.Count; i++)
                _items[i].Update(snapshot.Rows[i], _settings, SurfaceStatusOf(snapshot, snapshot.Rows[i]),
                    LookOf(snapshot.Rows[i]), _rowIcons.IconNameFor(snapshot.Rows[i]));
        }

        RefreshRealizedRowAutomationNames();
        SyncSelection(snapshot);
        ScrollRevealedSelection(snapshot);
        var flagged = snapshot.Mode == SidebarMode.Flagged;
        FlaggedToggle.IsChecked = flagged;
        FlaggedEmptyState.Visibility = snapshot.ShowsFlaggedEmptyState ? Visibility.Visible : Visibility.Collapsed;
        FlaggedGlyph.Fill = flagged ? FlaggedGlyph.Stroke : null;
        ApplyFlaggedHint();
        // upstream mutes the disabled toggle by hand: its glyph keeps the chrome foreground
        FlaggedToggle.IsEnabled = flagged || snapshot.FlaggedCount > 0;
        FlaggedToggle.Opacity = FlaggedToggle.IsEnabled ? 1 : 0.35;
        FocusFilterToggle.IsChecked = snapshot.FocusEnabled;
        FocusFilterGlyph.Fill = snapshot.FocusEnabled ? FocusFilterGlyph.Stroke : null;
        // nothing marked means nothing to filter to; the flagged toggle mutes itself the same way
        FocusFilterToggle.IsEnabled = snapshot.FocusMemberIds.Count > 0;
        FocusFilterToggle.Opacity = FocusFilterToggle.IsEnabled ? 1 : 0.35;
        ApplyFocusFilterHint();
    }

    private void RefreshRealizedRowAutomationNames()
    {
        foreach (var item in _items)
        {
            if (RowsList.ContainerFromItem(item) is ListViewItem container)
            {
                AutomationProperties.SetName(container, item.AutomationName);
            }
        }
    }

    /// <summary>The window chrome colors: the theme foreground for text and glyphs, the sidebar
    /// background shift as a wash, and the light or dark controls for the washed color (upstream
    /// ThemeBrightness), so the chevrons, the scroll bar and the focus visuals stay readable.</summary>
    public void ApplyChrome(ChromePalette palette)
    {
        RequestedTheme = palette.SidebarIsDark ? ElementTheme.Dark : ElementTheme.Light;
        SetBrush("SidebarForegroundBrush", palette.Foreground);
        SetBrush("SidebarSecondaryBrush", palette.Secondary);
        SetBrush("SidebarHoverBrush", palette.Hairline);
        SetBrush("SidebarPillBrush", palette.PillFill);
        SetBrush("SidebarWashBrush", palette.SidebarWash);
        _rowIconFill.Color = ToColor(palette.Foreground with { A = 0x60 });
        var foreground = palette.Foreground.Hex;
        if (_sidebarInks == palette.SidebarInks && _sidebarForeground == foreground) return;
        _sidebarInks = palette.SidebarInks;
        _sidebarForeground = foreground;
        RecolorRows();
    }

    private void SetBrush(string key, ChromeColor color) =>
        ((SolidColorBrush)Resources[key]).Color = ToColor(color);

    private static Color ToColor(ChromeColor color) => Color.FromArgb(color.A, color.R, color.G, color.B);
    private static SidebarSurfaceStatus SurfaceStatusOf(SidebarSnapshot snapshot, SidebarRow row)
    {
        if (row.Kind != SidebarRowKind.Session)
        {
            return SidebarSurfaceStatus.Ready;
        }

        return snapshot.SurfaceStatuses.TryGetValue(row.Id, out var status)
            ? status
            : SidebarSurfaceStatus.Ready;
    }

    private void SyncSelection(SidebarSnapshot snapshot)
    {
        var wanted = snapshot.ListSelection()
            .Select(id => _items.FirstOrDefault(i => i.IsSession && i.Id == id))
            .OfType<SidebarRowViewModel>()
            .ToList();
        // like upstream, a list that already shows this selection is left alone
        if (wanted.Count != RowsList.SelectedItems.Count || !wanted.All(RowsList.SelectedItems.Contains))
        {
            _syncingSelection = true;
            try
            {
                RowsList.SelectedItems.Clear();
                foreach (var item in wanted)
                    RowsList.SelectedItems.Add(item);
            }
            finally
            {
                _syncingSelection = false;
            }
        }

        RefreshListSelection();
    }

    /// <summary>A new reveal (a selection change or a view switch) scrolls the selected row into view;
    /// any other update leaves the scroll position where the user put it.</summary>
    private void ScrollRevealedSelection(SidebarSnapshot snapshot)
    {
        if (_revealGeneration == snapshot.RevealGeneration) return;
        _revealGeneration = snapshot.RevealGeneration;
        if (_items.FirstOrDefault(i => i.IsSession && i.Id == snapshot.SelectedSessionId) is { } row)
            RowsList.ScrollIntoView(row);
    }

    /// <summary>The workspace theme palette's preview, shown on that workspace's rows in this window
    /// only, like the preview on its panes.</summary>
    public void PreviewWorkspaceTheme(Guid workspaceId, string? themeName)
    {
        _rowThemes?.Preview(workspaceId, themeName);
        RecolorRows();
    }

    public void EndWorkspaceThemePreview()
    {
        _rowThemes?.EndPreview();
        RecolorRows();
    }

    public void WorkspaceThemeSaved(Guid workspaceId, string? themeName)
    {
        // the save already changed the store, so a snapshot requested now shows it
        _rowThemes?.Saved(workspaceId, themeName, _controller?.RequestSnapshot() ?? 0);
        RecolorRows();
    }

    /// <summary>The workspace icon palette's preview, shown on that workspace's rows in this window
    /// only.</summary>
    public void PreviewWorkspaceIcon(Guid workspaceId, string? iconName)
    {
        _rowIcons.Preview(workspaceId, iconName);
        RecolorRows();
    }

    public void EndWorkspaceIconPreview()
    {
        _rowIcons.EndPreview();
        RecolorRows();
    }

    public void WorkspaceIconSaved(Guid workspaceId, string? iconName)
    {
        // the save already changed the store, so a snapshot requested now shows it
        _rowIcons.Saved(workspaceId, iconName, _controller?.RequestSnapshot() ?? 0);
        RecolorRows();
    }

    private SidebarRowLook LookOf(SidebarRow row)
    {
        var style = _settings.EffectiveSidebarThemeStyle();
        var look = SidebarRowLook.From(_rowThemes?.ColorsFor(row, style != SidebarThemeStyle.Off), style);
        return look with
        {
            Tint = SidebarRowTint.For(row.Tint, look, _sidebarInks),
            Info = SidebarInfoInks.For(look, _sidebarForeground, _sidebarInks),
        };
    }

    private void RecolorRows()
    {
        foreach (var item in _items)
        {
            var status = _snapshot is { } snapshot ? SurfaceStatusOf(snapshot, item.Row) : SidebarSurfaceStatus.Ready;
            item.Update(item.Row, _settings, status, LookOf(item.Row), _rowIcons.IconNameFor(item.Row));
        }
    }

    private void RefreshListSelection()
    {
        foreach (var item in _items)
            item.IsListSelected = RowsList.SelectedItems.Contains(item);
    }

    // MARK: - selection / activation

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshListSelection();
        if (_syncingSelection || _controller is null) return;
        // workspace rows are not selectable — strip them (they still get their tap toggle)
        foreach (var added in e.AddedItems.OfType<SidebarRowViewModel>().Where(i => i.IsWorkspace).ToList())
            RowsList.SelectedItems.Remove(added);

        var selectedSessions = RowsList.SelectedItems.OfType<SidebarRowViewModel>()
            .Where(i => i.IsSession).Select(i => i.Id).ToList();
        if (selectedSessions.Count == 0) return;

        var clicked = e.AddedItems.OfType<SidebarRowViewModel>().FirstOrDefault(i => i.IsSession);
        var active = clicked?.Id
            ?? (_snapshot?.SelectedSessionId is { } current && selectedSessions.Contains(current)
                ? current
                : selectedSessions[^1]);
        _controller.Select(active, selectedSessions);
        RowActivated?.Invoke();
    }

    private void OnRowsListContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        // a container leaving its row takes the pointer with it: the row may never see PointerExited,
        // and its "+" would stay revealed when it is shown again
        if (args.InRecycleQueue && args.Item is SidebarRowViewModel recycled)
        {
            recycled.IsPointerOver = false;
            return;
        }
        if (args.ItemContainer is ListViewItem container && args.Item is SidebarRowViewModel row)
        {
            AutomationProperties.SetName(container, row.AutomationName);
        }
    }

    // MARK: - pointer tap gestures

    private void OnRowTapped(object sender, TappedRoutedEventArgs e)
    {
        // the workspace "+" acts on its own; its tap must not also toggle the workspace
        if (FromButton(e.OriginalSource)) return;
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not SidebarRowViewModel row) return;
        var mode = _snapshot?.Mode ?? SidebarMode.Tree;
        ExecuteRowAction(row, SidebarGesturePolicy.Decide(row.Kind, mode, SidebarPointerGesture.Tap));
    }

    // whether a tap landed on a button inside the row rather than on the row itself
    private static bool FromButton(object? source)
    {
        for (var node = source as DependencyObject;
             node is not null and not ListViewItem;
             node = VisualTreeHelper.GetParent(node))
        {
            if (node is ButtonBase) return true;
        }
        return false;
    }

    // MARK: - workspace "+"

    private void OnRowPointerEntered(object sender, PointerRoutedEventArgs e) => SetPointerOver(sender, true);

    private void OnRowPointerExited(object sender, PointerRoutedEventArgs e) => SetPointerOver(sender, false);

    private static void SetPointerOver(object sender, bool pointerOver)
    {
        if ((sender as FrameworkElement)?.DataContext is SidebarRowViewModel row)
            row.IsPointerOver = pointerOver;
    }

    // the same path as the workspace menu's "New Session"; the button carries no id, so the row is
    // read at click time and a recycled container targets the right workspace
    private void OnAddSessionClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SidebarRowViewModel { IsWorkspace: true } row)
            _controller?.NewSession(row.Id);
    }

    // MARK: - inline rename

    private void OnListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var gesture = e.Key switch
        {
            VirtualKey.Enter => SidebarKeyboardGesture.Enter,
            VirtualKey.Space => SidebarKeyboardGesture.Space,
            VirtualKey.F2 => SidebarKeyboardGesture.F2,
            _ => (SidebarKeyboardGesture?)null,
        };
        if (gesture is not { } keyboardGesture) return;

        if (FocusedRow() is not { } row) return;
        var mode = _snapshot?.Mode ?? SidebarMode.Tree;
        var action = SidebarGesturePolicy.DecideKeyboard(
            row.Kind,
            mode,
            keyboardGesture,
            row.Id == _snapshot?.SelectedSessionId);
        if (action == SidebarGestureAction.None)
        {
            return;
        }

        ExecuteRowAction(row, action);
        e.Handled = true;
    }

    /// <summary>The rename_session hotkey: inline-rename the selected session row.</summary>
    public void BeginRenameSelected()
    {
        if (_items.FirstOrDefault(i => i.IsSession && i.Id == _snapshot?.SelectedSessionId) is { } row)
            BeginRename(row);
    }

    public void BeginRenameCurrentWorkspace()
    {
        var snapshot = _snapshot;
        if (_items.FirstOrDefault(i => i.IsWorkspace && i.Id == snapshot?.CurrentWorkspaceId) is { } row)
            BeginRename(row);
    }

    private void BeginRename(SidebarRowViewModel row)
    {
        CancelRename();
        _editing = row;
        RenameStateChanged?.Invoke(true);
        var snapshot = _snapshot;
        var label = snapshot is null ? row.Label : SidebarRenameSeed.Of(row.Label, snapshot.Mode, snapshot.FlaggedLayout);
        _editingOriginal = label;
        row.EditText = label;
        row.IsEditing = true;
    }

    /// <summary>Fires once per realized container (ListView recycles containers without re-raising
    /// Loaded), so the initial focus attempt here alone would miss every rename after the first on a
    /// given row. The Visibility callback registered here instead re-fires on every edit-mode entry,
    /// since EditorVisibility flips Visible each time regardless of container recycling.</summary>
    private void OnRenameEditorLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box) return;
        box.RegisterPropertyChangedCallback(VisibilityProperty, OnRenameEditorVisibilityChanged);
        if (box.Visibility == Visibility.Visible && (box.DataContext as SidebarRowViewModel) == _editing)
            FocusRenameEditor(box);
    }

    private void OnRenameEditorVisibilityChanged(DependencyObject sender, DependencyProperty dp)
    {
        if (sender is TextBox { Visibility: Visibility.Visible } box &&
            (box.DataContext as SidebarRowViewModel) == _editing)
        {
            FocusRenameEditor(box);
        }
    }

    /// <summary>Focus can silently no-op immediately after a Collapsed→Visible flip (layout hasn't
    /// caught up yet), so retry once after the current layout pass if the first attempt is rejected.</summary>
    private static void FocusRenameEditor(TextBox box)
    {
        if (box.Focus(FocusState.Programmatic))
        {
            box.SelectAll();
            return;
        }

        box.DispatcherQueue.TryEnqueue(() =>
        {
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
        });
    }

    private void OnRenameKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_editing is not { } row) return;
        switch (e.Key)
        {
            case VirtualKey.Enter:
                CommitRename(row);
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                CancelRename();
                e.Handled = true;
                RowActivated?.Invoke();
                break;
        }
    }

    private void OnRenameLostFocus(object sender, RoutedEventArgs e)
    {
        if (_editing is { } row && (sender as TextBox)?.DataContext == row) CommitRename(row);
    }

    private void CommitRename(SidebarRowViewModel row)
    {
        var text = row.EditText;
        _editing = null;
        row.IsEditing = false;
        RenameStateChanged?.Invoke(false);
        if (text != _editingOriginal)
        {
            if (row.IsSession) _controller?.RenameSession(row.Id, text);
            else _controller?.RenameWorkspace(row.Id, text);
        }
        RowActivated?.Invoke();
    }

    private void CancelRename()
    {
        if (_editing is not { } row) return;
        _editing = null;
        row.IsEditing = false;
        RenameStateChanged?.Invoke(false);
    }

    private SidebarRowViewModel? FocusedRow()
    {
        if (FocusManager.GetFocusedElement(XamlRoot) is FrameworkElement { DataContext: SidebarRowViewModel focused })
        {
            return focused;
        }

        return RowsList.SelectedItems.OfType<SidebarRowViewModel>().FirstOrDefault();
    }

    private void ExecuteRowAction(SidebarRowViewModel row, SidebarGestureAction action)
    {
        switch (action)
        {
            case SidebarGestureAction.ToggleWorkspace:
                _controller?.ToggleWorkspaceCollapse(row.Id);
                break;
            case SidebarGestureAction.SelectSession:
                _controller?.Select(row.Id, new[] { row.Id });
                RowActivated?.Invoke();
                break;
            case SidebarGestureAction.ActivateSession:
                if (row.Id == _snapshot?.SelectedSessionId)
                {
                    // SelectionChanged owns selecting a new row; direct activation only refocuses.
                    RowActivated?.Invoke();
                }
                break;
            case SidebarGestureAction.Rename:
                BeginRename(row);
                break;
            default:
                break;
        }
    }

    // MARK: - context menus

    private void OnRowContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        var row = (e.OriginalSource as FrameworkElement)?.DataContext as SidebarRowViewModel
            ?? FocusedRow();
        if (row is null) return;

        var flyout = row.IsWorkspace ? WorkspaceMenu(row) : SessionMenu(row);
        flyout.Opening += (_, _) => PopupStateChanged?.Invoke(true);
        flyout.Closed += (_, _) => PopupStateChanged?.Invoke(false);
        if (e.TryGetPosition(RowsList, out var point))
        {
            flyout.ShowAt(RowsList, point);
        }
        else if (RowsList.ContainerFromItem(row) is FrameworkElement rowContainer)
        {
            flyout.ShowAt(rowContainer);
        }
        else
        {
            flyout.ShowAt(RowsList);
        }

        e.Handled = true;
    }

    /// <summary>List convention: context invocation inside the multi-selection keeps it; outside
    /// narrows to the invoked row.</summary>
    private IReadOnlyList<Guid> ContextTargets(SidebarRowViewModel clicked)
    {
        var selected = RowsList.SelectedItems.OfType<SidebarRowViewModel>()
            .Where(i => i.IsSession).Select(i => i.Id).ToList();
        return selected.Contains(clicked.Id) ? selected : new List<Guid> { clicked.Id };
    }

    private MenuFlyout SessionMenu(SidebarRowViewModel row)
    {
        var flyout = new MenuFlyout();
        var targets = ContextTargets(row);
        var rows = _snapshot?.Rows ?? Array.Empty<SidebarRow>();
        var targetRows = rows.Where(r => targets.Contains(r.Id)).ToList();
        var plural = targets.Count > 1;

        var anyIdle = targetRows.Any(r => r.Indicator.Status == AgentStatus.Idle);
        var anyStatus = targetRows.Any(r => r.Indicator.Status != AgentStatus.Idle);
        if (anyIdle)
            flyout.Items.Add(Item("Mark for Attention", () => _controller?.MarkForAttention(targets)));
        if (anyStatus)
        {
            flyout.Items.Add(Item(plural ? "Clear Statuses" : "Clear Status",
                () => _controller?.ClearStatuses(targets)));
        }
        if (anyIdle || anyStatus)
            flyout.Items.Add(new MenuFlyoutSeparator());
        if (!plural)
        {
            flyout.Items.Add(Item("Rename", () => BeginRename(row)));
            flyout.Items.Add(Item("Duplicate Session", () => _controller?.DuplicateSession(row.Id)));
        }

        var elsewhere = (_snapshot?.Workspaces ?? Array.Empty<SidebarWorkspaceInfo>())
            .Where(w => targetRows.Count == 0 || !AllTargetsLiveIn(w.Id, targets)).ToList();
        if (elsewhere.Count > 0)
        {
            var move = new MenuFlyoutSubItem { Text = "Move to" };
            foreach (var workspace in elsewhere)
            {
                move.Items.Add(Item(workspace.Name,
                    () => _controller?.MoveSessionsTo(targets, workspace.Id)));
            }
            flyout.Items.Add(move);
        }

        var allFlagged = targetRows.Count > 0 && targetRows.All(r => r.Flagged);
        flyout.Items.Add(Item(
            allFlagged ? (plural ? "Unflag Sessions" : "Unflag") : (plural ? "Flag Sessions" : "Flag"),
            () => _controller?.SetFlag(targets, !allFlagged)));
        flyout.Items.Add(TintMenu(targetRows.Select(r => r.Tint).ToList(), SidebarTint.SessionParts,
            choice => _controller?.SetTint(targets, choice)));
        if (!plural)
            flyout.Items.Add(Item("Reveal in Explorer", () => _controller?.RevealSession(row.Id)));
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(Item(plural ? $"Close {targets.Count} Sessions" : "Close Session",
            () => _controller?.RequestCloseSessions(targets, CurrentCloseOptions())));
        return flyout;
    }

    private bool AllTargetsLiveIn(Guid workspaceId, IReadOnlyList<Guid> targets)
    {
        var rows = _snapshot?.Rows ?? Array.Empty<SidebarRow>();
        Guid? current = null;
        var owners = new Dictionary<Guid, Guid>();
        foreach (var row in rows)
        {
            if (row.Kind == SidebarRowKind.Workspace) current = row.Id;
            else if (current is { } ws) owners[row.Id] = ws;
        }
        return targets.All(t => owners.TryGetValue(t, out var owner) && owner == workspaceId);
    }

    private MenuFlyout WorkspaceMenu(SidebarRowViewModel row)
    {
        var flyout = new MenuFlyout();
        flyout.Items.Add(Item("Rename", () => BeginRename(row)));
        flyout.Items.Add(Item("Theme…", () => WorkspaceThemeRequested?.Invoke(row.Id)));
        flyout.Items.Add(Item("Icon…", () => WorkspaceIconRequested?.Invoke(row.Id)));
        flyout.Items.Add(TintMenu([row.Row.Tint], SidebarTint.WorkspaceParts, choice => _controller?.SetWorkspaceTint(row.Id, choice)));
        flyout.Items.Add(Item("New Session", () => _controller?.NewSession(row.Id)));
        flyout.Items.Add(Item("Open Directory…", () => _ = OpenDirectoryIntoAsync(row.Id)));
        flyout.Items.Add(new MenuFlyoutSeparator());
        var sole = _snapshot?.FocusedWorkspaceId == row.Id;
        flyout.Items.Add(Item(sole ? "Unfocus" : "Focus",
            () => _controller?.FocusWorkspaceToggle(row.Id)));
        var member = _snapshot?.FocusMemberIds.Contains(row.Id) == true;
        flyout.Items.Add(Item(member ? "Remove from Focus" : "Add to Focus",
            () => _controller?.ToggleFocusMembership(row.Id)));
        flyout.Items.Add(new MenuFlyoutSeparator());
        var info = _snapshot?.Workspaces.FirstOrDefault(w => w.Id == row.Id);
        var flagAll = Item(info?.FlagAllTitle ?? "Flag All Sessions", () => _controller?.ToggleWorkspaceFlag(row.Id));
        flagAll.IsEnabled = info?.CanFlagAll == true;
        flyout.Items.Add(flagAll);
        flyout.Items.Add(new MenuFlyoutSeparator());
        var delete = Item("Delete Workspace", () =>
            _controller?.RequestDeleteWorkspace(row.Id, CurrentCloseOptions().GraceUndo));
        delete.IsEnabled = _snapshot?.CanRemoveWorkspace == true;
        flyout.Items.Add(delete);
        return flyout;
    }

    private async Task OpenDirectoryIntoAsync(Guid workspaceId)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker,
            WinRT.Interop.WindowNative.GetWindowHandle(_ownerWindow
                ?? throw new InvalidOperationException("Sidebar has no owning window.")));
        NativeDialogStateChanged?.Invoke(true);
        try
        {
            if (await picker.PickSingleFolderAsync() is { } folder)
                _controller?.NewSession(workspaceId, folder.Path);
        }
        finally { NativeDialogStateChanged?.Invoke(false); }
    }

    /// <summary>The Tint submenu (upstream WorkspaceSidebar+TintMenu.swift). Each color shows a swatch in
    /// the color the sidebar draws it; each part the row kind has is a checkbox.</summary>
    private MenuFlyoutSubItem TintMenu(IReadOnlyList<SidebarTint?> current, TintParts parts, Action<SidebarTintChoice> apply)
    {
        var tint = new MenuFlyoutSubItem { Text = "Tint" };
        foreach (var entry in SidebarTintMenu.Colors(current))
        {
            var item = TintItem(entry, apply);
            if (entry.Choice.Color is { } color)
            {
                item.Icon = new FontIcon
                {
                    FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                    Glyph = "\uE91F", // FullCircle
                    Foreground = SidebarRowViewModel.BrushFrom(_sidebarInks.For(color)),
                };
            }
            tint.Items.Add(item);
        }
        tint.Items.Add(new MenuFlyoutSeparator());
        tint.Items.Add(new MenuFlyoutItem { Text = "Show On", IsEnabled = false });
        foreach (var entry in SidebarTintMenu.Placements(current, parts))
            tint.Items.Add(TintItem(entry, apply));
        return tint;
    }

    private static ToggleMenuFlyoutItem TintItem(SidebarTintMenuEntry entry, Action<SidebarTintChoice> apply)
    {
        var item = new ToggleMenuFlyoutItem { Text = entry.Title, IsChecked = entry.IsChecked, IsEnabled = entry.IsEnabled };
        item.Click += (_, _) => apply(entry.Choice);
        return item;
    }

    private static MenuFlyoutItem Item(string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => action();
        return item;
    }

    // MARK: - bottom bar

    private void OnFlaggedEmptyAddClick(object sender, RoutedEventArgs e) => EditFlaggedSessionsRequested?.Invoke();

    private void OnFlaggedToggleClick(object sender, RoutedEventArgs e) =>
        _controller?.ToggleFlaggedView();

    private void OnFocusFilterClick(object sender, RoutedEventArgs e) =>
        _controller?.ToggleWorkspaceFilter();

    private void OnNewWorkspaceClick(object sender, RoutedEventArgs e) =>
        _controller?.NewWorkspace();

    // upstream actions.newSession(): the active workspace, at the newSessionDirectory cwd
    private void OnFooterNewSessionClick(object sender, RoutedEventArgs e)
    {
        _controller?.NewSessionInActiveWorkspace();
    }

    // upstream actions.openDirectory(): a picked directory in the active workspace
    private void OnFooterOpenDirectoryClick(object sender, RoutedEventArgs e)
    {
        if (_snapshot?.CurrentWorkspaceId is { } workspaceId)
            _ = OpenDirectoryIntoAsync(workspaceId);
    }

    private SidebarCloseOptions CurrentCloseOptions() => new(
        _settings.ConfirmCloseSession == true,
        _settings.CloseGraceUndoEnabled ?? true);

    // MARK: - drag reorder

    private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        _draggedSessions = Array.Empty<Guid>();
        _draggedWorkspace = null;
        var dragged = e.Items.OfType<SidebarRowViewModel>().ToList();
        if (dragged.Count == 1 && dragged[0].IsWorkspace && _snapshot?.Mode == SidebarMode.Tree)
        {
            _draggedWorkspace = dragged[0].Id;
            return;
        }
        if (dragged.Any(i => i.IsWorkspace))
        {
            e.Cancel = true; // workspaces reorder only in the tree (upstream); mixed drags have no meaning
            return;
        }
        // a selected row drags the whole selection block (visual order); an unselected one just itself
        var selection = RowsList.SelectedItems.OfType<SidebarRowViewModel>()
            .Where(i => i.IsSession).Select(i => i.Id).ToHashSet();
        var draggedIds = dragged.Select(i => i.Id).ToHashSet();
        var block = draggedIds.Overlaps(selection) && draggedIds.IsSubsetOf(selection)
            ? selection
            : draggedIds;
        _draggedSessions = _items.Where(i => i.IsSession && block.Contains(i.Id))
            .Select(i => i.Id).ToList();
    }

    private void OnListDragOver(object sender, DragEventArgs e)
    {
        var position = e.GetPosition(RowsList);
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            var accepted = _snapshot?.Mode == SidebarMode.Tree;
            e.AcceptedOperation = accepted ? DataPackageOperation.Copy : DataPackageOperation.None;
            e.Handled = true;
            SpringHover(accepted && _snapshot is { } snapshot
                ? SidebarDrop.ResolveDirectoryWorkspace(snapshot.Mode, WorkspaceAt(position),
                    snapshot.FocusedWorkspaceId, snapshot.CurrentWorkspaceId)
                : null);
            return;
        }

        if (_draggedWorkspace is not null && GapY(WorkspaceDrop(position).FlatIndex) is { } lineY)
        {
            SpringHover(null);
            e.AcceptedOperation = DataPackageOperation.Move;
            ShowDropLine(lineY);
            return;
        }
        // only a drop that changes the sidebar is offered, as upstream's validateDrop
        if (_draggedSessions.Count > 0 && SessionDropAt(position) is { } target &&
            _snapshot?.DropModel.Resolve(_draggedSessions, target.Drop) is { } move &&
            ShowDrop(target.Point))
        {
            SpringHover((move as SidebarSessionMove.Sessions)?.Workspace);
            e.AcceptedOperation = DataPackageOperation.Move;
            return;
        }
        SpringHover(null);
        e.AcceptedOperation = DataPackageOperation.None;
        HideDropIndicator();
    }

    /// <summary>Feeds the spring load the workspace a drop here would land in, with whether its row is
    /// open. A workspace without a row has no spring load.</summary>
    private void SpringHover(Guid? workspace)
    {
        var row = workspace is { } id
            ? _snapshot?.Rows.FirstOrDefault(r => r.Kind == SidebarRowKind.Workspace && r.Id == id)
            : null;
        _springLoad.Hover(row?.Id, row is { Collapsed: false });
    }

    private Action ScheduleOnUi(TimeSpan delay, Action fire)
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = delay;
        timer.IsRepeating = false;
        timer.Tick += (sender, _) =>
        {
            sender.Stop();
            fire();
        };
        timer.Start();
        return timer.Stop;
    }

    private void OnListDragLeave(object sender, DragEventArgs e)
    {
        HideDropIndicator();
        if (IsOutsideList(e.GetPosition(RowsList))) _springLoad.Cancel();
    }

    /// <summary>DragLeave also bubbles up when the pointer crosses from one row to the next; only a
    /// point outside the list means the drag left it.</summary>
    private bool IsOutsideList(Windows.Foundation.Point position) =>
        position.X < 0 || position.Y < 0 || position.X >= RowsList.ActualWidth || position.Y >= RowsList.ActualHeight;

    private void OnDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        HideDropIndicator();
        _springLoad.Cancel(); // a drop already finished it; a cancelled drag folds it back
        _draggedSessions = Array.Empty<Guid>();
        _draggedWorkspace = null;
    }

    private async void OnListDrop(object sender, DragEventArgs e)
    {
        HideDropIndicator();
        if (_snapshot is not { } snapshot || _controller is null) return;
        var position = e.GetPosition(RowsList);

        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.Handled = true;
            var deferral = e.GetDeferral();
            try
            {
                _springLoad.Finish(await ImportDroppedDirectoriesAsync(e.DataView, position, snapshot, _controller));
            }
            finally
            {
                deferral.Complete();
            }
            return;
        }

        // a successful drop keeps the workspace the drag opened open, so the moved rows stay visible
        var dropped = false;
        if (_draggedWorkspace is { } workspace)
        {
            _controller.DropWorkspace(workspace, WorkspaceDrop(position).Slot);
            dropped = true;
        }
        else if (_draggedSessions.Count > 0 && SessionDropAt(position) is { } target)
        {
            dropped = snapshot.DropModel.Resolve(_draggedSessions, target.Drop) is not null;
            _controller.DropSessions(_draggedSessions, target.Drop);
        }
        _springLoad.Finish(dropped);
        _draggedSessions = Array.Empty<Guid>();
        _draggedWorkspace = null;
        e.Handled = true;
    }

    /// <summary>The session drop under the pointer: the row zones (SidebarFlattenedDrop.Locate) over the
    /// realized rows, read in the projection's terms.</summary>
    private (SidebarDropPoint Point, SidebarSessionDrop Drop)? SessionDropAt(Windows.Foundation.Point position)
    {
        if (_snapshot is not { } snapshot) return null;
        var bounds = new List<SidebarRowBounds>();
        for (var i = 0; i < _items.Count; i++)
        {
            if (RowRect(i) is { } rect) bounds.Add(new SidebarRowBounds(i, rect.Top, rect.Height));
        }
        var point = SidebarFlattenedDrop.Locate(bounds, position.Y);
        return SidebarFlattenedDrop.SessionDrop(snapshot.Rows, snapshot.RendersWorkspaceRows, point) is { } drop
            ? (point, drop)
            : null;
    }

    /// <summary>A dragged workspace's slot: the workspace rows whose midpoint sits above the pointer (session
    /// rows are ignored, upstream-style), and the flat index of the row it lands before.</summary>
    private (int Slot, int FlatIndex) WorkspaceDrop(Windows.Foundation.Point position)
    {
        var slot = 0;
        var flatIndex = _items.Count;
        for (var i = 0; i < _items.Count; i++)
        {
            if (!_items[i].IsWorkspace || RowRect(i) is not { } rect) continue;
            if (position.Y > rect.Top + rect.Height / 2) slot++;
            else if (flatIndex == _items.Count) flatIndex = i;
        }
        return (slot, flatIndex);
    }

    private bool ShowDrop(SidebarDropPoint point)
    {
        switch (point)
        {
            case SidebarDropPoint.OnRow(var index) when RowRect(index) is { } rect:
                DropLine.Visibility = Visibility.Collapsed;
                DropFrame.Visibility = Visibility.Visible;
                DropFrame.Width = Math.Max(0, rect.Width - 4);
                DropFrame.Height = rect.Height;
                Canvas.SetLeft(DropFrame, rect.Left + 2);
                Canvas.SetTop(DropFrame, rect.Top);
                DropIndicator.Visibility = Visibility.Visible;
                return true;
            case SidebarDropPoint.Gap(var index) when GapY(index) is { } y:
                ShowDropLine(y);
                return true;
            default:
                HideDropIndicator();
                return false;
        }
    }

    private void ShowDropLine(double y)
    {
        DropFrame.Visibility = Visibility.Collapsed;
        DropLine.Visibility = Visibility.Visible;
        DropLine.Width = Math.Max(0, RowsList.ActualWidth - 8);
        Canvas.SetLeft(DropLine, 4);
        Canvas.SetTop(DropLine, y - DropLine.Height / 2);
        DropIndicator.Visibility = Visibility.Visible;
    }

    private void HideDropIndicator() => DropIndicator.Visibility = Visibility.Collapsed;

    /// <summary>The y of the gap before the row at flatIndex: that row's top, or the bottom of the row above
    /// it at the end of the list. Null when neither row is realized.</summary>
    private double? GapY(int flatIndex)
    {
        if (flatIndex < _items.Count && RowRect(flatIndex) is { } below) return below.Top;
        if (flatIndex > 0 && RowRect(flatIndex - 1) is { } above) return above.Bottom;
        return null;
    }

    /// <summary>A realized row's bounds in the list's coordinates.</summary>
    private Windows.Foundation.Rect? RowRect(int index) =>
        RowsList.ContainerFromIndex(index) is ListViewItem container
            ? container.TransformToVisual(RowsList).TransformBounds(
                new Windows.Foundation.Rect(0, 0, container.ActualWidth, container.ActualHeight))
            : null;

    /// <summary>Creates a session for each dropped folder. True when there was one to create.</summary>
    private async Task<bool> ImportDroppedDirectoriesAsync(DataPackageView dataView,
        Windows.Foundation.Point position, SidebarSnapshot snapshot, SidebarController controller)
    {
        var workspaceId = SidebarDrop.ResolveDirectoryWorkspace(snapshot.Mode,
            WorkspaceAt(position), snapshot.FocusedWorkspaceId, snapshot.CurrentWorkspaceId);
        if (workspaceId is null) return false;

        var storageItems = await dataView.GetStorageItemsAsync();
        var paths = storageItems.OfType<StorageFolder>().Select(folder => folder.Path);
        var requests = DirectoryImportPolicy.CreateRequests(workspaceId, paths);
        controller.ImportDirectories(requests);
        return requests.Count > 0;
    }

    private Guid? WorkspaceAt(Windows.Foundation.Point position)
    {
        Guid? workspaceId = null;
        for (var i = 0; i < _items.Count; i++)
        {
            if (_items[i].IsWorkspace) workspaceId = _items[i].Id;
            if (RowRect(i) is { } bounds && bounds.Contains(position)) return workspaceId;
        }
        return null;
    }

    // MARK: - status glyph blink

    private void OnGlyphLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Shape glyph || glyph.DataContext is not SidebarRowViewModel row) return;
        if (!row.GlyphBlink) return;
        var animation = new DoubleAnimation
        {
            From = 1.0,
            To = 0.2,
            Duration = new Duration(TimeSpan.FromMilliseconds(500)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        Storyboard.SetTarget(animation, glyph);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }
}
