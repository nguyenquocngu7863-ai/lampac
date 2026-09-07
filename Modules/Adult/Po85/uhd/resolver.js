const http = require('http');
const { chromium } = require('playwright-core');

const PORT_ARG = process.argv.indexOf('--port');
const PORT = PORT_ARG >= 0 ? parseInt(process.argv[PORT_ARG + 1] || '9196', 10) : 9196;
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

// Mo trang /vi/ bang Chrome that, dat video.src = file 4K, bat response 302
// de lay signed CDN URL (remote_control.php). Tra ve location hoac null.
async function resolve4k(pageUrl, fileUrl) {
  const b = await getBrowser();
  const ctx = await b.newContext({
    userAgent: UA,
    viewport: { width: 390, height: 844 },
    isMobile: true, hasTouch: true
  });
  const page = await ctx.newPage();
  try {
    const target = fileUrl + (fileUrl.includes('?') ? '&' : '?') + 'rnd=' + Date.now();
    const wantPath = fileUrl.split('?')[0];
    let done;
    const found = new Promise((resolve) => { done = resolve; });
    const timer = setTimeout(() => done(null), 25000);
    const onResp = (r) => {
      // chi nhan 302 cua dung file 4K yeu cau (bo qua autoplay 480p cua player)
      if (r.status() === 302 && r.url().startsWith(wantPath))
        done(r.headers()['location'] || '');
    };
    page.on('response', onResp);
    try {
      await page.goto(pageUrl, { waitUntil: 'domcontentloaded', timeout: 25000 }).catch(() => {});
      await page.waitForTimeout(4000);
      await page.evaluate((u) => {
        let v = document.querySelector('video');
        if (!v) { v = document.createElement('video'); v.muted = true; document.body.appendChild(v); }
        v.src = u;
        v.load();
      }, target);
      const loc = await found;
      clearTimeout(timer);
      return loc || null;
    } finally {
      clearTimeout(timer);
      page.off('response', onResp);
    }
  } finally {
    await ctx.close().catch(() => {});
  }
}

const server = http.createServer((req, res) => {
  let u;
  try { u = new URL(req.url, 'http://x'); } catch (e) { res.writeHead(400); res.end(); return; }
  if (u.pathname === '/health') { res.writeHead(200, { 'Content-Type': 'text/plain' }); res.end('ok'); return; }
  if (u.pathname !== '/r') { res.writeHead(404); res.end(); return; }
  const pageUrl = u.searchParams.get('page');
  const fileUrl = u.searchParams.get('file');
  if (!pageUrl || !fileUrl || !fileUrl.includes('/get_file/')) {
    res.writeHead(400, { 'Content-Type': 'application/json' });
    res.end('{"error":"bad params"}');
    return;
  }
  const job = queue.then(() => resolve4k(pageUrl, fileUrl));
  queue = job.catch(() => null).then(() => {});
  const killer = setTimeout(() => {
    try { res.writeHead(503, { 'Content-Type': 'application/json' }); res.end('{"error":"timeout"}'); } catch (e) {}
  }, 45000);
  job.then((loc) => {
    clearTimeout(killer);
    if (loc) {
      res.writeHead(200, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ location: loc }));
    } else {
      res.writeHead(503, { 'Content-Type': 'application/json' });
      res.end('{"error":"resolve failed"}');
    }
  }).catch(() => {
    clearTimeout(killer);
    try { res.writeHead(503, { 'Content-Type': 'application/json' }); res.end('{"error":"resolve failed"}'); } catch (e) {}
  });
});

server.listen(PORT, '127.0.0.1', () => console.log('po85-uhd resolver on 127.0.0.1:' + PORT));
