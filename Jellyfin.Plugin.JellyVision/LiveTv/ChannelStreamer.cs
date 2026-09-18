using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyVision.Configuration;
using Jellyfin.Plugin.JellyVision.Scheduling;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyVision.LiveTv;

/// <summary>
/// Produces a continuous MPEG-TS stream for a channel by running ffmpeg over
/// batches of scheduled items.
/// </summary>
/// <remarks>
/// Design notes, learned from the prior art (ErsatzTV, jellyfin-virtual-tv):
/// <list type="bullet">
/// <item>The concat demuxer is used with a full re-encode, not <c>-c copy</c>.
/// Copying gapless-looking streams breaks when SPS/PPS or audio codec_priv
/// changes between episodes: video stalls and some clients silently drop
/// audio.</item>
/// <item><c>-t</c> caps each batch at its wall-clock end, so a fast encoder
/// cannot run ahead of the guide.</item>
/// <item><c>-output_ts_offset</c> keeps PTS climbing across batch restarts;
/// without it every reconnect rewinds the player to zero.</item>
/// <item>The first item of the first batch uses <c>inpoint</c> so a client
/// tuning in mid-programme joins where the channel actually is.</item>
/// <item><c>-re</c> paces output at native frame rate. Without it ffmpeg
/// encodes as fast as the CPU allows - measured at 5.5x realtime on a live
/// server - so the client races ahead of the guide and the channel is no
/// longer live.</item>
/// </list>
/// </remarks>
public class ChannelStreamer
{
    private const int BatchSize = 3;

    private readonly ILibraryManager _libraryManager;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly ChannelTimeline _timeline;
    private readonly ILogger<ChannelStreamer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelStreamer"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="mediaEncoder">The media encoder, used for the ffmpeg path.</param>
    /// <param name="timeline">Builds the channel's item list, filler included.</param>
    /// <param name="logger">The logger.</param>
    public ChannelStreamer(
        ILibraryManager libraryManager,
        IMediaEncoder mediaEncoder,
        ChannelTimeline timeline,
        ILogger<ChannelStreamer> logger)
    {
        _libraryManager = libraryManager;
        _mediaEncoder = mediaEncoder;
        _timeline = timeline;
        _logger = logger;
    }

    /// <summary>
    /// Streams the channel to <paramref name="output"/> until the client disconnects.
    /// </summary>
    /// <param name="channel">The channel to stream.</param>
    /// <param name="output">The response body.</param>
    /// <param name="cancellationToken">Cancelled when the client goes away.</param>
    /// <returns>A task that completes when streaming stops.</returns>
    public async Task StreamAsync(
        ChannelConfig channel,
        Stream output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(output);

        var items = _timeline.Build(channel);
        if (items.Count == 0)
        {
            _logger.LogWarning("Channel {Channel} has no playable items", channel.Name);
            return;
        }

        var seed = ScheduleEngine.SeedFor(channel.Id);
        var ptsOffset = TimeSpan.Zero;
        var first = true;

        while (!cancellationToken.IsCancellationRequested)
        {
            var nowUtc = DateTime.UtcNow;
            var current = ScheduleEngine.GetCurrent(
                items, channel.Mode, channel.AnchorUtc, nowUtc, seed, out var offset);

            if (current is null)
            {
                return;
            }

            // Only the very first batch joins mid-programme; later batches start
            // on a boundary because we have been streaming continuously.
            var joinOffset = first && channel.LiveWallClock ? offset : TimeSpan.Zero;
            first = false;

            var batch = ScheduleEngine.GetGuide(
                items,
                channel.Mode,
                channel.AnchorUtc,
                nowUtc,
                TimeSpan.FromTicks(Math.Max(1, SumOfNext(items, current.Value, BatchSize))),
                seed);

            if (batch.Count == 0)
            {
                return;
            }

            var take = Math.Min(BatchSize, batch.Count);
            var duration = TimeSpan.Zero;
            var listPath = Path.Combine(Path.GetTempPath(), $"jellyvision-{Guid.NewGuid():N}.txt");

            try
            {
                var wrote = WriteConcatList(listPath, batch, take, joinOffset, ref duration);
                if (!wrote)
                {
                    _logger.LogWarning("No resolvable files in batch for {Channel}", channel.Name);
                    return;
                }

                await RunFfmpegAsync(listPath, duration, ptsOffset, output, cancellationToken)
                    .ConfigureAwait(false);

                ptsOffset += duration;
            }
            finally
            {
                TryDelete(listPath);
            }
        }
    }

