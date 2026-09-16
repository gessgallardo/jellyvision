# JellyVision

A Jellyfin plugin that turns opted-in series and movies into always-on,
cable-style channels — one episode at a time, on a schedule, like TV used
to work.

Status: **working engine, no client delivery yet.** Installed and verified
running on a live Jellyfin 10.11.9 server; the schedule engine and REST API
are covered by 22 tests. See Roadmap for what's missing.

## Install

Dashboard -> Plugins -> Repositories -> Add:

```
https://raw.githubusercontent.com/gessgallardo/jellyvision/main/manifest.json
```

Then Catalogue -> JellyVision -> Install, and restart the server.
(`raw.githubusercontent.com` caches for ~3 minutes, so a freshly published
version takes a moment to appear.)

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

## Releasing

`scripts/package.py` builds Release, zips the dll, computes the MD5 the
manifest requires, and updates `manifest.json`:

```bash
python3 scripts/package.py --version 0.1.3.0 \
  --base-url https://github.com/gessgallardo/jellyvision/releases/download/v0.1.3.0
git commit -am "..." && git push
gh release create v0.1.3.0 dist/jellyvision_0.1.3.0.zip
```

## Hard-won constraints

Two runtime traps that unit tests alone will not catch - both are now
pinned by tests:

- **Jellyfin persists plugin config with `XmlSerializer`**, which throws on
  interface-typed (`IList<T>`) and read-only collections. Configuration
  collections must be concrete, settable `List<T>`, or every endpoint that
  reads configuration returns 500. `PluginConfigurationSerializationTests`
  round-trips the real config to catch this at build time.
- **The guide must be derived from the cycle boundary**, not from the
  current programme's start time, or its first entry reports the wrong item
  for the right time slot - exactly the guide/stream drift the design
  exists to prevent.

## Roadmap

1. ~~Deterministic schedule engine + tests~~
2. ~~REST API~~
3. ~~Plugin repository packaging + install on a live server~~
4. Channel editor UI in the plugin config page (pick series/movies/collections)
5. Delivery — pick one or both:
   - **Web client**: injected JS that starts normal Jellyfin playback at the
     computed offset and chains to the next item. No ffmpeg, keeps direct
     play, subtitles and watch state. Web/desktop only.
   - **Live TV tuner**: `channels.m3u` + `epg.xml` + an MPEG-TS stream, added
     as an M3U tuner. Works on every client including TV apps, but needs an
     ffmpeg process per stream (concat + full re-encode + `-output_ts_offset`).
6. Filler: bumpers, idents, fake commercials between programmes.
7. Dayparting: different sources by time of day.

See `docs/DESIGN.md` for the full trade-off notes.
