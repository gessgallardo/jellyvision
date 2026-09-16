# JellyVision

A Jellyfin plugin that turns opted-in series and movies into always-on,
cable-style channels — one episode at a time, on a schedule, like TV used
to work.

Status: **early scaffold.** The schedule engine and its REST API work and
are covered by tests. There is no client delivery yet (see Roadmap).

## Why

Nothing dominant exists as a *native* Jellyfin plugin for this:

- **ErsatzTV** — the reference implementation, ~155k LoC of C#, archived Feb 2026.
- **Tunarr** — alive, TypeScript, but a multi-server sidecar that always transcodes.
- **FinTV** — a native plugin, young and kitchen-sink (weather channels, AI lineups).
- **jellyfin-virtual-tv** — an Express sidecar; its writeup documents the ffmpeg traps.

JellyVision aims to be the small, focused, Jellyfin-only one.

## Architecture

The core is a **deterministic schedule engine**: what a channel is airing is
a pure function of `(item list, mode, anchor time, wall clock)`. Nothing is
persisted, so a restart, a redeploy or a second process all compute the
exact same timeline — the guide can never drift away from the stream. This
is the single most important lesson from the prior art.

```
ChannelConfig (opt-in sources)
      |
      v
ChannelResolver ---> IReadOnlyList<ScheduleItem>   (Jellyfin library)
      |
      v
ScheduleEngine  ---> ProgramSlot (item + start + end + seek offset)
      |
      +--> REST API            (done)
      +--> Web client / JS     (planned)
      +--> M3U + XMLTV + ffmpeg tuner (planned)
```

### Scheduling modes
- `Sequential` — natural order (series, season, episode).
- `Shuffle` — seeded Fisher-Yates, reshuffled deterministically each cycle.
- `BlockShuffle` — shuffle whole seasons/series, keep each block in order.

Per channel, `LiveWallClock` decides whether tuning in joins the programme
already in progress (cable behaviour) or starts it from the beginning.

## API

All endpoints require normal Jellyfin authentication.

| Method | Route | Purpose |
|---|---|---|
| GET | `/JellyVision/Channels` | list channels + resolved item counts |
| GET | `/JellyVision/Channels/{id}/Now` | current programme + seek offset in ticks |
| GET | `/JellyVision/Channels/{id}/Guide?hours=24` | upcoming schedule |

## Build

```bash
dotnet build Jellyfin.Plugin.JellyVision/Jellyfin.Plugin.JellyVision.csproj -c Release
dotnet test tests/Jellyfin.Plugin.JellyVision.Tests
```

Copy `bin/Release/net9.0/Jellyfin.Plugin.JellyVision.dll` into
`<jellyfin-config>/plugins/JellyVision/` and restart the server.

Targets Jellyfin ABI 10.11.0.0 / net9.0.

## Roadmap

1. ~~Deterministic schedule engine + tests~~
2. ~~REST API~~
3. Channel editor UI in the plugin config page (pick series/movies/collections)
4. Delivery — pick one or both:
   - **Web client**: injected JS that starts normal Jellyfin playback at the
     computed offset and chains to the next item. No ffmpeg, keeps direct
     play, subtitles and watch state. Web/desktop only.
   - **Live TV tuner**: `channels.m3u` + `epg.xml` + an MPEG-TS stream, added
     as an M3U tuner. Works on every client including TV apps, but needs an
     ffmpeg process per stream (concat + full re-encode + `-output_ts_offset`).
5. Filler: bumpers, idents, fake commercials between programmes.
6. Dayparting: different sources by time of day.

See `docs/DESIGN.md` for the full trade-off notes.
