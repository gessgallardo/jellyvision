using System;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyVision.Configuration;

namespace Jellyfin.Plugin.JellyVision.Scheduling;

/// <summary>
/// Builds a channel's playable item list, filler included.
/// </summary>
/// <remarks>
/// Every caller - the stream, the guide, the API, the preview - must derive
/// the schedule from the identical list, or the picture and the guide drift
/// apart. This is the single place that assembles it.
/// </remarks>
public class ChannelTimeline
{
    private readonly ChannelResolver _resolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelTimeline"/> class.
    /// </summary>
    /// <param name="resolver">The channel resolver.</param>
    public ChannelTimeline(ChannelResolver resolver)
    {
        _resolver = resolver;
    }

    /// <summary>
    /// Builds the channel's full item list, with filler interleaved when configured.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <returns>The items the channel actually plays.</returns>
    public IReadOnlyList<ScheduleItem> Build(ChannelConfig channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        var programmes = _resolver.Resolve(channel);
        if (channel.Filler.Mode != FillerMode.BetweenProgrammes)
        {
            return programmes;
        }

        var filler = _resolver.ResolveFiller(channel);
        return ScheduleEngine.Interleave(
            programmes, filler, channel.Filler.CountBetweenProgrammes);
    }

    /// <summary>
    /// Counts the real programmes on a channel, ignoring filler.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <returns>The programme count.</returns>
    public int CountProgrammes(ChannelConfig channel)
        => _resolver.Resolve(channel).Count;

    /// <summary>
    /// Builds the guide, collapsing filler into the programme that follows it.
    /// </summary>
    /// <remarks>
    /// A real EPG does not list the ad break: the programme's slot simply
    /// covers it. Extending each programme's start back over any preceding
    /// filler keeps the guide gapless, which is what clients expect, and means
    /// tuning in during a bumper still shows the upcoming programme.
    /// </remarks>
    /// <param name="channel">The channel.</param>
    /// <param name="fromUtc">Window start.</param>
    /// <param name="window">Window length.</param>
    /// <returns>The guide entries.</returns>
    public IReadOnlyList<ProgramSlot> BuildGuide(
        ChannelConfig channel, DateTime fromUtc, TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(channel);

        var raw = ScheduleEngine.GetGuide(
            Build(channel),
            channel.Mode,
            channel.AnchorUtc,
            fromUtc,
            window,
            ScheduleEngine.SeedFor(channel.Id));

        return MergeFiller(raw);
    }

    /// <summary>
    /// Collapses filler slots into the programme that follows them.
    /// </summary>
    /// <param name="slots">The raw slots.</param>
    /// <returns>Slots containing only real programmes.</returns>
    public static IReadOnlyList<ProgramSlot> MergeFiller(IReadOnlyList<ProgramSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);

        var merged = new List<ProgramSlot>(slots.Count);
        DateTime? pendingStart = null;

        foreach (var slot in slots)
        {
            if (slot.Item.IsFiller)
            {
                // Remember where the break began so the next programme can
                // absorb it and the guide stays contiguous.
                pendingStart ??= slot.StartUtc;
                continue;
            }

            var start = pendingStart ?? slot.StartUtc;
            pendingStart = null;
            merged.Add(new ProgramSlot(slot.Item, start, slot.EndUtc));
        }

        // Trailing filler with no following programme extends the last entry.
        if (pendingStart.HasValue && merged.Count > 0)
        {
            var last = merged[^1];
            var trailingEnd = slots[^1].EndUtc;
            merged[^1] = new ProgramSlot(last.Item, last.StartUtc, trailingEnd);
        }

        return merged;
    }
}
