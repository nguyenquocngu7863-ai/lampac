#!/usr/bin/env node
/**
 * xnhau-login.js — tự đăng nhập xnhau.pub để lấy cookie PHPSESSID (+ kt_member).
 *
 * VÌ SAO PHẢI HEADFUL: Cloudflare Turnstile KHÔNG trả token khi Chrome headless
 * (đo 2026-10-28: click toạ độ, Enter/Space, turnstile.execute(), spoof
 * navigator.webdriver/plugins/languages/hardwareConcurrency, WebGL swiftshader,
 * cả CDP thuần với Input.dispatchMouseEvent → token luôn rỗng).
 * Chrome HEADFUL trên X server ảo (Xvnc :1) + UA thật (KHÔNG override) +
 * --disable-blink-features=AutomationControlled → widget tự giải, token ~730 ký tự.
 *
 * Dùng: node xnhau-login.js <username> <password> [cookie-out]
 *   hoặc env XNHAU_USER / XNHAU_PASS / XNHAU_COOKIE
 * In JSON ra stdout: {"ok":true,"cookie":"PHPSESSID=…; kt_member=1; kt_tcookie=1","member":"2"}
 * Exit: 0 ok, 2 thiếu user/pass, 3 Turnstile không trả token, 4 login sai, 1 lỗi khác.
 */
const fs = require('fs');

const HOME = process.env.HOME || '/data/data/com.termux/files/home';
const PW_PKG = process.env.XNHAU_PW_PKG || `${HOME}/lampac-run/.playwright/package`;
const CHROME = process.env.XNHAU_CHROME || `${HOME}/lampac-native/chrome/chrome-run.sh`;
const PROFILE = process.env.XNHAU_PROFILE || `${HOME}/lampac-native/xnhau-profile`;
const TMP = process.env.XNHAU_TMP || `${HOME}/lampac-native/tmp/probe`;

const USER = process.argv[2] || process.env.XNHAU_USER || '';
const PASS = process.argv[3] || process.env.XNHAU_PASS || '';
const OUT = process.argv[4] || process.env.XNHAU_COOKIE || `${HOME}/lampac-run/xnhau.cookie`;

const { chromium } = require(PW_PKG);
const sleep = ms => new Promise(r => setTimeout(r, ms));
const log = (...a) => console.error('[xnhau-login]', ...a);
const done = (obj, code) => { console.log(JSON.stringify(obj)); process.exit(code); };

