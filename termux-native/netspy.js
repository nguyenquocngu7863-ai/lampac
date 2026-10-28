#!/usr/bin/env node
/**
 * netspy.js — mở 1 URL bằng Chrome THẬT (headful trên Xvnc) và ghi lại mọi request
 * để biết site thật sự gọi host nào, thay vì phải đọc/deobfuscate JS.
 *
 * Dùng: node netspy.js <url> [giây] [profile-dir]
 *   env: NETSPY_PROFILE, NETSPY_OUT
 * Ra: JSON {requests:[…], cookies:[…], title, url} + in bảng gọn ra stderr.
 *
 * Vì sao headful: nhiều site (Cloudflare/DDos-Guard) chặn headless ⇒ trang không load
 * ⇒ không thấy request nào. Xem termux-native/xnhau-login.sh để biết cách bật Xvnc.
 */
const fs = require('fs');

const HOME = process.env.HOME || '/data/data/com.termux/files/home';
const PW_PKG = process.env.NETSPY_PW_PKG || `${HOME}/lampac-run/.playwright/package`;
const CHROME = process.env.NETSPY_CHROME || `${HOME}/lampac-native/chrome/chrome-run.sh`;
const PROFILE = process.argv[4] || process.env.NETSPY_PROFILE || `${HOME}/lampac-native/netspy-profile`;
const OUT = process.env.NETSPY_OUT || `${HOME}/lampac-native/tmp/netspy.json`;

const URL_ARG = process.argv[2];
const WAIT = Math.max(5, Number(process.argv[3] || 20));

const MEDIA = /\.(m3u8|mpd|mp4|webm|mkv|ts|m4s|key)(\?|$)/i;
const APIish = /\/(api|ajax|player|video|stream|source|get|embed|media|play)\b/i;

const { chromium } = require(PW_PKG);
const log = (...a) => console.error('[netspy]', ...a);

