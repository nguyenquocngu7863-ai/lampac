'use strict';

// lampac-stremio-sub — minimal Stremio subtitle addon that queries SubDL and
// SubSource using API keys loaded from the Lampac init.conf on the same host.
// Intended to be run inside the Ubuntu proot alongside Lampac (port 7000),
// so Lampa/StremioSub points at 127.0.0.1 instead of the public subdl.strem.top
// or subsource.strem.top hosts that share one API key with everyone (rarelimit).
//
// Usage:
//   PORT=7000 LAMPAC_INIT_CONF=/root/lampac/init.conf node server.js
//
// Endpoints:
//   GET /manifest.json       -> addon manifest (anonymous/no-key mode uses keys
//                               loaded from init.conf)
//   GET /:resource/:type/:id/:extra?.json   -> stremio addon SDK routes

const fs = require('fs');
const path = require('path');
const http = require('http');
const https = require('https');
const { addonBuilder, serveHTTP } = require('stremio-addon-sdk');

const PORT = parseInt(process.env.PORT || '7000', 10);
const INIT_CONF = process.env.LAMPAC_INIT_CONF || '/root/lampac/init.conf';
const USER_AGENT = 'lampac-stremio-sub/1.0 (+https://github.com/nguyenquocngu7863-ai/lampac)';

// ---- Logging ---------------------------------------------------------------
function log(...args) {
  const ts = new Date().toISOString().slice(11, 19);
  console.log(`[${ts}]`, ...args);
}

// ---- HTTP helper -----------------------------------------------------------
function request(url, { headers = {}, method = 'GET', body = null } = {}) {
  return new Promise((resolve, reject) => {
    const lib = url.startsWith('https:') ? https : http;
    const req = lib.request(url, {
      method,
      headers: {
        'User-Agent': USER_AGENT,
        'Accept': 'application/json, text/plain, */*',
        ...headers,
      },
      timeout: 15000,
    }, (res) => {
      const chunks = [];
      res.on('data', (c) => chunks.push(c));
      res.on('end', () => {
        const text = Buffer.concat(chunks).toString('utf8');
        resolve({ status: res.statusCode, headers: res.headers, body: text });
      });
    });
    req.on('timeout', () => { req.destroy(new Error('timeout')); });
    req.on('error', reject);
    if (body) req.write(body);
    req.end();
  });
}

function requestFollow(url, opts = {}, depth = 0) {
  if (depth > 3) return request(url, opts);
  return request(url, opts).then((r) => {
    if ([301, 302, 303, 307, 308].includes(r.status) && r.headers.location) {
      return requestFollow(new URL(r.headers.location, url).toString(), opts, depth + 1);
    }
    return r;
  });
}

// ---- Load API keys from Lampac init.conf -----------------------------------
// Parses the "SubFinder": { "subdl_api_key": "...", "subsource_api_key": "..." }
// block without pulling in a JSON parser dependency (init.conf is JSONC with
// //-style comments; strip them with a small regex).
function loadKeys() {
  // Prefer env (populated by subaddonctl.sh from init.conf), fall back to parsing
  // Lampac init.conf ourselves so a manual `node server.js` also works.
  const envSub = process.env.SUBDL_API_KEY || '';
  const envSubSource = process.env.SUBSOURCE_API_KEY || '';
  if (envSub || envSubSource) return { subdl: envSub, subsource: envSubSource };
  try {
    let txt = fs.readFileSync(INIT_CONF, 'utf8');
    txt = txt.replace(/^\s*\/\/.*$/gm, '');
    const sfMatch = txt.match(/"SubFinder"\s*:\s*\{([^}]*)\}/s);
    const out = { subdl: '', subsource: '' };
    if (!sfMatch) return out;
    const block = sfMatch[1];
    const sd = block.match(/"subdl_api_key"\s*:\s*"([^"]*)"/);
    const ss = block.match(/"subsource_api_key"\s*:\s*"([^"]*)"/);
    if (sd) out.subdl = sd[1];
    if (ss) out.subsource = ss[1];
    return out;
  } catch (err) {
    log('loadKeys failed:', err.message);
    return { subdl: '', subsource: '' };
  }
}

