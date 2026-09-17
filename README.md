# JellyVision

A Jellyfin plugin that turns opted-in series and movies into always-on,
cable-style channels — one episode at a time, on a schedule, like TV used
to work.

Status: **working end to end.** Installed on a live Jellyfin 10.11.9 server,
added as an M3U tuner with an XMLTV guide, and verified playing back through
Jellyfin's own Live TV pipeline at realtime. 29 tests.

## Install

Dashboard -> Plugins -> Repositories -> Add:

```
https://raw.githubusercontent.com/gessgallardo/jellyvision/main/manifest.json
```

Then Catalogue -> JellyVision -> Install, and restart the server.
(`raw.githubusercontent.com` caches for ~3 minutes, so a freshly published
version takes a moment to appear.)

### Wire it into Live TV

1. Dashboard -> Live TV -> Add tuner device -> **M3U Tuner**
   `<server>/JellyVision/iptv/channels.m3u`
2. Add guide provider -> **XMLTV**
   `<server>/JellyVision/iptv/epg.xml`

Both URLs are shown on the plugin's configuration page. Behind a reverse
proxy that terminates TLS, set **Public base URL** there too, or the playlist
advertises `http://` stream URLs.

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
- `RoundRobin` — one episode from each series per round, preserving episode order.

Per channel, `LiveWallClock` decides whether tuning in joins the programme
already in progress (cable behaviour) or starts it from the beginning.

## API

All endpoints require normal Jellyfin authentication.

| Method | Route | Purpose |
|---|---|---|
| GET | `/JellyVision/Channels` | list channels + resolved item counts |
| GET | `/JellyVision/Channels/{id}/Now` | current programme + seek offset in ticks |
| GET | `/JellyVision/Channels/{id}/Guide?hours=24` | upcoming schedule |

The IPTV surface is anonymous, because Jellyfin's M3U tuner and XMLTV guide
fetcher send no credentials:

| Method | Route | Purpose |
|---|---|---|
| GET | `/JellyVision/iptv/channels.m3u` | M3U playlist |
| GET | `/JellyVision/iptv/epg.xml` | XMLTV guide |
| GET | `/JellyVision/iptv/stream/{id}` | continuous MPEG-TS |

These expose channel names, programme titles and media to anyone who can
reach the server, so do not publish it to the internet unproxied.

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
- **ffmpeg needs `-re`.** Without it the encoder runs as fast as the CPU
  allows (measured 5.5x realtime on the live server), so the stream outruns
  its own guide and the channel stops being live.
- **`XmlWriter` takes its declared encoding from the underlying writer**, and
  a plain `StringWriter` reports UTF-16. The XMLTV response is UTF-8 bytes,
  so it declared `utf-16` and strict parsers rejected it.
- **Stream URLs must honour `X-Forwarded-Proto`** (or `PublicBaseUrl`), or a
  TLS-terminating proxy makes the playlist advertise `http://`.
- **Concat with `-c copy` is not safe here**: SPS/PPS or audio codec_priv
  changes between episodes stall video and silently kill audio on some
  clients. Full re-encode, `-t` to cap each batch at its wall-clock end, and
  `-output_ts_offset` so reconnects do not rewind.

## Roadmap

1. ~~Deterministic schedule engine + tests~~
2. ~~REST API~~
3. ~~Plugin repository packaging + install on a live server~~
4. ~~Live TV tuner: M3U + XMLTV + MPEG-TS streaming~~
5. Channel editor UI in the plugin config page (pick series/movies/collections).
   Channels are currently created by POSTing the plugin configuration.
6. Stream sharing: one ffmpeg per channel rather than per client.
7. Channel logos in the playlist and guide.
8. Filler: bumpers, idents, fake commercials between programmes.
9. Dayparting: different sources by time of day.

See `docs/DESIGN.md` for the full trade-off notes.
