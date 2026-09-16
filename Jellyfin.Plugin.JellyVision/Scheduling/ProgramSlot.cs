using System;

namespace Jellyfin.Plugin.JellyVision.Scheduling;

/// <summary>
/// A programme occupying a slot on the channel timeline.
/// </summary>
/// <param name="Item">The scheduled item.</param>
/// <param name="StartUtc">When the programme starts.</param>
/// <param name="EndUtc">When the programme ends.</param>
public record struct ProgramSlot(ScheduleItem Item, DateTime StartUtc, DateTime EndUtc);
