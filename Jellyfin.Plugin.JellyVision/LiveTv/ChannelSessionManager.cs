using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyVision.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyVision.LiveTv;

/// <summary>
/// Keeps at most one encoder running per channel and fans its output out to
/// every viewer.
/// </summary>
/// <remarks>
/// Without this, each client opening a channel spawns its own ffmpeg: N
/// viewers cost N encoders, and because each starts at its own wall-clock
/// moment they are not even showing the same frame. A shared session means one
/// encoder per channel, a genuinely common broadcast, and CPU that scales with
/// channels rather than viewers.
/// </remarks>
public sealed class ChannelSessionManager : IDisposable
{
    private readonly ConcurrentDictionary<string, ChannelSession> _sessions = new(StringComparer.Ordinal);
    private readonly ChannelStreamer _streamer;
    private readonly ILogger<ChannelSessionManager> _logger;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelSessionManager"/> class.
    /// </summary>
    /// <param name="streamer">The underlying encoder driver.</param>
    /// <param name="logger">The logger.</param>
    public ChannelSessionManager(ChannelStreamer streamer, ILogger<ChannelSessionManager> logger)
    {
        _streamer = streamer;
        _logger = logger;
    }

    /// <summary>
    /// Gets the number of channels currently being encoded.
    /// </summary>
    public int ActiveSessions => _sessions.Count;

    /// <summary>
    /// Streams a channel to one client, starting the channel's encoder if it is
    /// not already running.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="output">The response body.</param>
    /// <param name="cancellationToken">Cancelled when this client disconnects.</param>
    /// <returns>A task that completes when this client stops reading.</returns>
    public async Task StreamAsync(
        ChannelConfig channel,
        Stream output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(output);

        var session = _sessions.GetOrAdd(
            channel.Id,
            _ => new ChannelSession(channel, _streamer, _logger));

        using var subscriber = session.Subscribe();
        _logger.LogInformation(
            "Client joined channel {Channel}; {Count} viewer(s) on this encoder",
            channel.Name,
            session.ViewerCount);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var chunk = await subscriber.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (chunk is null)
                {
                    break;
                }

                await output.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Client went away.
        }
        finally
        {
            if (subscriber.DroppedChunks > 0)
            {
                _logger.LogWarning(
                    "A viewer of {Channel} fell behind and dropped {Count} chunks",
                    channel.Name,
                    subscriber.DroppedChunks);
            }

            // Last one out turns off the encoder.
            if (session.Release() == 0)
            {
                _sessions.TryRemove(channel.Id, out _);
                session.Dispose();
                _logger.LogInformation("Last viewer left {Channel}; encoder stopped", channel.Name);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }

        _sessions.Clear();
    }

    private sealed class ChannelSession : IDisposable
    {
        private readonly Broadcaster _broadcaster = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly ILogger _logger;
        private readonly ChannelConfig _channel;
        private int _viewers;
        private bool _disposed;

        public ChannelSession(ChannelConfig channel, ChannelStreamer streamer, ILogger logger)
        {
            _channel = channel;
            _logger = logger;

            // The encoder runs detached from any single request, so one client
            // disconnecting cannot kill the broadcast for everyone else.
            _ = Task.Run(async () =>
            {
                var sink = new BroadcastStream(_broadcaster);
                try
                {
                    await streamer.StreamAsync(channel, sink, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Session shutting down.
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "Encoder for {Channel} stopped", channel.Name);
                }
                finally
                {
                    await sink.DisposeAsync().ConfigureAwait(false);
                    _broadcaster.Complete();
                }
            });
        }

        public int ViewerCount => Volatile.Read(ref _viewers);

        public BroadcastSubscriber Subscribe()
        {
            Interlocked.Increment(ref _viewers);
            return _broadcaster.Subscribe();
        }

        public int Release() => Interlocked.Decrement(ref _viewers);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _logger.LogDebug("Disposing session for {Channel}", _channel.Name);

            _cts.Cancel();
            _broadcaster.Dispose();
            _cts.Dispose();
        }
    }

    /// <summary>
    /// Adapts the broadcaster to the <see cref="Stream"/> the encoder writes to.
    /// </summary>
    private sealed class BroadcastStream : Stream
    {
        private readonly Broadcaster _broadcaster;

        public BroadcastStream(Broadcaster broadcaster) => _broadcaster = broadcaster;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            if (offset == 0)
            {
                _broadcaster.Publish(buffer, count);
                return;
            }

            var slice = new byte[count];
            Array.Copy(buffer, offset, slice, 0, count);
            _broadcaster.Publish(slice, count);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var chunk = buffer.ToArray();
            _broadcaster.Publish(chunk, chunk.Length);
            return ValueTask.CompletedTask;
        }
    }
}
