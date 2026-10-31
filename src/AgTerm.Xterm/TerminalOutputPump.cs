using System.Threading.Channels;
using System.Runtime.ExceptionServices;

namespace AgTerm.Xterm;

public sealed record TerminalOutputPumpOptions
{
    public int QueueMaxBytes { get; init; } = 1 * 1024 * 1024;
    public int QueueMaxItems { get; init; } = 256;
    public int BatchMaxBytes { get; init; } = 64 * 1024;
    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromMilliseconds(8);
}

public sealed class TerminalOutputPump : IDisposable, IAsyncDisposable
{
    private readonly TerminalOutputPumpOptions _options;
    private readonly Func<ReadOnlyMemory<byte>, Task> _sink;
    private readonly Channel<byte[]> _channel;
    private readonly Lock _gate = new();
    private readonly TaskCompletionSource _flushWake = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _disposeCts = new();

    private readonly Task _consumerTask;

    private bool _completionStarted;
    private bool _disposed;
    private int _reservedQueueBytes;
    private int _reservedQueueItems;
    private long _producerBlockCount;
    private int _maxObservedQueueBytes;
    private int _disposeCtsDisposed;
    private List<TaskCompletionSource<bool>>? _budgetWaiters;
    private ExceptionDispatchInfo? _consumerFailure;

