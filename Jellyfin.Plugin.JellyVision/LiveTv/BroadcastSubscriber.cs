using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyVision.LiveTv;

/// <summary>
/// One reader of a <see cref="Broadcaster"/> stream.
/// </summary>
/// <remarks>
/// Each reader owns a bounded queue. A client that cannot keep up drops its
/// oldest chunks instead of stalling the encoder, and resynchronises on the
/// next MPEG-TS key frame.
/// </remarks>
public sealed class BroadcastSubscriber : IDisposable
{
    private readonly Broadcaster _owner;
    private readonly int _maxQueued;
    private readonly Queue<byte[]> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly object _queueGate = new();
    private bool _done;
    private bool _disposed;

    internal BroadcastSubscriber(Broadcaster owner, int maxQueued)
    {
        _owner = owner;
        _maxQueued = maxQueued;
    }

    /// <summary>
    /// Gets the number of chunks dropped because this reader fell behind.
    /// </summary>
    public long DroppedChunks { get; private set; }

    /// <summary>
    /// Waits for the next chunk.
    /// </summary>
    /// <param name="cancellationToken">Cancelled when the client disconnects.</param>
    /// <returns>The next chunk, or null once the stream has ended.</returns>
    public async Task<byte[]?> ReadAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            lock (_queueGate)
            {
                if (_queue.Count > 0)
                {
                    return _queue.Dequeue();
                }

                if (_done)
                {
                    return null;
                }
            }

            await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    internal void Enqueue(byte[] chunk)
    {
        lock (_queueGate)
        {
            if (_done)
            {
                return;
            }

            while (_queue.Count >= _maxQueued)
            {
                _queue.Dequeue();
                DroppedChunks++;
            }

            _queue.Enqueue(chunk);
        }

        TrySignal();
    }

    internal void Complete()
    {
        lock (_queueGate)
        {
            _done = true;
        }

        TrySignal();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _owner.Remove(this);
        Complete();
        _disposed = true;
        _signal.Dispose();
    }

    private void TrySignal()
    {
        try
        {
            _signal.Release();
        }
        catch (ObjectDisposedException)
        {
            // Reader already went away.
        }
    }
}
