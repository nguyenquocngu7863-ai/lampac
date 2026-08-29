# Maintained hls.js runtime

- Upstream: https://github.com/video-dev/hls.js
- Version: 1.7.1
- Distribution file: `dist/hls.min.js` from the published `hls.js@1.7.1` npm package.
- License: Apache-2.0; see `LICENSE`.

Lampac copies `hls.js` to Lampa's historical `wwwroot/lampa-main/vender/hls/hls.js` path. `LampaCron` reapplies it after frontend updates.

Local guard: the minified `BasePlaylistController.switchParams` check is changed from `this.hls.config.lowLatencyMode` to `this.hls && this.hls.config.lowLatencyMode`. This prevents a delayed LL-HLS playlist tick from dereferencing the Hls instance after the player has destroyed it following a fatal reload.
