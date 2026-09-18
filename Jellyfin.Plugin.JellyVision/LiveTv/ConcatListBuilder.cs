using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Jellyfin.Plugin.JellyVision.LiveTv;

/// <summary>
/// Builds an ffconcat list whose declared file durations match the schedule.
/// </summary>
public static class ConcatListBuilder
{
    /// <summary>
    /// Builds a concat list and returns its scheduled duration.
    /// </summary>
    /// <param name="entries">Path, scheduled duration, and optional inpoint for each file.</param>
    /// <returns>The concat text and effective duration.</returns>
    public static (string Text, TimeSpan Duration) Build(
        IReadOnlyList<(string Path, TimeSpan Duration, TimeSpan Inpoint)> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var text = new StringBuilder();
        var total = TimeSpan.Zero;
        foreach (var (path, scheduledDuration, inpoint) in entries)
        {
            if (string.IsNullOrEmpty(path) || scheduledDuration <= TimeSpan.Zero)
            {
                continue;
            }

            var effectiveDuration = scheduledDuration;
            var escaped = path.Replace("'", @"'\''", StringComparison.Ordinal);
            text.Append(CultureInfo.InvariantCulture, $"file '{escaped}'\n");
            if (inpoint > TimeSpan.Zero && inpoint < scheduledDuration)
            {
                text.Append(CultureInfo.InvariantCulture, $"inpoint {inpoint.TotalSeconds:F3}\n");
                effectiveDuration -= inpoint;
            }

            // Without this directive concat uses the container's duration. A
            // metadata/runtime mismatch can then end a chapter at the wrong
            // point and leave the live stream stuck instead of advancing.
            text.Append(CultureInfo.InvariantCulture, $"duration {effectiveDuration.TotalSeconds:F3}\n");
            total += effectiveDuration;
        }

        return (text.ToString(), total);
    }
}