(async () => {
  if (!URL_ARG) { console.error('dùng: netspy.js <url> [giây] [profile]'); process.exit(2); }
  fs.mkdirSync(PROFILE, { recursive: true });
  fs.mkdirSync(require('path').dirname(OUT), { recursive: true });

  const hits = [];
  const push = (o) => { if (hits.length < 400) hits.push(o); };

  const ctx = await chromium.launchPersistentContext(PROFILE, {
    executablePath: CHROME,
    headless: false,
    viewport: { width: 1280, height: 900 },
    ignoreHTTPSErrors: true,
    args: ['--no-sandbox', '--disable-setuid-sandbox', '--disable-dev-shm-usage',
      '--no-first-run', '--disable-blink-features=AutomationControlled']
  });

  // NETSPY_COOKIE="a=b; c=d" → tiêm cookie (dùng cookie của module để xem trang member)
  const COOKIE = process.env.NETSPY_COOKIE || '';
  if (COOKIE) {
    const host = new URL(URL_ARG).hostname;
    const list = COOKIE.split(';').map(x => x.trim()).filter(Boolean).map(pair => {
      const i = pair.indexOf('=');
      return { name: pair.slice(0, i), value: pair.slice(i + 1), domain: host, path: '/' };
    }).filter(c => c.name && c.value !== undefined);
    await ctx.addCookies(list);
    log('tiêm', list.length, 'cookie cho', host);
  }

  const page = ctx.pages()[0] || await ctx.newPage();

  page.on('request', r => {
    const u = r.url();
    const t = r.resourceType();
    if (!(MEDIA.test(u) || t === 'xhr' || t === 'fetch' || t === 'media' || APIish.test(u))) return;
    const h = r.headers();
    push({
      kind: 'req', t, method: r.method(), url: u,
      referer: h['referer'] || '', cookie: h['cookie'] ? 'yes' : '',
      body: (r.postData() || '').slice(0, 300)
    });
  });

  let docHtml = '';
  page.on('response', async r => {
    const u = r.url();
    if (!docHtml && r.request().resourceType() === 'document' && r.status() < 400) {
      try { docHtml = (await r.text()).slice(0, 4_000_000); } catch { }
    }
    if (!(MEDIA.test(u) || APIish.test(u))) return;
    const h = r.headers();
    let body = '';
    if (MEDIA.test(u)) body = '';                     // media: không đọc body
    else if ((h['content-type'] || '').match(/json|javascript|text/i)) {
      try { body = (await r.text()).slice(0, 500); } catch { }
    }
    push({
      kind: 'res', status: r.status(), url: u,
      ct: h['content-type'] || '', len: h['content-length'] || '',
      loc: h['location'] || '', body: body.replace(/\s+/g, ' ')
    });
  });

  await page.goto(URL_ARG, { waitUntil: 'domcontentloaded', timeout: 60000 }).catch(e => log('goto:', e.message));
  // bấm nút play nếu có, để player thật sự gọi stream
  await page.waitForTimeout(2500);
  // ưu tiên player CHÍNH (video thật), tránh bấm nhầm banner quảng cáo
  for (const sel of ['.player video', '.video-wrap video', '.video-box video', '#player video',
                     '.kt_player', '#kt_player', '.fp-play', '.play-button', '.poster',
                     'video', '.vjs-big-play-button', '.plyr__control--overlaid']) {
    try {
      const el = page.locator(sel).first();
      if (await el.count()) { await el.click({ timeout: 1500, force: true }); log('click', sel); break; }
    } catch { }
  }
  await page.waitForTimeout(WAIT * 1000);

  log('docHtml len =', docHtml.length, '| video_url:', docHtml.includes('video_url'),
      '| acctoken:', docHtml.includes('acctoken'), '| no-player:', docHtml.includes('no-player'));
  const cookies = (await ctx.cookies()).map(c => `${c.name}=${c.value}`).join('; ');
  const title = await page.title().catch(() => '');

  // link nhúng trong JS inline (kiểu KVS `video_url: '…'`, `file:`, `sources:`) —
  // nhiều site không gọi XHR mà chỉ khai báo biến rồi player mới fetch.
  const inlineLinks = await page.evaluate((html) => {
    if (!html) return [];
    const re = /(?:video_url|video_alt_url|file|hls|source|stream)\s*[:=]\s*['"]([^'"\s]{20,400})['"]/gi;
    const out = new Set(); let m;
    while ((m = re.exec(html)) !== null) {
      if (/\.(mp4|m3u8|mpd|webm)|acctoken|\/get_file\//i.test(m[1])) out.add(m[1]);
      if (out.size > 25) break;
    }
    return [...out];
  }, docHtml || '').catch(() => []);

  // src khai báo trong DOM — nhiều site giấu link ở đây thay vì gọi XHR
  const domMedia = await page.evaluate(() => Array.from(document.querySelectorAll('video, source'))
    .map(v => v.currentSrc || v.src || '').filter(Boolean)).catch(() => []);

  // in bảng gọn: chỉ dòng đáng đọc
  const seen = new Set();
  for (const h of hits) {
    const k = h.kind + h.url;
    if (seen.has(k)) continue;
    seen.add(k);
    if (h.kind === 'res' && h.status >= 300 && h.status < 400)
      log(h.status, '→', h.loc.slice(0, 110) || '(redirect)');
    else if (MEDIA.test(h.url))
      log((h.status || '') + (h.ct ? ' ' + h.ct : ''), h.url.slice(0, 150));
  }

  fs.writeFileSync(OUT, JSON.stringify({ url: URL_ARG, title, cookies, inlineLinks, domMedia, requests: hits }, null, 1));
  if (process.env.NETSPY_DUMP_HTML) fs.writeFileSync(OUT + '.html', docHtml || '');
  console.log(JSON.stringify({
    ok: true, title, out: OUT,
    inlineLinks,
    domMedia,
    media: [...new Set(hits.filter(h => MEDIA.test(h.url)).map(h => h.url))].slice(0, 12)
  }));
  await ctx.close();
  process.exit(0);
})().catch(e => { console.error('FATAL', e && e.message); process.exit(1); });
