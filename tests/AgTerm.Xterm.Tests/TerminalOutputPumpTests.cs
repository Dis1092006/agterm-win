using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using AgTerm.Xterm;
using Xunit.Abstractions;

namespace AgTerm.Xterm.Tests;

public sealed class TerminalOutputPumpTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(5);

    private readonly ITestOutputHelper _output;

    public TerminalOutputPumpTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static TerminalOutputPump MakePump(
        Func<ReadOnlyMemory<byte>, Task> sink,
        int queueMaxBytes = 4096,
        int queueMaxItems = 256,
        int batchMaxBytes = 65536,
        TimeSpan? flushInterval = null)
    {
        var options = new TerminalOutputPumpOptions
        {
            QueueMaxBytes = queueMaxBytes,
            QueueMaxItems = queueMaxItems,
            BatchMaxBytes = batchMaxBytes,
            FlushInterval = flushInterval ?? TimeSpan.FromMilliseconds(20),
        };
        return new TerminalOutputPump(options, sink);
    }

    [Fact]
    public async Task Write_and_complete_deliver_exact_bytes_in_original_order()
    {
        var received = new List<byte[]>();
        Task Sink(ReadOnlyMemory<byte> data)
        {
            received.Add(data.ToArray());
            return Task.CompletedTask;
        }

        using var pump = MakePump(Sink);

        var chunk1 = Utf8("alpha");
        var chunk2 = Utf8("bravo");
        var chunk3 = Utf8("charlie");

        await pump.WriteAsync(chunk1);
        await pump.WriteAsync(chunk2);
        await pump.WriteAsync(chunk3);
        await pump.CompleteAsync().WaitAsync(ShortTimeout);

        var actual = received.SelectMany(b => b).ToArray();
        var expected = chunk1.Concat(chunk2).Concat(chunk3).ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Adjacent_small_chunks_are_coalesced_into_fewer_sink_calls()
    {
        var sinkCallCount = 0;
        var received = new List<byte[]>();
        Task Sink(ReadOnlyMemory<byte> data)
        {
            Interlocked.Increment(ref sinkCallCount);
            received.Add(data.ToArray());
            return Task.CompletedTask;
        }

        using var pump = MakePump(Sink,
            queueMaxBytes: 4096,
            queueMaxItems: 256,
            batchMaxBytes: 65536,
            flushInterval: TimeSpan.FromHours(1));

        await pump.WriteAsync(Utf8("A"));
        await pump.WriteAsync(Utf8("B"));
        await pump.WriteAsync(Utf8("C"));
        await pump.CompleteAsync().WaitAsync(ShortTimeout);

        Assert.InRange(sinkCallCount, 1, 2);
        Assert.Equal(Utf8("ABC"), received.SelectMany(b => b).ToArray());
    }

    [Fact]
    public async Task Producer_blocks_when_queue_is_full_and_all_bytes_arrive_in_order_after_release()
    {
        var sinkStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sinkGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new List<byte[]>();
        var sinkInvocation = 0;

        async Task Sink(ReadOnlyMemory<byte> data)
        {
            received.Add(data.ToArray());
            if (Interlocked.Increment(ref sinkInvocation) == 1)
            {
                sinkStarted.TrySetResult();
                await sinkGate.Task;
            }
        }

        using var pump = MakePump(Sink,
            queueMaxBytes: 4,
            queueMaxItems: 256,
            batchMaxBytes: 4,
            flushInterval: TimeSpan.FromMilliseconds(20));

        var chunk1 = new byte[] { 1, 2, 3, 4 };
        var chunk2 = new byte[] { 5, 6, 7, 8 };
        var chunk3 = new byte[] { 9, 10, 11, 12 };

        await pump.WriteAsync(chunk1);
        await sinkStarted.Task.WaitAsync(ShortTimeout);

        await pump.WriteAsync(chunk2);

        var blockedWrite = pump.WriteAsync(chunk3).AsTask();
        await Task.Yield();
        Assert.False(blockedWrite.IsCompleted);

        Assert.True(pump.ProducerBlockCount > 0);
        Assert.True(pump.MaxObservedQueueBytes <= 4);

        sinkGate.SetResult();
        await blockedWrite.WaitAsync(ShortTimeout);
        await pump.CompleteAsync().WaitAsync(ShortTimeout);

        var expected = chunk1.Concat(chunk2).Concat(chunk3).ToArray();
        Assert.Equal(expected, received.SelectMany(b => b).ToArray());
    }

    [Fact]
    public async Task Cancelling_blocked_write_token_releases_producer_without_losing_earlier_chunks()
    {
        var sinkStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sinkGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new List<byte[]>();
        var sinkInvocation = 0;

        async Task Sink(ReadOnlyMemory<byte> data)
        {
            received.Add(data.ToArray());
            if (Interlocked.Increment(ref sinkInvocation) == 1)
            {
                sinkStarted.TrySetResult();
                await sinkGate.Task;
            }
        }

        using var pump = MakePump(Sink,
            queueMaxBytes: 4,
            queueMaxItems: 256,
            batchMaxBytes: 4,
            flushInterval: TimeSpan.FromMilliseconds(20));

        var chunk1 = new byte[] { 1, 2, 3, 4 };
        var chunk2 = new byte[] { 5, 6, 7, 8 };
        var chunk3 = new byte[] { 9, 10, 11, 12 };

        await pump.WriteAsync(chunk1);
        await sinkStarted.Task.WaitAsync(ShortTimeout);
        await pump.WriteAsync(chunk2);

        using var cts = new CancellationTokenSource();
        var blockedWrite = pump.WriteAsync(chunk3, cts.Token).AsTask();
        await Task.Yield();
        Assert.False(blockedWrite.IsCompleted);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            blockedWrite.WaitAsync(ShortTimeout));

        sinkGate.SetResult();
        await pump.CompleteAsync().WaitAsync(ShortTimeout);

        var expected12 = chunk1.Concat(chunk2).ToArray();
        Assert.Equal(expected12, received.SelectMany(b => b).ToArray());
    }

    [Fact]
    public async Task Dispose_releases_a_blocked_producer_with_cancellation_or_disposed_exception()
    {
        var sinkStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sinkGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new List<byte[]>();
        var sinkInvocation = 0;

        async Task Sink(ReadOnlyMemory<byte> data)
        {
            received.Add(data.ToArray());
            if (Interlocked.Increment(ref sinkInvocation) == 1)
            {
                sinkStarted.TrySetResult();
                await sinkGate.Task;
            }
        }

        var pump = MakePump(Sink,
            queueMaxBytes: 4,
            queueMaxItems: 256,
            batchMaxBytes: 4,
            flushInterval: TimeSpan.FromMilliseconds(20));

        var chunk1 = new byte[] { 1, 2, 3, 4 };
        var chunk2 = new byte[] { 5, 6, 7, 8 };
        var chunk3 = new byte[] { 9, 10, 11, 12 };

        await pump.WriteAsync(chunk1);
        await sinkStarted.Task.WaitAsync(ShortTimeout);
        await pump.WriteAsync(chunk2);

        var blockedWrite = pump.WriteAsync(chunk3).AsTask();
        await Task.Yield();
        Assert.False(blockedWrite.IsCompleted);

        pump.Dispose();
        sinkGate.SetResult();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            blockedWrite.WaitAsync(ShortTimeout));
        Assert.True(ex is OperationCanceledException or ObjectDisposedException,
            $"expected OperationCanceledException or ObjectDisposedException, got {ex.GetType().Name}");
    }

    [Fact]
    public async Task CompleteAsync_flushes_pending_bytes_even_when_flush_interval_is_very_long()
    {
        var received = new List<byte[]>();
        Task Sink(ReadOnlyMemory<byte> data)
        {
            received.Add(data.ToArray());
            return Task.CompletedTask;
        }

        using var pump = MakePump(Sink,
            queueMaxBytes: 4096,
            queueMaxItems: 256,
            batchMaxBytes: 65536,
            flushInterval: TimeSpan.FromHours(1));

        var chunk = Utf8("final-flush");
        await pump.WriteAsync(chunk);
        await pump.CompleteAsync().WaitAsync(ShortTimeout);

        Assert.Single(received);
        Assert.Equal(chunk, received[0]);
    }

    [Fact]
    public async Task Two_chunks_crossing_batch_boundary_deliver_all_bytes_without_data_loss()
    {
        var received = new List<byte[]>();
        Task Sink(ReadOnlyMemory<byte> data)
        {
            received.Add(data.ToArray());
            return Task.CompletedTask;
        }

        using var pump = MakePump(Sink,
            queueMaxBytes: 16,
            queueMaxItems: 256,
            batchMaxBytes: 6,
            flushInterval: TimeSpan.FromSeconds(1));

        var chunk1 = new byte[] { 1, 2, 3, 4 };
        var chunk2 = new byte[] { 5, 6, 7, 8 };

        await pump.WriteAsync(chunk1);
        await pump.WriteAsync(chunk2);
        await pump.CompleteAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.All(received, batch => Assert.True(batch.Length <= 6,
            $"Batch of {batch.Length} bytes exceeded BatchMaxBytes=6"));
        var allBytes = received.SelectMany(b => b).ToArray();
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, allBytes);
    }

    [Fact]
    public async Task Ten_megabytes_through_pump_delivers_exact_bytes_via_sha256()
    {
        const int totalBytes = 10 * 1024 * 1024;
        const int chunkSize = 4096;

        var source = new byte[totalBytes];
        for (var i = 0; i < totalBytes; i++)
            source[i] = (byte)(((i * 17) ^ (i >> 3)) & 0xFF);

        var expectedHash = SHA256.HashData(source);

        var options = new TerminalOutputPumpOptions();
        using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long receivedBytes = 0;
        var flushCount = 0;

        async Task Sink(ReadOnlyMemory<byte> data)
        {
            Assert.True(data.Length <= options.BatchMaxBytes,
                $"Batch length {data.Length} exceeds BatchMaxBytes {options.BatchMaxBytes}");
            incrementalHash.AppendData(data.Span);
            Interlocked.Add(ref receivedBytes, data.Length);
            flushCount++;
            await Task.Yield();
        }

        var sw = Stopwatch.StartNew();
        await using var pump = new TerminalOutputPump(options, Sink);

        for (var offset = 0; offset < totalBytes; offset += chunkSize)
        {
            var length = Math.Min(chunkSize, totalBytes - offset);
            await pump.WriteAsync(source.AsMemory(offset, length));
        }

        await pump.CompleteAsync().WaitAsync(TimeSpan.FromSeconds(30));
        sw.Stop();

        Assert.Equal((long)totalBytes, Interlocked.Read(ref receivedBytes));
        Assert.Equal(expectedHash, incrementalHash.GetHashAndReset());
        Assert.True(pump.MaxObservedQueueBytes <= options.QueueMaxBytes,
            $"MaxObservedQueueBytes {pump.MaxObservedQueueBytes} exceeded QueueMaxBytes {options.QueueMaxBytes}");

        var elapsed = sw.Elapsed.TotalSeconds;
        var throughput = totalBytes / (1024.0 * 1024.0) / elapsed;
        _output.WriteLine(
            $"QueueMaxBytes={options.QueueMaxBytes} BatchMaxBytes={options.BatchMaxBytes} " +
            $"FlushInterval={options.FlushInterval.TotalMilliseconds}ms " +
            $"ProducerBlockCount={pump.ProducerBlockCount} " +
            $"MaxObservedQueueBytes={pump.MaxObservedQueueBytes} " +
            $"FlushCount={flushCount} " +
            $"Elapsed={elapsed:F2}s " +
            $"Throughput={throughput:F1}MiB/s");
    }

    [Fact]
    public async Task DisposeAsync_does_not_wait_for_a_sink_that_never_completes()
    {
        var sinkStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sinkGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task Sink(ReadOnlyMemory<byte> data)
        {
            sinkStarted.TrySetResult();
            await sinkGate.Task; // never completes — simulates a UI callback that stalls
        }

        var pump = MakePump(Sink,
            queueMaxBytes: 4,
            queueMaxItems: 256,
            batchMaxBytes: 4,
            flushInterval: TimeSpan.Zero);

        var chunk = new byte[] { 1, 2, 3, 4 };
        await pump.WriteAsync(chunk);
        await sinkStarted.Task.WaitAsync(ShortTimeout);

        var disposeTask = pump.DisposeAsync().AsTask();
        await disposeTask.WaitAsync(ShortTimeout);
    }

    [Fact]
    public async Task ObjectDisposedException_from_sink_unblocks_blocked_producer_and_faults_CompleteAsync()
    {
        var sinkStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sinkGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task Sink(ReadOnlyMemory<byte> data)
        {
            sinkStarted.TrySetResult();
            await sinkGate.Task;
            throw new ObjectDisposedException("renderer");
        }

        var pump = MakePump(Sink,
            queueMaxBytes: 4,
            queueMaxItems: 256,
            batchMaxBytes: 4,
            flushInterval: TimeSpan.FromMilliseconds(20));

        try
        {
            var chunk1 = new byte[] { 1, 2, 3, 4 };
            var chunk2 = new byte[] { 5, 6, 7, 8 };
            var chunk3 = new byte[] { 9, 10, 11, 12 };

            await pump.WriteAsync(chunk1);
            await sinkStarted.Task.WaitAsync(ShortTimeout);

            await pump.WriteAsync(chunk2);

            var blockedWrite = pump.WriteAsync(chunk3).AsTask();
            await Task.Yield();
            Assert.False(blockedWrite.IsCompleted);

            sinkGate.SetResult();

            var writeEx = await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                blockedWrite.WaitAsync(ShortTimeout));
            Assert.Equal("renderer", writeEx.ObjectName);

            var completeEx = await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                pump.CompleteAsync().WaitAsync(ShortTimeout));
            Assert.Equal("renderer", completeEx.ObjectName);
        }
        finally
        {
            pump.Dispose();
        }
    }

    [Fact]
    public async Task DisposeAsync_propagates_unobserved_sink_failure()
    {
        var sinkCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task Sink(ReadOnlyMemory<byte> _)
        {
            sinkCalled.TrySetResult();
            return Task.FromException(new InvalidOperationException("dispose sink failed"));
        }

        var pump = MakePump(Sink,
            queueMaxBytes: 4,
            queueMaxItems: 256,
            batchMaxBytes: 4,
            flushInterval: TimeSpan.Zero);

        await pump.WriteAsync(new byte[] { 1, 2, 3, 4 });
        await sinkCalled.Task.WaitAsync(ShortTimeout);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => pump.DisposeAsync().AsTask().WaitAsync(ShortTimeout));
        Assert.Equal("dispose sink failed", ex.Message);
    }

    [Fact]
    public async Task Blocked_producer_unblocks_with_exception_when_consumer_faults()
    {
        var sinkStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sinkGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task Sink(ReadOnlyMemory<byte> data)
        {
            sinkStarted.TrySetResult();
            await sinkGate.Task;
            throw new InvalidOperationException("sink failed");
        }

        var pump = MakePump(Sink,
            queueMaxBytes: 4,
            queueMaxItems: 256,
            batchMaxBytes: 4,
            flushInterval: TimeSpan.FromMilliseconds(20));

        try
        {
            var chunk1 = new byte[] { 1, 2, 3, 4 };
            var chunk2 = new byte[] { 5, 6, 7, 8 };
            var chunk3 = new byte[] { 9, 10, 11, 12 };

            await pump.WriteAsync(chunk1);
            await sinkStarted.Task.WaitAsync(ShortTimeout);

            await pump.WriteAsync(chunk2);

            var blockedWrite = pump.WriteAsync(chunk3).AsTask();
            await Task.Yield();
            Assert.False(blockedWrite.IsCompleted);

            sinkGate.SetResult();

            var writeEx = await Assert.ThrowsAnyAsync<Exception>(() =>
                blockedWrite.WaitAsync(ShortTimeout));
            Assert.IsNotType<TimeoutException>(writeEx);

            var completeEx = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                pump.CompleteAsync().WaitAsync(ShortTimeout));
            Assert.Equal("sink failed", completeEx.Message);
        }
        finally
        {
            pump.Dispose();
        }
    }
}