// ---- Imdb / meta parsing ---------------------------------------------------
function parseMeta(id) {
  // stremio sends ids like "tt1234567" for movies, "tt1234567:3:15" for series ep.
  const parts = id.split(':');
  const imdb = parts[0];
  const season = parts.length >= 2 ? parseInt(parts[1], 10) : null;
  const episode = parts.length >= 3 ? parseInt(parts[2], 10) : null;
  const type = (parts.length >= 2) ? 'series' : 'movie';
  return { imdb, type, season, episode };
}

// ---- SubDL -----------------------------------------------------------------
async function searchSubdl({ imdb, type, season, episode }, key) {
  if (!key) return [];
  try {
    const base = 'https://api.subdl.com/api/v2';
    const auth = { Authorization: `Bearer ${key}`, Accept: 'application/json' };

    // Step 1: resolve sd_id from IMDb id via the search endpoint (subdl v2 uses sd_id)
    const sq = new URLSearchParams({
      api_key: key,
      q: imdb, // searches by title/imdb string; returns best match
      type: type === 'series' ? 'tv' : 'movie',
      limit: '1',
    });
    const searchRes = await requestFollow(`${base}/movies/search?${sq.toString()}`, { headers: auth });
    if (searchRes.status !== 200) { log('subdl search status', searchRes.status); return []; }
    const sJson = JSON.parse(searchRes.body);
    const results = sJson.results || [];
    if (!results.length) return [];
    const sdId = results[0].sd_id;
    if (!sdId) return [];

    // Step 2: subtitles search with sd_id
    const p = new URLSearchParams({ api_key: key, sd_id: String(sdId), unpack: '1', langs: 'vi,en' });
    if (season) p.set('season', String(season));
    if (episode) p.set('episode', String(episode));
    const r = await requestFollow(`${base}/subtitles/search?${p.toString()}`, { headers: auth });
    if (r.status !== 200) { log('subdl subs status', r.status, r.body.slice(0, 200)); return []; }
    const json = JSON.parse(r.body);
    const out = [];
    const push = (sub, uf) => {
      let subUrl = uf?.url || sub?.url || sub?.download_link;
      if (!subUrl) return;
      if (subUrl.startsWith('/')) subUrl = 'https://dl.subdl.com' + subUrl;
      const releaseName = uf?.release_name || sub?.release_name || sub?.filename || 'sub';
      const lang = String(uf?.lang || sub?.lang || sub?.language || '').toLowerCase();
      const label = `SubDL · ${lang} · ${releaseName}`.slice(0, 160);
      out.push({ id: `subdl-${Buffer.from(subUrl).toString('base64url')}`, url: subUrl, lang, label });
    };
    for (const s of (json.subtitles || [])) {
      const ufs = s.unpack_files;
      if (Array.isArray(ufs) && ufs.length) ufs.forEach((uf) => push(s, uf));
      else push(s, null);
    }
    return out;
  } catch (err) {
    log('subdl error:', err.message);
    return [];
  }
}

// ---- SubSource -------------------------------------------------------------
async function searchSubsource({ imdb, type, season, episode }, key) {
  if (!key) return [];
  try {
    const url = new URL('https://api.subsource.net/api/getMovie');
    url.searchParams.set('imdb', imdb);
    url.searchParams.set('type', type === 'series' ? 'tv' : 'movie');
    if (season) url.searchParams.set('season', String(season));
    if (episode) url.searchParams.set('episode', String(episode));
    const r = await requestFollow(url.toString(), {
      headers: { 'X-API-Key': key },
    });
    if (r.status !== 200) {
      log('subsource status', r.status, r.body.slice(0, 200));
      return [];
    }
    const json = JSON.parse(r.body);
    const out = [];
    for (const s of (json.subs || [])) {
      const subUrl = s.url;
      if (!subUrl) continue;
      const lang = (s.lang || '').toLowerCase();
      const release = s.release || s.releaseName || 'sub';
      const label = `SubSource · ${lang} · ${release}`.slice(0, 160);
      out.push({
        id: `subsource-${Buffer.from(subUrl).toString('base64url')}`,
        url: subUrl,
        lang,
        label,
      });
    }
    return out;
  } catch (err) {
    log('subsource error:', err.message);
    return [];
  }
}

