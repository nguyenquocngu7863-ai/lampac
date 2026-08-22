# WebStreamrMBG bridge

This module consumes a configured Stremio add-on through its standard
`stream/{type}/{id}.json` resource and exposes the returned **HTTP(S)** streams
as a normal Lampac online source.

The default manifest is the WebStreamrMBG public manifest configured with
`multi=on`:

```text
https://87d6a6ef6b58-webstreamrmbg.baby-beamup.club/%7B%22multi%22%3A%22on%22%7D/manifest.json
```

Replace `WebStreamr.manifest` in `init.conf` with the HTTPS manifest URL
created by the add-on configuration page if different languages, resolution
filters, or other options are selected.

## Behavior

- The source accepts IMDb ids (`tt...`) and TMDB ids (`tmdb:...`).
- Movies query `stream/movie/{id}.json`; every returned file stays visible as
  its own Lampac card. The source filter buttons above the list can narrow the
  cards to one provider without hiding same-resolution releases.
- Stremio asks for one series episode at a time. The bridge first builds the
  Lampa season/episode UI from the TMDB API (with a Cinemeta metadata fallback
  when only an IMDb id is available), then calls a dedicated episode route
  after selection. That route queries
  `stream/series/{id}:{season}:{episode}.json` and returns the playable result.
- Episode links are marked for the client helper to show a link picker before
  the player starts. This avoids an automatic playlist attempt hiding which
  individual source failed and lets the user choose a 4K/1080p/provider link.
- Only HTTP(S) `stream.url` values are accepted. `magnet:`, `externalUrl`, and
  other non-HTTP values are intentionally skipped.
- Stremio `behaviorHints.proxyHeaders.request` and `url|Header=value` headers
  are preserved for the normal Lampac stream proxy.
- HLS/MP4 results use a normal `/video` redirect and stay direct.
- MKV results use a `/file.mkv` redirect. The existing `gst.js` plug-in checks
  the live root `gst.enable` status: when it is `true`, the selected MKV goes
  through GStreamer; when it is `false`, the same URL is allowed to redirect to
  the source/proxy for VLC/direct playback. HLS and MP4 results preserve their
  own `/file.m3u8` or `/file.mp4` suffix so Lampa selects the right player.
  Opaque download URLs marked `behaviorHints.notWebReady` fall back to the MKV
  route instead of being sent to an extensionless HTML5 video element.

## Configuration

```json
"WebStreamr": {
  "enable": true,
  "manifest": "https://.../manifest.json",
  "streamproxy": true,
  "timeoutSeconds": 25,
  "maxStreams": 24
}
```

`streamproxy: true` makes the phone proxy the bytes and apply the headers
returned by the add-on; it does not transcode the video. Set it to `false` only
when the TV/VLC can reach the returned URL and does not need request headers.
Use only add-ons and streams that you are authorized to access. This bridge
does not bypass DRM, anti-bot challenges, or VIP authentication.
