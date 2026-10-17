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
    private Window? _ownerWindow;
    private AppSettings _settings = new();
    private Keymap _keymap = Keymap.Empty;
    private SidebarSnapshot? _snapshot;
    private string? _shapeKey;
    private bool _syncingSelection;
    private SidebarRowViewModel? _editing;
    private string _editingOriginal = "";
    private IReadOnlyList<Guid> _draggedSessions = Array.Empty<Guid>();
    private Guid? _draggedWorkspace;

    public SidebarView()
    {
        InitializeComponent();
        RowsList.ItemsSource = _items;
    }

    /// <summary>The session the user activated by clicking a row — MainWindow refocuses the
    /// terminal after the select lands.</summary>
    public event Action? RowActivated;

    /// <summary>The workspace row's Theme… item. MainWindow opens the theme palette for that
    /// workspace.</summary>
    public event Action<Guid>? WorkspaceThemeRequested;

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
            _items[i].Update(row, _settings, status, ColorsOf(row));
        }

        RefreshRealizedRowAutomationNames();
        ApplyBackgroundShiftOverlay();
        // upstream WindowContentView.shows(_:) for the footer buttons the port draws
        NewWorkspaceButton.Visibility = VisibleUnless(settings, InterfaceElement.NewWorkspace);
        NewSessionButton.Visibility = VisibleUnless(settings, InterfaceElement.NewSession);
        FlaggedToggle.Visibility = VisibleUnless(settings, InterfaceElement.FlaggedView);
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
        if (_shapeKey != snapshot.ShapeKey)
        {
            _shapeKey = snapshot.ShapeKey;
            CancelRename();
            _items.Clear();
            foreach (var row in snapshot.Rows)
                _items.Add(new SidebarRowViewModel(row, _settings, SurfaceStatusOf(snapshot, row), ColorsOf(row)));
        }
        else
        {
            for (var i = 0; i < snapshot.Rows.Count; i++)
                _items[i].Update(snapshot.Rows[i], _settings, SurfaceStatusOf(snapshot, snapshot.Rows[i]),
                    ColorsOf(snapshot.Rows[i]));
        }

        RefreshRealizedRowAutomationNames();
        SyncSelection(snapshot);
        FlaggedToggle.IsChecked = snapshot.Mode == SidebarMode.Flagged;
        ApplyFlaggedHint();
        FlaggedToggle.IsEnabled = snapshot.Mode == SidebarMode.Flagged || snapshot.FlaggedCount > 0;
        ClearFlaggedButton.Visibility = snapshot.FlaggedCount > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        FocusPill.Visibility = snapshot.FocusedWorkspaceName is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        FocusPillText.Text = snapshot.FocusedWorkspaceName ?? "";
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

    private void ApplyBackgroundShiftOverlay()
    {
        var shift = AppSettings.SidebarShiftAmount(
            _settings.SidebarBackgroundShift ?? AppSettings.DefaultSidebarBackgroundShift);
        var alpha = Math.Abs(shift);
        if (alpha <= 0)
        {
            BackgroundShiftOverlay.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            return;
        }

        var channel = shift > 0 ? (byte)0x00 : (byte)0xFF;
        var opacity = (byte)Math.Clamp(Math.Round(alpha * 255.0), 0, 255);
        BackgroundShiftOverlay.Background = new SolidColorBrush(
            Color.FromArgb(opacity, channel, channel, channel));
    }

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
        _syncingSelection = true;
        try
        {
            RowsList.SelectedItems.Clear();
            if (snapshot.SelectedSessionId is { } selected &&
                _items.FirstOrDefault(i => i.IsSession && i.Id == selected) is { } item)
            {
                RowsList.SelectedItems.Add(item);
            }
        }
        finally
        {
            _syncingSelection = false;
        }

        RefreshListSelection();
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

    private SidebarRowColors? ColorsOf(SidebarRow row) =>
        _rowThemes?.ColorsFor(row, _settings.ShowSidebarThemeColors());

    private void RecolorRows()
    {
        foreach (var item in _items)
        {
            var status = _snapshot is { } snapshot ? SurfaceStatusOf(snapshot, item.Row) : SidebarSurfaceStatus.Ready;
            item.Update(item.Row, _settings, status, ColorsOf(item.Row));
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
        // flagged mode decorates the label with " : workspace" — seed the BARE name so the tail
        // can't get committed into the custom name (upstream pin)
        var label = row.Label;
        if (_snapshot?.Mode == SidebarMode.Flagged && label.LastIndexOf(" : ", StringComparison.Ordinal) is var cut and >= 0)
            label = label[..cut];
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

        if (targetRows.Any(r => r.Indicator.Status != AgentStatus.Idle))
        {
            flyout.Items.Add(Item(plural ? "Clear Statuses" : "Clear Status",
                () => _controller?.ClearStatuses(targets)));
            flyout.Items.Add(new MenuFlyoutSeparator());
        }
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
        flyout.Items.Add(Item("New Session", () => _controller?.NewSession(row.Id)));
        flyout.Items.Add(Item("Open Directory…", () => _ = OpenDirectoryIntoAsync(row.Id)));
        var focused = _snapshot?.FocusedWorkspaceId == row.Id;
        flyout.Items.Add(Item(focused ? "Unfocus" : "Focus",
            () => _controller?.FocusWorkspaceToggle(row.Id)));
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
        if (await picker.PickSingleFolderAsync() is { } folder)
            _controller?.NewSession(workspaceId, folder.Path);
    }

    private static MenuFlyoutItem Item(string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => action();
        return item;
    }

    // MARK: - bottom bar

    private void OnFlaggedToggleClick(object sender, RoutedEventArgs e) =>
        _controller?.ToggleFlaggedView();

    private void OnClearFlaggedClick(object sender, RoutedEventArgs e) =>
        _controller?.RequestClearFlags();

    private void OnFocusPillClick(object sender, RoutedEventArgs e) => _controller?.ClearFocus();

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
        if (_snapshot?.Mode == SidebarMode.Flagged)
        {
            e.Cancel = true; // drag is a tree-only affordance (upstream)
            return;
        }
        var dragged = e.Items.OfType<SidebarRowViewModel>().ToList();
        if (dragged.Count == 1 && dragged[0].IsWorkspace)
        {
            _draggedWorkspace = dragged[0].Id;
            return;
        }
        if (dragged.Any(i => i.IsWorkspace))
        {
            e.Cancel = true; // mixed drags have no meaning
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
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = _snapshot?.Mode == SidebarMode.Tree
                ? DataPackageOperation.Copy
                : DataPackageOperation.None;
            e.Handled = true;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Move;
    }

    private async void OnListDrop(object sender, DragEventArgs e)
    {
        if (_snapshot is not { } snapshot || _controller is null) return;
        var position = e.GetPosition(RowsList);

        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.Handled = true;
            var deferral = e.GetDeferral();
            try
            {
                await ImportDroppedDirectoriesAsync(e.DataView, position, snapshot, _controller);
            }
            finally
            {
                deferral.Complete();
            }
            return;
        }

        if (_draggedWorkspace is { } workspace)
        {
            // slot from the workspace-row midpoints only (session rows are ignored, upstream-style)
            var raw = 0;
            for (var i = 0; i < _items.Count; i++)
            {
                if (!_items[i].IsWorkspace) continue;
                if (RowsList.ContainerFromIndex(i) is not ListViewItem container) continue;
                var bounds = container.TransformToVisual(RowsList)
                    .TransformBounds(new Windows.Foundation.Rect(0, 0,
                        container.ActualWidth, container.ActualHeight));
                if (position.Y > bounds.Top + bounds.Height / 2) raw++;
            }
            _controller.DropWorkspace(workspace, raw);
        }
        else if (_draggedSessions.Count > 0)
        {
            var flat = FlatInsertIndex(position);
            if (SidebarFlattenedDrop.SessionSlot(snapshot.Rows, flat) is { } slot)
            {
                _controller.DropSessions(_draggedSessions,
                    new SidebarDrop.SessionDropTarget.WorkspaceRow(slot.Workspace, SessionCount(slot.Workspace)),
                    slot.ChildIndex);
            }
        }
        _draggedSessions = Array.Empty<Guid>();
        _draggedWorkspace = null;
        e.Handled = true;
    }

    private async Task ImportDroppedDirectoriesAsync(DataPackageView dataView,
        Windows.Foundation.Point position, SidebarSnapshot snapshot, SidebarController controller)
    {
        var workspaceId = SidebarDrop.ResolveDirectoryWorkspace(snapshot.Mode,
            WorkspaceAt(position), snapshot.FocusedWorkspaceId, snapshot.CurrentWorkspaceId);
        if (workspaceId is null) return;

        var storageItems = await dataView.GetStorageItemsAsync();
        var paths = storageItems.OfType<StorageFolder>().Select(folder => folder.Path);
        controller.ImportDirectories(DirectoryImportPolicy.CreateRequests(workspaceId, paths));
    }

    private Guid? WorkspaceAt(Windows.Foundation.Point position)
    {
        Guid? workspaceId = null;
        for (var i = 0; i < _items.Count; i++)
        {
            if (_items[i].IsWorkspace) workspaceId = _items[i].Id;
            if (RowsList.ContainerFromIndex(i) is not ListViewItem container) continue;
            var bounds = container.TransformToVisual(RowsList)
                .TransformBounds(new Windows.Foundation.Rect(0, 0,
                    container.ActualWidth, container.ActualHeight));
            if (bounds.Contains(position)) return workspaceId;
        }
        return null;
    }

    private int SessionCount(Guid workspaceId)
    {
        var rows = _snapshot?.Rows ?? Array.Empty<SidebarRow>();
        var count = 0;
        var inside = false;
        foreach (var row in rows)
        {
            if (row.Kind == SidebarRowKind.Workspace) inside = row.Id == workspaceId;
            else if (inside) count++;
        }
        return count;
    }

    /// <summary>The flat insertion slot for a pointer position: rows whose midpoint sits above the
    /// pointer count as passed.</summary>
    private int FlatInsertIndex(Windows.Foundation.Point position)
    {
        var index = 0;
        for (var i = 0; i < _items.Count; i++)
        {
            if (RowsList.ContainerFromIndex(i) is not ListViewItem container) continue;
            var bounds = container.TransformToVisual(RowsList)
                .TransformBounds(new Windows.Foundation.Rect(0, 0,
                    container.ActualWidth, container.ActualHeight));
            if (position.Y > bounds.Top + bounds.Height / 2) index = i + 1;
        }
        return index;
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