(async () => {
  fs.mkdirSync(PROFILE, { recursive: true });
  fs.mkdirSync(TMP, { recursive: true });

  let ctx;
  try {
    ctx = await chromium.launchPersistentContext(PROFILE, {
      executablePath: CHROME,
      headless: false,                       // BẮT BUỘC: headless → Turnstile chặn
      viewport: { width: 1280, height: 860 },
      args: [
        '--no-sandbox', '--disable-setuid-sandbox', '--disable-dev-shm-usage',
        '--no-first-run', '--disable-blink-features=AutomationControlled'
      ]
    });
  } catch (e) {
    done({ ok: false, msg: 'không launch được Chrome: ' + String(e.message || e).slice(0, 120) }, 1);
  }

  const isMember = async page => {
    try {
      return await page.evaluate(() => {
        if (document.body && document.body.innerText.includes('Đăng xuất')) return true;
        const x = document.querySelector('#xnia');
        return !!(x && x.dataset && x.dataset.member && x.dataset.member !== '0');
      });
    } catch { return false; }
  };

  try {
    const page = ctx.pages()[0] || await ctx.newPage();
    const home = await ctx.cookies('https://xnhau.pub/');
    log('profile có', home.length, 'cookie');

    // ---------- fast path: profile còn phiên ----------
    await page.goto('https://xnhau.pub/', { waitUntil: 'domcontentloaded', timeout: 45000 });
    if (await isMember(page)) {
      log('profile còn phiên → lấy cookie luôn');
    } else {
      // ---------- login ----------
      if (!USER || !PASS) done({ ok: false, msg: 'profile hết phiên và thiếu username/password' }, 2);

      await page.locator("a[data-fancybox][data-href*='login'], a[data-fancybox][href*='login'], a[data-href*='/login']")
        .first().click({ timeout: 10000 }).catch(() => { });
      await page.waitForTimeout(2500);

      try {
        await page.waitForSelector('#login_username', { state: 'visible', timeout: 20000 });
      } catch {
        await page.screenshot({ path: `${TMP}/xnhau-login-nopopup.png` }).catch(() => { });
        done({ ok: false, msg: 'không mở được form đăng nhập' }, 4);
      }

      const box = await page.evaluate(() => {
        const d = document.querySelector('div[data-recaptcha-key]');
        if (!d) return null;
        const r = d.getBoundingClientRect();
        return { x: r.x, y: r.y, w: r.width, h: r.height };
      });

      const tokenOf = async () => await page.evaluate(() => {
        const a = document.querySelector('[name="cf-turnstile-response"]');
        if (a && a.value) return a.value;
        const b = document.querySelector('[name="g-recaptcha-response"]');
        return (b && b.value) || '';
      }).catch(() => '');

      // bấm widget như người thật, lặp vài lần (CF chập chờn)
      let token = '';
      for (let i = 0; i < 22; i++) {
        token = await tokenOf();
        if (token) break;
        if (box && box.w > 0 && (i === 0 || i === 4 || i === 9 || i === 14)) {
          const cx = box.x + 24, cy = box.y + box.h / 2;
          await page.mouse.move(cx - 120 - i * 8, cy - 60, { steps: 12 }).catch(() => { });
          await page.waitForTimeout(300);
          await page.mouse.move(cx, cy, { steps: 8 }).catch(() => { });
          await page.waitForTimeout(400);
          await page.mouse.click(cx, cy).catch(() => { });
          log('bấm widget Turnstile (lần', i, ')');
        }
        await sleep(2000);
      }
      if (!token) {
        await page.screenshot({ path: `${TMP}/xnhau-login-notoken.png` }).catch(() => { });
        done({ ok: false, msg: 'Turnstile không trả token' }, 3);
      }
      log('token', token.length);

      await page.fill('#login_username', USER);
      await page.waitForTimeout(200);
      await page.fill('#login_pass', PASS);
      await page.waitForTimeout(300);
      await page.click("form[data-form='ajax'] input[type='submit']");

      let member = false;
      for (let i = 0; i < 8; i++) {
        await sleep(2500);
        member = await isMember(page);
        if (member) break;
        if (i === 3) await page.goto('https://xnhau.pub/', { waitUntil: 'domcontentloaded', timeout: 45000 }).catch(() => { });
      }
      if (!member) {
        await page.screenshot({ path: `${TMP}/xnhau-login-fail.png` }).catch(() => { });
        done({ ok: false, msg: 'đăng nhập không thành công (sai mật khẩu?)' }, 4);
      }
      log('đăng nhập OK');
    }

    // ---------- xuất cookie ----------
    const ck = await ctx.cookies('https://xnhau.pub/');
    const sess = ck.find(c => c.name === 'PHPSESSID' && c.value);
    if (!sess) done({ ok: false, msg: 'không thấy PHPSESSID' }, 1);

    const km = ck.find(c => c.name === 'kt_member' && c.value);
    const cookie = `PHPSESSID=${sess.value}; kt_member=${km ? km.value : '1'}; kt_tcookie=1`;
    fs.writeFileSync(OUT, cookie + '\n');
    log('đã ghi', OUT);

    const x = await page.evaluate(() => {
      const el = document.querySelector('#xnia');
      return el && el.dataset ? el.dataset.member || '' : '';
    }).catch(() => '');

    await ctx.close();
    done({ ok: true, cookie, member: x || '2', out: OUT }, 0);
  } catch (e) {
    try { await ctx.close(); } catch (e2) { }
    done({ ok: false, msg: String((e && e.message) || e).slice(0, 160) }, 1);
  }
})();
