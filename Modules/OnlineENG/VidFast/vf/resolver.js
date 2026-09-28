// VidFast resolver: 1 Chromium thuong truc (gia mobile, tat co
// AutomationControlled), mo trang embed, bam play, bat .m3u8.
// GET /r?movie=<tmdb|imdb>&server=<ten> -> {m3u8, headers}
const http = require('http');
const { chromium } = require('playwright-core');

const ARG = process.argv.indexOf('--port');
const PORT = ARG >= 0
  ? parseInt(process.argv[ARG + 1] || '9197', 10) : 9197;
const CHROME = process.env.CHROME_BIN
  || '/data/data/com.termux/files/home/lampac-native/chrome/chrome';
const UA = 'Mozilla/5.0 (Linux; Android 13; Pixel 7) '
  + 'AppleWebKit/537.36 (KHTML, like Gecko) '
  + 'Chrome/126.0.0.0 Mobile Safari/537.36';

let browser = null;
let queue = Promise.resolve();

async function getBrowser() {
  if (!browser) {
    browser = await chromium.launch({
      executablePath: CHROME,
      headless: true,
      args: ['--no-sandbox', '--disable-dev-shm-usage',
        '--disable-blink-features=AutomationControlled']
    });
  }
  return browser;
}

async function resolve(movie, server) {
  const b = await getBrowser();
  const ctx = await b.newContext({
    userAgent: UA,
    viewport: { width: 390, height: 844 },
    isMobile: true, hasTouch: true
  });
  const page = await ctx.newPage();
  // Stealth toi thieu: an headless fingerprint truoc moi
  // document load (CF cham diem bot o day).
  await page.addInitScript(() => {
    Object.defineProperty(navigator, 'webdriver',
      { get: () => undefined });
    window.chrome = window.chrome || { runtime: {} };
    Object.defineProperty(navigator, 'plugins',
      { get: () => [1, 2, 3] });
    Object.defineProperty(navigator, 'languages',
      { get: () => ['en-US', 'en'] });
  }).catch(() => {});
  try {
    let url = 'https://vidfast.vc/movie/' + movie + '?autoPlay=true';
    if (server)
      url += '&server=' + encodeURIComponent(server);

    let hit = null;
    let headers = null;
    const done = new Promise((ok) => { hit = ok; });
    const timer = setTimeout(() => hit(null), 30000);
    page.on('request', (r) => {
      if (hit && r.url().includes('.m3u8')) {
        headers = {};
        for (const [k, v] of Object.entries(r.headers())) {
          const lk = k.toLowerCase();
          if (['host', 'accept-encoding', 'connection', 'range']
            .includes(lk)) continue;
          headers[k] = v;
        }
        const u = r.url();
        hit = null;
        clearTimeout(timer);
        done(u);
      }
    });
    await page.goto(url,
      { waitUntil: 'domcontentloaded', timeout: 25000 })
      .catch(() => {});
    await page.waitForTimeout(3000);
    await page.evaluate(() => {
      const s = '[aria-label*="play" i],[data-action*="play" i],'
        + '[class*="play" i],.vjs-big-play-button,button:has(svg),video';
      Array.from(document.querySelectorAll(s)).slice(0, 5)
        .forEach((n) => {
          if (n.tagName === 'VIDEO') n.play().catch(() => {});
          else n.click();
        });
    }).catch(() => {});
    // khung con (player that co the nam iframe rieng)
    for (const f of page.frames()) {
      try {
        await f.evaluate((s) => {
          Array.from(document.querySelectorAll(s)).slice(0, 5)
            .forEach((n) => {
              if (n.tagName === 'VIDEO') n.play().catch(() => {});
              else n.click();
            });
        }, '[aria-label*="play" i],button:has(svg),video');
      } catch (e) {}
    }
    const m3u8 = await done;
    clearTimeout(timer);
    return m3u8 ? { m3u8, headers } : null;
  } finally {
    await ctx.close().catch(() => {});
  }
}

const server = http.createServer((req, res) => {
  let u;
  try {
    u = new URL(req.url, 'http://x');
  } catch (e) {
    res.writeHead(400); res.end(); return;
  }
  if (u.pathname === '/health') {
    res.writeHead(200, { 'Content-Type': 'text/plain' });
    res.end('ok'); return;
  }
  if (u.pathname !== '/r') {
    res.writeHead(404); res.end(); return;
  }
  const movie = u.searchParams.get('movie');
  const srv = u.searchParams.get('server') || '';
  if (!movie) {
    res.writeHead(400, { 'Content-Type': 'application/json' });
    res.end('{"error":"bad params"}'); return;
  }
  const job = queue.then(() => resolve(movie, srv));
  queue = job.catch(() => null).then(() => {});
  const killer = setTimeout(() => {
    try {
      res.writeHead(503, { 'Content-Type': 'application/json' });
      res.end('{"error":"timeout"}');
    } catch (e) {}
  }, 45000);
  job.then((out) => {
    clearTimeout(killer);
    if (out) {
      res.writeHead(200, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify(out));
    } else {
      res.writeHead(503, { 'Content-Type': 'application/json' });
      res.end('{"error":"resolve failed"}');
    }
  }).catch(() => {
    clearTimeout(killer);
    try {
      res.writeHead(503, { 'Content-Type': 'application/json' });
      res.end('{"error":"resolve failed"}');
    } catch (e) {}
  });
});

server.listen(PORT, '127.0.0.1',
  () => console.log('vidfast resolver on 127.0.0.1:' + PORT));
