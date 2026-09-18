# JellyVision — design notes (draft)

Goal: turn opted-in Jellyfin library items (series, movies) into linear
"channels" that play one episode/movie at a time, like old cable TV.

## Prior art (checked 2026-09)

| Project | What it is | Relevance |
|---|---|---|
| ErsatzTV | Standalone C# server, ~155k LoC, M3U+XMLTV+ffmpeg. **Archived Feb 2026** | The reference implementation. Read algorithms, don't fork. |
| Tunarr | TypeScript sidecar, multi-server (Plex/Emby/Jellyfin), always-transcode | Alive, but sidecar + not Jellyfin-focused |
| FinTV (binarygeek119) | **Native Jellyfin plugin**, virtual channels, 48-slot lineups, M3U+XMLTV endpoints, commercials | Closest thing to what we want. Proves the plugin approach works. |
| jellyfin-virtual-tv | Express+React sidecar, 28 channels in prod | Good writeup of the ffmpeg pitfalls |
| dizqueTV | Plex-only, predecessor to Tunarr | historical |

Conclusion: nothing dominant exists as a *native Jellyfin plugin* except
FinTV (young, kitchen-sink). There is room for a focused one.

## Architecture options

### A. Live TV tuner (M3U + XMLTV + ffmpeg) — "the ErsatzTV way"
Plugin exposes `/JellyVision/iptv/channels.m3u`, `/epg.xml`,
`/stream/{id}`; user adds it as an M3U tuner + XMLTV guide in Jellyfin.

- ✅ Works on **every** Jellyfin client (TV, phone, web) with zero client code
- ✅ Real EPG, channel surfing, tune in mid-programme
- ❌ We own an ffmpeg process per stream; concat/PTS/codec-switch bugs are
  the hard part (documented: needs full re-encode + `-output_ts_offset`)
- ❌ Double transcode (our ffmpeg → Jellyfin's transcoder)
- ❌ No per-user watch state, no resume, no subtitle picking

### B. Virtual playqueue ("director" mode)
Plugin computes, per channel, *what should be playing now and at which
offset* as a pure function of (channel, wall-clock). A small injected JS
client in jellyfin-web starts normal Jellyfin playback of that item,
seeked to the offset, and chains to the next item on end.

- ✅ No ffmpeg, no second transcode — normal Jellyfin direct play
- ✅ Keeps subtitles, audio track selection, HW accel, watch state
- ✅ Tiny codebase
- ❌ Needs JS injection into jellyfin-web (File Transformation / JS Injector
  plugin, or index.html patching) — web/desktop only, not Android TV app
- ❌ Not a "real" channel: no EPG grid unless we build our own UI

### C. Hybrid (recommended long-term)
Core = deterministic schedule engine (option B's brain) exposed over REST.
Two front-ends on top of it: the JS client (A-quality UX on web) and the
M3U/XMLTV/ffmpeg tuner (for dumb clients). Build the engine first; the
engine is the interesting part and is independent of the delivery.

## Core concepts (draft data model)

- **Channel** — id, number, name, logo, list of *sources*, schedule mode,
  optional daypart rules.
- **Source** — an opt-in selector: a Series, a Movie, a Collection, or a
  filter (genre/tag/library). Opt-in is what the user asked for.
- **Schedule mode** — `sequential` (S01E01…), `shuffle`, `block shuffle`
  (whole seasons), `marathon`, `random movie`.
- **Playout** — the materialised timeline: ordered (itemId, startUtc,
  durationTicks). Must be **deterministic from an anchor time** so server
  restarts never desync the stream from the guide (hard-won lesson from
  jellyfin-virtual-tv).
- **Filler** (later) — bumpers, idents, fake commercials between items.

## Constraints already known

- Target Jellyfin 10.11 ABI (`10.11.0.0`), net9.0, `Jellyfin.Controller`
  10.11.5 package. (dotnet 10 SDK installed locally can build net9.0.)
- Plugin GUID must be stable and unique — generate once, never change.
- Stylecop + `TreatWarningsAsErrors` are on in the template; keep them.
- Persist state in `PluginConfiguration` XML or our own JSON under
  `IApplicationPaths.PluginConfigurationsPath`. Config XML gets ugly fast
  for many channels — prefer our own JSON store.

## Current decisions and next direction

1. Wall-clock live is configurable per channel; the default joins the item
   already in progress, matching cable behaviour.
2. The Live TV tuner is the current delivery surface. The deterministic engine
   remains exposed through REST so a future web client can use the same timeline.
3. Channels support hand-picked series, movies, collections and playlists, plus
   genre/tag filters. Filler has its own source pool and does not appear in the
   guide.
4. The next scheduling feature is dayparting: selecting different source pools
   by time of day without breaking the deterministic anchor-based timeline.
