using System;

namespace Jellyfin.Plugin.JellyVision.Api;

/// <summary>
/// A guide entry.
/// </summary>
/// <param name="ItemId">The library item id.</param>
/// <param name="Title">The programme title.</param>
/// <param name="StartUtc">Start time.</param>
/// <param name="EndUtc">End time.</param>
public record GuideEntryDto(string ItemId, string Title, DateTime StartUtc, DateTime EndUtc);
