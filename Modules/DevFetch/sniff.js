const http = require('http');
const { chromium } = require('playwright-core');

// Sniffer chung cho recon: mo trang web bang Chrome that, kich play,
// bat moi request/response media (.m3u8/.mp4) + playlist jwplayer.
// Tam (nam trong module DevFetch, xoa cung no truoc khi push).
const PORT_ARG = process.argv.indexOf('--port');
const PORT = PORT_ARG >= 0 ? parseInt(process.argv[PORT_ARG + 1] || '9197', 10) : 9197;
const UA = 'Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Mobile Safari/537.36';

let browser = null;
let queue = Promise.resolve();

async function getBrowser() {
  if (!browser) {
    browser = await chromium.launch({
      executablePath: '/usr/bin/google-chrome',
      headless: true,
      args: ['--no-sandbox', '--disable-dev-shm-usage', '--disable-blink-features=AutomationControlled']
    });
  }
  return browser;
}

async function sniff(pageUrl) {
  const b = await getBrowser();
  const ctx = await b.newContext({
    userAgent: UA,
    viewport: { width: 390, height: 844 },
    isMobile: true, hasTouch: true
  });
  const page = await ctx.newPage();
  const out = { url: pageUrl, req: [], resp: [], jw: null, video: [], frames: [], api: [], xhr: [] };
  const seen = new Set();
  const push = (arr, v) => {
    if (!v || seen.has(v)) return;
    seen.add(v);
    arr.push(v.length > 300 ? v.slice(0, 300) : v);
  };
  try {
    const noisy = /\.(js|css|png|jpg|jpeg|gif|webp|svg|woff2?|ico)(\?|#|$)/i;
    let xhrCount = 0;
    page.on('request', (r) => {
      const u = r.url();
      if (/\.(m3u8|mp4)(\?|#|$)/i.test(u)) push(out.req, r.method() + ' ' + u);
      const rt = r.resourceType();
      if ((rt === 'xhr' || rt === 'fetch') && !noisy.test(u.split('?')[0]) && xhrCount < 40) {
        xhrCount++;
        push(out.xhr, r.method() + ' ' + u);
      }
    });
    page.on('response', async (r) => {
      try {
        const ct = (r.headers()['content-type'] || '').toLowerCase();
        if (ct.includes('mpegurl') || ct.includes('mp2t') || (ct.includes('mp4') && !ct.includes('image'))) {
          push(out.resp, r.status() + ' ' + ct + ' ' + r.url());
        } else if (ct.includes('json')) {
          try {
            const t = await r.text().catch(() => '');
            if (t && /(mp4|m3u8|file|source|stream|embed)/i.test(t)) push(out.api, r.status() + ' ' + r.url() + ' :: ' + t.slice(0, 300));
          } catch (e) {}
        }
      } catch (e) {}
    });
    await page.goto(pageUrl, { waitUntil: 'domcontentloaded', timeout: 25000 }).catch(() => {});
    await page.waitForTimeout(5000);
    // thu iframe player (nhieu site nhung player trong iframe rieng)
    try {
      const frames = await page.$$('iframe');
      for (const f of frames) {
        try {
          const src = await f.getAttribute('src');
          if (src) push(out.frames, src);
        } catch (e) {}
      }
    } catch (e) {}
    // click chuot that giua player (trusted gesture mo popup/player)
    try {
      const wrap = await page.$('#player-wrapper, #video, .video-player, .player');
      if (wrap) {
        const box = await wrap.boundingBox().catch(() => null);
        if (box) {
          await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2);
          await page.waitForTimeout(4000);
        }
      }
    } catch (e) {}
    try {
      const btn = await page.$('#player-wrapper, .jw-display-icon-display, .play-button, button[class*=play]');
      if (btn) { await btn.click({ timeout: 5000 }).catch(() => {}); await page.waitForTimeout(4000); }
    } catch (e) {}
    try {
      await page.evaluate(() => {
        document.querySelectorAll('video').forEach((v) => {
          try { v.muted = true; const p = v.play(); if (p && p.catch) p.catch(() => {}); } catch (e) {}
        });
      });
      await page.waitForTimeout(5000);
    } catch (e) {}
    // quet lai iframe sau khi kich (player co the chen iframe moi)
    try {
      const frames = await page.$$('iframe');
      for (const f of frames) {
        try {
          const src = await f.getAttribute('src');
          if (src) push(out.frames, src);
        } catch (e) {}
      }
    } catch (e) {}
    // liet ke frames cua page (bat blob: va nested)
    try {
      for (const fr of page.frames()) {
        try {
          const fu = fr.url();
          if (fu && fu !== 'about:blank') push(out.frames, 'FRAME:' + fu);
        } catch (e) {}
      }
    } catch (e) {}
    try {
      const info = await page.evaluate(() => {
        const r = { jw: null, videos: [] };
        try {
          if (typeof jwplayer !== 'undefined') {
            const p = jwplayer('player');
            const pl = p && p.getPlaylist ? p.getPlaylist() : null;
            if (pl) r.jw = JSON.stringify(pl).slice(0, 2000);
          }
        } catch (e) {}
        try {
          document.querySelectorAll('video').forEach((v) => {
            r.videos.push({ src: (v.currentSrc || v.src || '').slice(0, 300), ready: v.readyState });
          });
        } catch (e) {}
        return r;
      });
      out.jw = info.jw;
      out.video = info.videos;
    } catch (e) {}
    return out;
  } finally {
    await ctx.close().catch(() => {});
  }
}

const server = http.createServer((req, res) => {
  let u;
  try { u = new URL(req.url, 'http://x'); } catch (e) { res.writeHead(400); res.end(); return; }
  if (u.pathname === '/health') { res.writeHead(200, { 'Content-Type': 'text/plain' }); res.end('ok'); return; }
  if (u.pathname !== '/sniff') { res.writeHead(404); res.end(); return; }
  const pageUrl = u.searchParams.get('url');
  if (!pageUrl || !/^https?:\/\//i.test(pageUrl)) {
    res.writeHead(400, { 'Content-Type': 'application/json' });
    res.end('{"error":"bad params"}');
    return;
  }
  const job = queue.then(() => sniff(pageUrl));
  queue = job.catch(() => null).then(() => {});
  const killer = setTimeout(() => {
    try { res.writeHead(503, { 'Content-Type': 'application/json' }); res.end('{"error":"timeout"}'); } catch (e) {}
  }, 55000);
  job.then((out) => {
    clearTimeout(killer);
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify(out || { error: 'sniff failed' }));
  }).catch(() => {
    clearTimeout(killer);
    try { res.writeHead(503, { 'Content-Type': 'application/json' }); res.end('{"error":"sniff failed"}'); } catch (e) {}
  });
});

server.listen(PORT, '127.0.0.1', () => console.log('devfetch-sniff on 127.0.0.1:' + PORT));