    private static long SumOfNext(
        IReadOnlyList<ScheduleItem> items, ProgramSlot current, int count)
    {
        // A window long enough to contain `count` programmes from the current one.
        var longest = 0L;
        for (var i = 0; i < items.Count; i++)
        {
            longest = Math.Max(longest, items[i].Duration.Ticks);
        }

        return (current.EndUtc - DateTime.UtcNow).Ticks + (longest * count);
    }

    private bool WriteConcatList(
        string listPath,
        IReadOnlyList<ProgramSlot> batch,
        int take,
        TimeSpan joinOffset,
        ref TimeSpan duration)
    {
        var entries = new List<(string Path, TimeSpan Duration, TimeSpan Inpoint)>(take);

        for (var i = 0; i < take; i++)
        {
            var slot = batch[i];
            var path = ResolvePath(slot.Item.ItemId);
            if (path is null)
            {
                continue;
            }

            entries.Add((path, slot.Item.Duration, i == 0 ? joinOffset : TimeSpan.Zero));
        }

        var result = ConcatListBuilder.Build(entries);
        if (result.Duration > TimeSpan.Zero)
        {
            File.WriteAllText(listPath, result.Text);
            duration = result.Duration;
            return true;
        }

        return false;
    }

    private string? ResolvePath(string itemId)
    {
        if (!Guid.TryParse(itemId, out var guid))
        {
            return null;
        }

        var item = _libraryManager.GetItemById(guid);
        var path = item?.Path;
        return string.IsNullOrEmpty(path) || !File.Exists(path) ? null : path;
    }

    private async Task RunFfmpegAsync(
        string listPath,
        TimeSpan duration,
        TimeSpan ptsOffset,
        Stream output,
        CancellationToken cancellationToken)
    {
        var args = string.Create(
            CultureInfo.InvariantCulture,
            $"-hide_banner -loglevel error -nostdin " +
            $"-fflags +genpts+discardcorrupt " +
            $"-re " +
            $"-f concat -safe 0 -i \"{listPath}\" " +
            $"-map 0:v:0 -map 0:a:0? " +
            $"-c:v libx264 -preset veryfast -tune zerolatency " +
            // Repeat SPS/PPS with every keyframe. A live viewer joins
            // mid-stream and never sees the initial headers, so without this
            // the decoder reports "non-existing PPS" and shows nothing.
            $"-x264-params repeat-headers=1 -bsf:v dump_extra " +
            $"-profile:v high -level 4.1 -pix_fmt yuv420p " +
            $"-g 60 -keyint_min 60 -sc_threshold 0 " +
            $"-b:v 4000k -maxrate 6000k -bufsize 8000k " +
            $"-c:a aac -b:a 192k -ar 48000 -ac 2 " +
            $"-t {duration.TotalSeconds:F3} " +
            $"-output_ts_offset {ptsOffset.TotalSeconds:F3} " +
            $"-f mpegts -mpegts_flags resend_headers -flush_packets 1 pipe:1");

        var psi = new ProcessStartInfo(_mediaEncoder.EncoderPath, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        _logger.LogDebug(
            "ffmpeg batch: {Duration}s at pts offset {Offset}s",
            duration.TotalSeconds,
            ptsOffset.TotalSeconds);

        process.Start();

        var drainErrors = Task.Run(
            async () =>
            {
                var text = await process.StandardError.ReadToEndAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogWarning("ffmpeg: {Error}", text.Trim());
                }
            },
            cancellationToken);

        try
        {
            await process.StandardOutput.BaseStream
                .CopyToAsync(output, 64 * 1024, cancellationToken)
                .ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected; fall through to kill the encoder.
        }
        finally
        {
            KillQuietly(process);
            await Task.WhenAny(drainErrors, Task.Delay(1000, CancellationToken.None))
                .ConfigureAwait(false);
        }
    }

    private void KillQuietly(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
        catch (NotSupportedException ex)
        {
            _logger.LogDebug(ex, "Could not kill ffmpeg");
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "Could not delete {Path}", path);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogDebug(ex, "Could not delete {Path}", path);
        }
    }
}
