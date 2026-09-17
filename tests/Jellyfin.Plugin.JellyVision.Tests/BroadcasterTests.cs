using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyVision.LiveTv;
using Xunit;

namespace Jellyfin.Plugin.JellyVision.Tests;

/// <summary>
/// The broadcaster is what makes one encoder serve many viewers, so its
/// fan-out and back-pressure behaviour is worth pinning down.
/// </summary>
public class BroadcasterTests
{
    private static byte[] Chunk(params byte[] bytes) => bytes;

    [Fact]
    public async Task EverySubscriberReceivesEveryChunk()
    {
        using var broadcaster = new Broadcaster();
        using var a = broadcaster.Subscribe();
        using var b = broadcaster.Subscribe();

        broadcaster.Publish(Chunk(1, 2, 3), 3);
        broadcaster.Publish(Chunk(4, 5), 2);
        broadcaster.Complete();

        Assert.Equal([1, 2, 3], await a.ReadAsync(CancellationToken.None));
        Assert.Equal([4, 5], await a.ReadAsync(CancellationToken.None));
        Assert.Null(await a.ReadAsync(CancellationToken.None));

        Assert.Equal([1, 2, 3], await b.ReadAsync(CancellationToken.None));
        Assert.Equal([4, 5], await b.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SubscriberJoiningLate_OnlyGetsSubsequentChunks()
    {
        // Live TV has no rewind: a viewer tuning in joins the broadcast in
        // progress rather than replaying what it missed.
        using var broadcaster = new Broadcaster();
        using var early = broadcaster.Subscribe();

        broadcaster.Publish(Chunk(1), 1);

        using var late = broadcaster.Subscribe();
        broadcaster.Publish(Chunk(2), 1);
        broadcaster.Complete();

        Assert.Equal([1], await early.ReadAsync(CancellationToken.None));
        Assert.Equal([2], await early.ReadAsync(CancellationToken.None));

        Assert.Equal([2], await late.ReadAsync(CancellationToken.None));
        Assert.Null(await late.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SlowSubscriber_DropsOldestInsteadOfBlocking()
    {
        using var broadcaster = new Broadcaster();
        using var slow = broadcaster.Subscribe(maxQueuedChunks: 4);

        for (var i = 0; i < 10; i++)
        {
            broadcaster.Publish(Chunk((byte)i), 1);
        }

        broadcaster.Complete();

        var received = new List<byte>();
        while (await slow.ReadAsync(CancellationToken.None) is { } chunk)
        {
            received.Add(chunk[0]);
        }

        // Only the newest four survive, and the drop is accounted for.
        Assert.Equal([6, 7, 8, 9], received);
        Assert.Equal(6, slow.DroppedChunks);
    }

    [Fact]
    public async Task OneSlowSubscriber_DoesNotStarveAFastOne()
    {
        using var broadcaster = new Broadcaster();
        using var slow = broadcaster.Subscribe(maxQueuedChunks: 2);
        using var fast = broadcaster.Subscribe(maxQueuedChunks: 1000);

        for (var i = 0; i < 20; i++)
        {
            broadcaster.Publish(Chunk((byte)i), 1);
        }

        broadcaster.Complete();

        var fastCount = 0;
        while (await fast.ReadAsync(CancellationToken.None) is not null)
        {
            fastCount++;
        }

        Assert.Equal(20, fastCount);
        Assert.True(slow.DroppedChunks > 0, "the slow reader should have dropped data");
    }

    [Fact]
    public async Task DisposingASubscriber_DetachesItFromTheBroadcast()
    {
        using var broadcaster = new Broadcaster();
        var a = broadcaster.Subscribe();
        using var b = broadcaster.Subscribe();

        Assert.Equal(2, broadcaster.SubscriberCount);

        a.Dispose();
        Assert.Equal(1, broadcaster.SubscriberCount);

        broadcaster.Publish(Chunk(7), 1);
        broadcaster.Complete();

        Assert.Equal([7], await b.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadBlocksUntilDataArrives()
    {
        using var broadcaster = new Broadcaster();
        using var subscriber = broadcaster.Subscribe();

        var read = subscriber.ReadAsync(CancellationToken.None);
        Assert.False(read.IsCompleted, "read should wait for the encoder");

        broadcaster.Publish(Chunk(42), 1);
        Assert.Equal([42], await read);
    }

    [Fact]
    public async Task CancellingAReader_StopsItWithoutAffectingOthers()
    {
        using var broadcaster = new Broadcaster();
        using var cancelled = broadcaster.Subscribe();
        using var healthy = broadcaster.Subscribe();
        using var cts = new CancellationTokenSource();

        var pending = cancelled.ReadAsync(cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        broadcaster.Publish(Chunk(9), 1);
        Assert.Equal([9], await healthy.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentPublishAndRead_LosesNothingForAFastReader()
    {
        using var broadcaster = new Broadcaster();
        using var subscriber = broadcaster.Subscribe(maxQueuedChunks: 10000);

        const int Total = 2000;

        var reader = Task.Run(async () =>
        {
            var count = 0;
            while (await subscriber.ReadAsync(CancellationToken.None) is not null)
            {
                count++;
            }

            return count;
        });

        await Task.Run(() =>
        {
            for (var i = 0; i < Total; i++)
            {
                broadcaster.Publish(Chunk(1), 1);
            }

            broadcaster.Complete();
        });

        Assert.Equal(Total, await reader);
    }

    [Fact]
    public void PublishIgnoresEmptyWrites()
    {
        using var broadcaster = new Broadcaster();
        using var subscriber = broadcaster.Subscribe();

        broadcaster.Publish([], 0);
        broadcaster.Publish(Chunk(1), 0);
        broadcaster.Complete();

        Assert.Equal(1, broadcaster.SubscriberCount);
    }

    [Fact]
    public void PublishCopiesTheBuffer_SoReuseCannotCorruptQueuedData()
    {
        // The encoder reuses its read buffer, so the broadcaster must not keep
        // a reference to it.
        using var broadcaster = new Broadcaster();
        using var subscriber = broadcaster.Subscribe();

        var buffer = new byte[] { 1, 2, 3 };
        broadcaster.Publish(buffer, 3);

        buffer[0] = 99;
        broadcaster.Complete();

        var received = subscriber.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal([1, 2, 3], received);
    }
}
