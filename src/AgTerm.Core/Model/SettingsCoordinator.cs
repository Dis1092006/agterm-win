using System.Diagnostics;

namespace AgTerm.Core.Model;

public enum SettingsApplyKind { Startup, Reload, Edit, Theme }

public sealed record SettingsApplicationState(SettingsSnapshot Snapshot, bool IsApplying, Exception? Error);

public sealed class SettingsApplyException(SettingsSnapshot snapshot, IReadOnlyList<Exception> errors)
    : AggregateException("Settings were saved or loaded, but could not be applied to every window.", errors)
{
    public SettingsSnapshot Snapshot { get; } = snapshot;
}

/// <summary>One application-wide sequence of disk updates and awaited window applications.
/// A failed apply retains the committed snapshot so the UI can distinguish it from a failed save.</summary>
public sealed class SettingsCoordinator
{
    private readonly SettingsStore store;
    private readonly SemaphoreSlim operations = new(1, 1);
    private readonly Lock subscribersGate = new();
    private readonly Dictionary<Guid, Func<SettingsSnapshot, SettingsApplyKind, Task>> subscribers = new();
    private SettingsApplicationState state;

    public SettingsCoordinator(SettingsStore store)
    {
        this.store = store;
        try { state = new(store.ReadStrict(), false, null); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or System.Text.Json.JsonException or NotSupportedException)
        {
            state = new(new SettingsSnapshot(store.FilePath, null, store.Load()), false, ex);
        }
    }

    public SettingsApplicationState State => Volatile.Read(ref state);
    internal SettingsSnapshot ReadStrict() => store.ReadStrict();
    public event Action<SettingsApplicationState>? Changed;
    public bool HasSubscribers { get { lock (subscribersGate) return subscribers.Count > 0; } }

    public async Task<IDisposable> SubscribeAsync(
        Func<SettingsSnapshot, SettingsApplyKind, Task> apply,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(apply);
        await operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await apply(State.Snapshot, SettingsApplyKind.Startup).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var id = Guid.NewGuid();
            lock (subscribersGate) subscribers.Add(id, apply);
            return new Subscription(this, id);
        }
        finally { operations.Release(); }
    }

    public Task<SettingsSnapshot> UpdateAsync(
        SettingsSnapshot baseline,
        Func<AppSettings, AppSettings> change,
        SettingsApplyKind kind = SettingsApplyKind.Edit,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => store.Update(baseline, change), kind, cancellationToken);

    public Task<SettingsSnapshot> ReloadAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(store.ReadStrict, SettingsApplyKind.Reload, cancellationToken);

    public Task<SettingsSnapshot> UpdateCurrentAsync(
        Func<AppSettings, AppSettings> change,
        SettingsApplyKind kind = SettingsApplyKind.Edit,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => store.UpdateCurrent(change), kind, cancellationToken);

    private async Task<SettingsSnapshot> ExecuteAsync(
        Func<SettingsSnapshot> readOrUpdate,
        SettingsApplyKind kind,
        CancellationToken cancellationToken)
    {
        await operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SettingsSnapshot snapshot;
            try
            {
                snapshot = await Task.Run(readOrUpdate, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (kind == SettingsApplyKind.Theme)
                {
                    // theme.set updates command readback before its asynchronous save. Restore it
                    // under this operation's gate so a failed save cannot overwrite a later success.
                    var rollbackErrors = await ApplyToSubscribersAsync(State.Snapshot, kind).ConfigureAwait(false);
                    if (rollbackErrors.Count > 0)
                    {
                        rollbackErrors.Insert(0, ex);
                        var rollbackFailure = new AggregateException(
                            "Theme was not saved and the previous settings could not be applied to every window.", rollbackErrors);
                        Publish(State with { IsApplying = false, Error = rollbackFailure });
                        throw rollbackFailure;
                    }
                }
                Publish(State with { IsApplying = false, Error = ex });
                throw;
            }

            Publish(new(snapshot, true, null));
            // Once a write succeeds, finish the fan-out even if the requesting UI closes.
            var errors = await ApplyToSubscribersAsync(snapshot, kind).ConfigureAwait(false);
            var failure = errors.Count == 0 ? null : new SettingsApplyException(snapshot, errors.AsReadOnly());
            Publish(new(snapshot, false, failure));
            if (failure is not null) throw failure;
            return snapshot;
        }
        finally { operations.Release(); }
    }

    private async Task<List<Exception>> ApplyToSubscribersAsync(SettingsSnapshot snapshot, SettingsApplyKind kind)
    {
        Func<SettingsSnapshot, SettingsApplyKind, Task>[] targets;
        lock (subscribersGate) targets = subscribers.Values.ToArray();
        var errors = new List<Exception>();
        foreach (var apply in targets)
        {
            try { await apply(snapshot, kind).ConfigureAwait(false); }
            catch (Exception ex) { errors.Add(ex); }
        }
        return errors;
    }

    private void Publish(SettingsApplicationState next)
    {
        Volatile.Write(ref state, next);
        if (Changed is not { } changed) return;
        foreach (Action<SettingsApplicationState> observer in changed.GetInvocationList())
        {
            try { observer(next); }
            catch (Exception ex) { Trace.TraceError($"Settings observer failed: {ex}"); }
        }
    }

    private sealed class Subscription(SettingsCoordinator owner, Guid id) : IDisposable
    {
        public void Dispose()
        {
            lock (owner.subscribersGate) owner.subscribers.Remove(id);
        }
    }
}
