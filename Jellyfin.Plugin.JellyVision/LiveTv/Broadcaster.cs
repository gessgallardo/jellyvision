using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyVision.LiveTv;

/// <summary>
/// Fans one producer's byte stream out to many concurrent readers.
/// </summary>
/// <remarks>
/// Live TV is a broadcast: every viewer of a channel should see the same bytes
/// from the same encoder. Subscribers each get their own bounded queue, so a
/// slow client cannot stall the encoder or the other viewers.
/// </remarks>
public sealed class Broadcaster : IDisposable
{
    private readonly object _gate = new();
    private readonly List<BroadcastSubscriber> _subscribers = [];

    /// <summary>
    /// Gets the number of attached subscribers.
    /// </summary>
    public int SubscriberCount
    {
        get
        {
            lock (_gate)
            {
                return _subscribers.Count;
            }
        }
    }

    /// <summary>
    /// Attaches a new subscriber.
    /// </summary>
    /// <param name="maxQueuedChunks">How many chunks may back up before the oldest are dropped.</param>
    /// <returns>The subscriber, which must be disposed when the client goes away.</returns>
    public BroadcastSubscriber Subscribe(int maxQueuedChunks = 256)
    {
        var subscriber = new BroadcastSubscriber(this, maxQueuedChunks);
        lock (_gate)
        {
            _subscribers.Add(subscriber);
        }

        return subscriber;
    }

    /// <summary>
    /// Publishes a chunk to every subscriber.
    /// </summary>
    /// <param name="buffer">The source buffer.</param>
    /// <param name="count">How many bytes of the buffer are valid.</param>
    public void Publish(byte[] buffer, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (count <= 0)
        {
            return;
        }

        // Copy once, share the copy: subscribers only ever read it.
        var chunk = new byte[count];
        Array.Copy(buffer, chunk, count);

        lock (_gate)
        {
            for (var i = 0; i < _subscribers.Count; i++)
            {
                _subscribers[i].Enqueue(chunk);
            }
        }
    }

    /// <summary>
    /// Signals that no more data will be published.
    /// </summary>
    public void Complete()
    {
        lock (_gate)
        {
            for (var i = 0; i < _subscribers.Count; i++)
            {
                _subscribers[i].Complete();
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        BroadcastSubscriber[] snapshot;
        lock (_gate)
        {
            snapshot = [.. _subscribers];
            _subscribers.Clear();
        }

        foreach (var subscriber in snapshot)
        {
            subscriber.Dispose();
        }
    }

    internal void Remove(BroadcastSubscriber subscriber)
    {
        lock (_gate)
        {
            _subscribers.Remove(subscriber);
        }
    }
}