    public TerminalOutputPump(TerminalOutputPumpOptions options, Func<ReadOnlyMemory<byte>, Task> sink)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sink);

        if (options.QueueMaxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.QueueMaxBytes), "QueueMaxBytes must be positive.");
        }

        if (options.QueueMaxItems <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.QueueMaxItems), "QueueMaxItems must be positive.");
        }

        if (options.BatchMaxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.BatchMaxBytes), "BatchMaxBytes must be positive.");
        }

        if (options.FlushInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options.FlushInterval), "FlushInterval must be nonnegative.");
        }

        _options = options;
        _sink = sink;

        _channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(options.QueueMaxItems)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });

        _consumerTask = RunConsumerAsync();
        _consumerTask.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        _consumerTask.ContinueWith(
            static (_, state) => ((TerminalOutputPump)state!).DisposeCancellationSource(),
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public long ProducerBlockCount => Interlocked.Read(ref _producerBlockCount);

    public int MaxObservedQueueBytes
    {
        get
        {
            lock (_gate)
            {
                return _maxObservedQueueBytes;
            }
        }
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        if (bytes.Length == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfWriteNotAllowed();
            return;
        }

        var maxSegmentSize = Math.Min(_options.QueueMaxBytes, _options.BatchMaxBytes);
        var offset = 0;

        while (offset < bytes.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var length = Math.Min(maxSegmentSize, bytes.Length - offset);
            var segment = bytes.Slice(offset, length).ToArray();
            offset += length;

            await ReserveBudgetAndWriteAsync(segment, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        bool shouldSignal;
        lock (_gate)
        {
            ThrowIfDisposedNoLock();
            shouldSignal = !_completionStarted;
            _completionStarted = true;
            WakeBudgetWaitersNoLock();
        }

        if (shouldSignal)
        {
            _flushWake.TrySetResult();
            _channel.Writer.TryComplete();
        }

        await _consumerTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        bool alreadyDisposed;
        lock (_gate)
        {
            alreadyDisposed = _disposed;
            _disposed = true;
            _completionStarted = true;
            WakeBudgetWaitersNoLock();
        }

        if (alreadyDisposed)
        {
            return;
        }

        _flushWake.TrySetResult();
        _channel.Writer.TryComplete();
        try
        {
            _disposeCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();

        try
        {
            await _consumerTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        DisposeCancellationSource();
    }

    private async ValueTask ReserveBudgetAndWriteAsync(byte[] segment, CancellationToken cancellationToken)
    {
        await ReserveBudgetAsync(segment.Length, cancellationToken).ConfigureAwait(false);

        try
        {
            await _channel.Writer.WriteAsync(segment, cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            // Dispose or a consumer failure closed the channel after the budget check: report it the
            // way a write that starts after it does
            ReleaseReservation(segment.Length, signalWaiters: true);
            ThrowIfWriteNotAllowed();
            throw;
        }
        catch
        {
            ReleaseReservation(segment.Length, signalWaiters: true);
            throw;
        }
    }

    private async ValueTask ReserveBudgetAsync(int bytes, CancellationToken cancellationToken)
    {
        TaskCompletionSource<bool>? waiter = null;
        var countedBlock = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                ThrowIfWriteNotAllowedNoLock();

                if (CanReserveNoLock(bytes))
                {
                    _reservedQueueBytes += bytes;
                    _reservedQueueItems += 1;
                    if (_reservedQueueBytes > _maxObservedQueueBytes)
                    {
                        _maxObservedQueueBytes = _reservedQueueBytes;
                    }

                    return;
                }

                waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                (_budgetWaiters ??= new List<TaskCompletionSource<bool>>()).Add(waiter);
                if (!countedBlock)
                {
                    Interlocked.Increment(ref _producerBlockCount);
                    countedBlock = true;
                }
            }

            try
            {
                await waiter.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                lock (_gate)
                {
                    _budgetWaiters?.Remove(waiter);
                }

                throw;
            }
        }
    }

    private bool CanReserveNoLock(int bytes)
    {
        return _reservedQueueBytes + bytes <= _options.QueueMaxBytes
            && _reservedQueueItems + 1 <= _options.QueueMaxItems;
    }

    private void ReleaseReservation(int bytes, bool signalWaiters)
    {
        lock (_gate)
        {
            _reservedQueueBytes -= bytes;
            _reservedQueueItems -= 1;

            if (_reservedQueueBytes < 0)
            {
                _reservedQueueBytes = 0;
            }

            if (_reservedQueueItems < 0)
            {
                _reservedQueueItems = 0;
            }

            if (signalWaiters)
            {
                WakeBudgetWaitersNoLock();
            }
        }
    }

    private void DisposeCancellationSource()
    {
        if (Interlocked.Exchange(ref _disposeCtsDisposed, 1) == 0)
        {
            _disposeCts.Dispose();
        }
    }

    private async Task RunConsumerAsync()
    {
        var reader = _channel.Reader;
        var batchBuffer = new byte[_options.BatchMaxBytes];
        ReadOnlyMemory<byte> carry = ReadOnlyMemory<byte>.Empty;

        try
        {
            while (true)
            {
                if (carry.IsEmpty)
                {
                    var hasData = await reader.WaitToReadAsync(_disposeCts.Token).ConfigureAwait(false);
                    if (!hasData)
                    {
                        break;
                    }
                }

                var batchCount = 0;
                DrainAvailable(reader, batchBuffer, ref batchCount, ref carry);

                if (batchCount == 0)
                {
                    continue;
                }

                if (batchCount < _options.BatchMaxBytes && carry.IsEmpty)
                {
                    await WaitForCoalesceWindowAsync(reader).ConfigureAwait(false);
                    DrainAvailable(reader, batchBuffer, ref batchCount, ref carry);
                }

                var sinkTask = _sink(batchBuffer.AsMemory(0, batchCount));
                try
                {
                    await sinkTask.WaitAsync(_disposeCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested && !sinkTask.IsCompleted)
                {
                    _ = sinkTask.ContinueWith(
                        static t => _ = t.Exception,
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                    throw;
                }
            }
        }
        catch (OperationCanceledException) when (_disposed)
        {
        }
        catch (ObjectDisposedException) when (_disposed)
        {
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _consumerFailure = ExceptionDispatchInfo.Capture(ex);
                _completionStarted = true;
                WakeBudgetWaitersNoLock();
            }

            _channel.Writer.TryComplete(ex);
            _flushWake.TrySetResult();
            throw;
        }
        finally
        {
            while (reader.TryRead(out var dropped))
            {
                ReleaseReservation(dropped.Length, signalWaiters: true);
            }
        }
    }

    private async Task WaitForCoalesceWindowAsync(ChannelReader<byte[]> reader)
    {
        if (_options.FlushInterval == TimeSpan.Zero)
        {
            return;
        }

        var readReadyTask = reader.WaitToReadAsync(_disposeCts.Token).AsTask();
        var delayTask = Task.Delay(_options.FlushInterval, _disposeCts.Token);

        await Task.WhenAny(readReadyTask, delayTask, _flushWake.Task).ConfigureAwait(false);
    }

    private void DrainAvailable(ChannelReader<byte[]> reader, byte[] batchBuffer, ref int batchCount, ref ReadOnlyMemory<byte> carry)
    {
        if (!carry.IsEmpty && batchCount < _options.BatchMaxBytes)
        {
            var carryCopyLength = Math.Min(carry.Length, _options.BatchMaxBytes - batchCount);
            carry.Span.Slice(0, carryCopyLength).CopyTo(batchBuffer.AsSpan(batchCount));
            batchCount += carryCopyLength;
            carry = carry.Slice(carryCopyLength);
        }

        while (batchCount < _options.BatchMaxBytes && reader.TryRead(out var chunk))
        {
            var copyLength = Math.Min(chunk.Length, _options.BatchMaxBytes - batchCount);
            chunk.AsSpan(0, copyLength).CopyTo(batchBuffer.AsSpan(batchCount));
            batchCount += copyLength;
            ReleaseReservation(chunk.Length, signalWaiters: true);

            if (copyLength < chunk.Length)
            {
                carry = chunk.AsMemory(copyLength);
                return;
            }
        }
    }

    private void ThrowIfWriteNotAllowed()
    {
        lock (_gate)
        {
            ThrowIfWriteNotAllowedNoLock();
        }
    }

    private void ThrowIfWriteNotAllowedNoLock()
    {
        ThrowIfDisposedNoLock();

        if (_consumerFailure is not null)
        {
            _consumerFailure.Throw();
        }

        if (_completionStarted)
        {
            throw new InvalidOperationException("The pump is completing and does not accept new writes.");
        }
    }

    private void ThrowIfDisposedNoLock()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TerminalOutputPump));
        }
    }

    private void WakeBudgetWaitersNoLock()
    {
        if (_budgetWaiters is not { Count: > 0 })
        {
            return;
        }

        foreach (var waiter in _budgetWaiters)
        {
            waiter.TrySetResult(true);
        }

        _budgetWaiters.Clear();
    }
}