// ---- Download/subtitle proxy -----------------------------------------------
// Subtitle files are served through this addon so players on the LAN don't
// hit CORS/referer restrictions and so we can normalize ZIP archives into SRT
// entries in a future iteration. For now, pass through with a Referer header.
async function fetchSubtitle(url) {
  const r = await requestFollow(url, {
    headers: { Referer: 'https://subdl.com/', Accept: '*/*' },
  });
  return { status: r.status, body: r.body, headers: r.headers };
}

// ---- Stremio addon ---------------------------------------------------------
const builder = new addonBuilder({
  id: 'ai.lampac.sub',
  version: '1.0.0',
  name: 'Lampac Subs (SubDL + SubSource)',
  description:
    'Phụ đề tiếng Việt / Anh từ SubDL và SubSource, dùng API key của bạn đọc từ init.conf. Chạy tại chỗ trên Termux, không bị rare limit.',
  logo: 'https://subdl.com/favicon.ico',
  resources: ['subtitles'],
  types: ['movie', 'series'],
  idPropertys: ['imdb_id'],
  catalogs: [],
});

builder.defineSubtitlesHandler(async ({ type, id, extra }) => {
  const meta = parseMeta(id);
  const keys = loadKeys();
  log('search', meta, 'keys(subdl/ss):', keys.subdl ? 'set' : 'EMPTY', '/', keys.subsource ? 'set' : 'EMPTY');
  const [subdlSubs, subsourceSubs] = await Promise.all([
    searchSubdl(meta, keys.subdl),
    searchSubsource(meta, keys.subsource),
  ]);
  const all = [...subdlSubs, ...subsourceSubs];
  // Convert to stremio subtitle resource format.
  const subtitles = all.map((s) => ({
    id: s.id,
    lang: s.lang.startsWith('vi') ? 'vie' : s.lang.startsWith('en') ? 'eng' : s.lang,
    url: `http://127.0.0.1:${PORT}/subtitle/${encodeURIComponent(s.id)}?src=${encodeURIComponent(s.url)}`,
    name: s.label,
  }));
  return { subtitles };
});

const addonInterface = builder.getInterface();

// Add a /subtitle/:id route that proxies the actual SRT/ZIP file.
const app = serveHTTP(addonInterface, { port: PORT });
app.get('/subtitle/:id', async (req, res) => {
  try {
    const src = req.query.src;
    if (!src) return res.status(400).send('missing src');
    const r = await fetchSubtitle(src);
    if (r.status !== 200) return res.status(r.status).send('upstream error');
    const contentType = r.headers['content-type'] || 'application/octet-stream';
    res.set('Content-Type', contentType);
    res.set('Access-Control-Allow-Origin', '*');
    res.send(r.body);
  } catch (err) {
    log('subtitle proxy error:', err.message);
    res.status(502).send(err.message);
  }
});
app.get('/health', (req, res) => res.json({ status: 'ok' }));

log(`Lampac Subs addon listening on http://0.0.0.0:${PORT}/manifest.json`);
log(`Config path: ${INIT_CONF}`);
const keys = loadKeys();
if (!keys.subdl) log('⚠️  SubDL API key chưa được cấu hình trong SubFinder.subdl_api_key');
if (!keys.subsource) log('⚠️  SubSource API key chưa được cấu hình trong SubFinder.subsource_api_key');
