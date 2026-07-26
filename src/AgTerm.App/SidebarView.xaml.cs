using System.Collections.ObjectModel;
using AgTerm.Core.Model;
using AgTerm.Xterm;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
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
    private AppSettings _settings = new();
    private SidebarSnapshot? _snapshot;
    private string? _shapeKey;
    private bool _syncingSelection;
    private DispatcherQueueTimerHolder? _pendingToggle;
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

    public void Attach(SidebarController controller, AppSettings settings)
    {
        _controller = controller;
        _settings = settings;
        controller.SnapshotChanged += Apply;
    }

    /// <summary>Reconcile a fresh snapshot on the UI thread (the controller marshals here).</summary>
    public void Apply(SidebarSnapshot snapshot)
    {
        _snapshot = snapshot;
        if (_shapeKey != snapshot.ShapeKey)
        {
            _shapeKey = snapshot.ShapeKey;
            CancelRename();
            _items.Clear();
            foreach (var row in snapshot.Rows)
                _items.Add(new SidebarRowViewModel(row, _settings));
        }
        else
        {
            for (var i = 0; i < snapshot.Rows.Count; i++)
                _items[i].Update(snapshot.Rows[i], _settings);
        }
        SyncSelection(snapshot);
        FlaggedToggle.IsChecked = snapshot.Mode == SidebarMode.Flagged;
        FlaggedToggle.IsEnabled = snapshot.Mode == SidebarMode.Flagged || snapshot.FlaggedCount > 0;
        FocusPill.Visibility = snapshot.FocusedWorkspaceName is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        FocusPillText.Text = snapshot.FocusedWorkspaceName ?? "";
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
    }

    // MARK: - selection / activation

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
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

    // MARK: - workspace collapse (deferred single tap, canceled by a rename double-tap)

    private void OnRowTapped(object sender, TappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not SidebarRowViewModel row) return;
        if (!row.IsWorkspace || _snapshot?.Mode == SidebarMode.Flagged) return;
        _pendingToggle?.Cancel();
        var timer = DispatcherQueue.CreateTimer();
        _pendingToggle = new DispatcherQueueTimerHolder(timer);
        timer.Interval = TimeSpan.FromMilliseconds(350);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            _pendingToggle?.Cancel();
            _controller?.ToggleWorkspaceCollapse(row.Id);
        };
        timer.Start();
    }

    private sealed class DispatcherQueueTimerHolder
    {
        private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;

        public DispatcherQueueTimerHolder(Microsoft.UI.Dispatching.DispatcherQueueTimer timer)
        {
            _timer = timer;
        }

        public void Cancel() => _timer.Stop();
    }

    // MARK: - inline rename

    private void OnRowDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        _pendingToggle?.Cancel(); // a double-click renames; it must not flip the workspace
        if ((e.OriginalSource as FrameworkElement)?.DataContext is SidebarRowViewModel row)
            BeginRename(row);
    }

    private void OnListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.F2) return;
        if (RowsList.SelectedItems.OfType<SidebarRowViewModel>().FirstOrDefault() is { } row)
        {
            BeginRename(row);
            e.Handled = true;
        }
    }

    /// <summary>The rename_session hotkey: inline-rename the selected session row.</summary>
    public void BeginRenameSelected()
    {
        if (_items.FirstOrDefault(i => i.IsSession && i.Id == _snapshot?.SelectedSessionId) is { } row)
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

    private void OnRenameEditorLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box && (box.DataContext as SidebarRowViewModel) == _editing)
        {
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
        }
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

    // MARK: - context menus

    private void OnRowRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not SidebarRowViewModel row) return;
        var flyout = row.IsWorkspace ? WorkspaceMenu(row) : SessionMenu(row);
        flyout.ShowAt(RowsList, e.GetPosition(RowsList));
        e.Handled = true;
    }

    /// <summary>Mac list convention: right-click inside the multi-selection keeps it; outside
    /// narrows to the clicked row.</summary>
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
            () => _controller?.CloseSessions(targets)));
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
        flyout.Items.Add(Item("New Session", () => _controller?.NewSession(row.Id)));
        flyout.Items.Add(Item("Open Directory…", () => _ = OpenDirectoryIntoAsync(row.Id)));
        var focused = _snapshot?.FocusedWorkspaceId == row.Id;
        flyout.Items.Add(Item(focused ? "Unfocus" : "Focus",
            () => _controller?.FocusWorkspaceToggle(row.Id)));
        flyout.Items.Add(new MenuFlyoutSeparator());
        var delete = Item("Delete Workspace", () => _controller?.DeleteWorkspace(row.Id));
        delete.IsEnabled = _snapshot?.CanRemoveWorkspace == true;
        flyout.Items.Add(delete);
        return flyout;
    }

    private async Task OpenDirectoryIntoAsync(Guid workspaceId)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker,
            WinRT.Interop.WindowNative.GetWindowHandle(App.Window!));
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

    private void OnFocusPillClick(object sender, RoutedEventArgs e) => _controller?.ClearFocus();

    private void OnNewWorkspaceClick(object sender, RoutedEventArgs e) =>
        _controller?.NewWorkspace($"workspace {(_snapshot?.Workspaces.Count ?? 0) + 1}");

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
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
    }

    private void OnListDrop(object sender, DragEventArgs e)
    {
        if (_snapshot is not { } snapshot || _controller is null) return;
        var position = e.GetPosition(RowsList);

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
