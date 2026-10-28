using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.PlaywrightCore;
using Shared.Services;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace XNhau;

public class XNhauController : BaseSisiController
{
    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient();

    /// <summary>
    /// Relay media qua Cloudflare Worker (egress CF) — bật bằng env XNHAU_RELAY=https://worker.workers.dev
    /// Trang HTML/cookie vẫn đi trực tiếp (giữ domain cho cookie + Turnstile), chỉ media đi qua relay.
    /// </summary>
    static readonly string relayBase = System.Environment.GetEnvironmentVariable("XNHAU_RELAY")?.TrimEnd('/');

    static string Relay(string url)
    {
        if (string.IsNullOrEmpty(relayBase) || string.IsNullOrEmpty(url) || !url.StartsWith("http"))
            return url;

        // GetLocation co the tra ve chinh URL cua relay (redirect da follow) -> khong wrap lan 2
        if (url.StartsWith(relayBase, System.StringComparison.OrdinalIgnoreCase))
            return url;

        try
        {
            var u = new System.Uri(url);
            return $"{relayBase}/{u.Host}{u.PathAndQuery}";
        }
        catch
        {
            return url;
        }
    }

    public XNhauController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("xnhau")]
    async public Task<ActionResult> Index(string search, string sort, string c, string t, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
        return badInitMsg;

    rhubFallback:
        var cache = await InvokeCacheResult(ipkey($"xnhau:{search}:{sort}:{c}:{t}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            if (init.httpversion == 1)
                httpHydra.RegisterHttp(httpClient);

            List<PlaylistItem> playlists = null;

            string dbgUrl = Relay(XNhauTo.Uri(init.host, search, sort, c, t, pg));
            await httpHydra.GetSpan(dbgUrl, span =>
            {
                string h = span.ToString();
                playlists = XNhauTo.Playlist("xnhau/vidosik", span);
            }, addheaders: HeadersModel.Init(
                ("User-Agent", "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36"),
                ("Referer", init.host + "/")
            ));

            // Search khong ket qua -> `success` + list rong de app hien
            // "khong co ket qua", khong phai loi.
            if (playlists == null || playlists.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(search))
                    return e.Success(new List<PlaylistItem>());

                return e.Fail("playlists", refresh_proxy: true);
            }

            return e.Success(playlists);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, await MenuAsync(search, sort, c, t));
    }

    // Menu = head (Tim kiem + Sắp xếp theo context) + base taxonomy cache.
    // Menu khong con 1 key tinh: dong 2 doi theo search/sort/c/t.
    async Task<List<MenuItem>> MenuAsync(string search, string sort, string c, string t)
    {
        var menu = XNhauTo.MenuHead(host, search, sort, c, t);
        string memKey = ipkey("xnhau:menu");

        if (hybridCache.TryGetValue(memKey,
            out List<(string name, List<(string slug, string name)> items)> groups)
            && groups != null && groups.Count > 0)
        {
            menu.AddRange(XNhauTo.Menu(host, groups));
            return menu;
        }

        string hostLocal = host;

        try
        {
            var list = await FetchAsync(hostLocal + "/the-loai/");
            var tax = XNhauTo.Taxonomies(list);

            if (tax.Count > 0)
            {
                menu.AddRange(XNhauTo.Menu(hostLocal, tax));
                hybridCache.Set(memKey, tax, cacheTime(720), true);
                return menu;
            }
        }
        catch { }

        menu.AddRange(XNhauTo.Menu(hostLocal, null));
        return menu;
    }

    #region Login (Playwright/Chrome) — video members-only
    static readonly SemaphoreSlim loginLock = new SemaphoreSlim(1, 1);

    /// Cookie lấy được từ phiên login do module tự mở (ưu tiên hơn init.cookie)
    static string cookieCache;

    /// <summary>
    /// File cookie do script login ghi ra (mặc định: ~/lampac-run/xnhau.cookie).
    /// Module đọc lại khi mtime đổi ⇒ refresh cookie KHÔNG cần restart app.
    /// </summary>
    static readonly string CookieFile = System.IO.Path.Combine(System.AppContext.BaseDirectory, "xnhau.cookie");
    static System.DateTime cookieFileStamp;
    static string cookieFileValue;

    /// <summary>Chống gọi login liên tục khi cookie chết (tối đa 1 lần / 5 phút)</summary>
    static System.DateTime lastLoginAttemptUtc;

    /// <summary>Script login headful (Chrome trên Xvnc) — xem termux-native/xnhau-login.sh</summary>
    static readonly string LoginScript = System.Environment.GetEnvironmentVariable("XNHAU_LOGIN_SCRIPT")
        ?? System.IO.Path.Combine(
            System.Environment.GetEnvironmentVariable("HOME") ?? "/data/data/com.termux/files/home",
            "lampac-native", "xnhau-login.sh");

    bool CanLogin => !string.IsNullOrWhiteSpace(init.login) && !string.IsNullOrWhiteSpace(init.passwd);

    /// <summary>
    /// Cookie đang dùng, thứ tự ưu tiên: file xnhau.cookie (mới nhất) → cookieCache → init.cookie.
    /// </summary>
    string Cookie
    {
        get
        {
            try
            {
                var fi = new System.IO.FileInfo(CookieFile);
                if (fi.Exists && fi.LastWriteTimeUtc != cookieFileStamp)
                {
                    string text = System.IO.File.ReadAllText(CookieFile).Trim();
                    if (!string.IsNullOrEmpty(text))
                    {
                        cookieFileValue = text;
                        cookieCache = text;
                        init.cookie = text;
                        cookieFileStamp = fi.LastWriteTimeUtc;
                    }
                }
            }
            catch { }

            return !string.IsNullOrEmpty(cookieFileValue) ? cookieFileValue
                 : !string.IsNullOrEmpty(cookieCache) ? cookieCache
                 : init.cookie;
        }
    }

    /// Token Turnstile (compat=recaptcha → 2 input ẩn). Rỗng = chưa giải xong.
    static async Task<string> GetTurnstileTokenAsync(IPage page)
    {
        try
        {
            return await page.EvaluateAsync<string>(
                "() => { var a = document.querySelector('[name=\"cf-turnstile-response\"]'); " +
                "if (a && a.value) return a.value; " +
                "var b = document.querySelector('[name=\"g-recaptcha-response\"]'); " +
                "if (b && b.value) return b.value; return ''; }");
        }
        catch { return null; }
    }

    /// <summary>
    /// Làm mới cookie đăng nhập, ưu tiên script headful `~/lampac-native/xnhau-login.sh`
    /// (Chrome headful trên X server ảo Xvnc) vì Turnstile chỉ giải được ở chế độ đó.
    /// Fallback: Playwright trong app (chỉ thành công nếu có display / site tắt captcha).
    /// </summary>
    async Task<(bool ok, string msg)> LoginAsync(bool force)
    {
        if (!force && !string.IsNullOrEmpty(Cookie))
            return (true, "đã có cookie từ phiên login trước");

        if (!CanLogin)
            return (false, "init.conf thiếu login/passwd cho XNhau");

        if (!await loginLock.WaitAsync(System.TimeSpan.FromSeconds(90)))
            return (false, "đang có phiên login khác chạy");

        try
        {
            lastLoginAttemptUtc = System.DateTime.UtcNow;

            if (System.IO.File.Exists(LoginScript))
                return await LoginViaScriptAsync();

            if (Chromium.Status == PlaywrightStatus.disabled)
                return (false, $"Chromium disabled và không thấy script login ({LoginScript})");

            return await LoginViaPlaywrightAsync();
        }
        finally
        {
            loginLock.Release();
        }
    }

    /// <summary>
    /// Chạy script login headful (Chrome + Xvnc) rồi đọc lại file cookie.
    /// Script tự bật Xvnc :1, tự đăng nhập, tự ghi ~/lampac-run/xnhau.cookie.
    /// </summary>
    async Task<(bool ok, string msg)> LoginViaScriptAsync()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "bash",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add(LoginScript);

            // App chạy bằng dotnet-host glibc: LD_LIBRARY_PATH/LD_PRELOAD của cha làm
            // bash Termux không link được (libdl.so) ⇒ phải xoá, trả PATH về Termux.
            string prefix = System.Environment.GetEnvironmentVariable("PREFIX")
                ?? "/data/data/com.termux/files/usr";
            psi.Environment.Remove("LD_LIBRARY_PATH");
            psi.Environment.Remove("LD_PRELOAD");
            psi.Environment["PREFIX"] = prefix;
            psi.Environment["PATH"] = $"{prefix}/bin:/system/bin:/system/xbin";

            // truyền qua env, không qua argv (tránh lộ mật khẩu trong `ps`)
            psi.Environment["XNHAU_USER"] = init.login ?? "";
            psi.Environment["XNHAU_PASS"] = init.passwd ?? "";
            psi.Environment["XNHAU_COOKIE"] = CookieFile;

            using var p = System.Diagnostics.Process.Start(psi);
            if (p == null)
                return (false, "không chạy được script login");

            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();

            using var cts = new System.Threading.CancellationTokenSource(System.TimeSpan.FromMinutes(4));
            try
            {
                await p.WaitForExitAsync(cts.Token);
            }
            catch (System.OperationCanceledException)
            {
                try { p.Kill(true); } catch { }
                return (false, "script login quá 4 phút");
            }

            string stdout = (await stdoutTask).Trim();
            string stderr = (await stderrTask).Trim();
            System.Console.WriteLine($"XNhau login script rc={p.ExitCode} out={Trunc(stdout)} err={Trunc(stderr.Replace('\n', ' '))}");

            cookieFileStamp = default;              // buộc đọc lại file
            string cookie = Cookie;

            bool scriptOk = stdout.Contains("\"ok\":true");
            if (!scriptOk)
                return (false, $"script login lỗi (rc={p.ExitCode}): {Trunc(stdout.Length > 0 ? stdout : stderr)}");

            if (string.IsNullOrEmpty(cookie))
                return (false, "script login không ghi cookie");

            string html = await FetchAsync(init.host + "/");
            if (html == null || !(html.Contains("data-member=\"2\"") || html.Contains("Đăng xuất")))
                return (false, "script chạy xong nhưng site báo chưa phải thành viên");

            return (true, "ok (headful script)");
        }
        catch (System.Exception ex)
        {
            return (false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    static string Trunc(string s)
        => string.IsNullOrEmpty(s) ? "" : (s.Length > 200 ? s[..200] : s);

    /// <summary>
    /// Tự đăng nhập xnhau.pub bằng Chrome thật (Playwright) để lấy PHPSESSID.
    ///
    /// ĐO TRÊN MÁY NÀY (2026-10-28): Cloudflare Turnstile KHÔNG giải được khi
    /// Chrome chạy headless — widget vẽ ra checkbox nhưng không bao giờ trả token
    /// (đã thử click chuột theo toạ độ, Enter/Space, turnstile.execute(), spoof
    /// navigator.webdriver/plugins/languages, WebGL swiftshader → token vẫn rỗng).
    /// Headful (Xvnc) + UA thật + --disable-blink-features=AutomationControlled
    /// thì widget TỰ GIẢI, token ~730 ký tự ⇒ đường chuẩn là LoginViaScriptAsync().
    ///
    /// Chỉ trang detail cần cookie; file mp4 đã có v-acctoken ký sẵn trong URL.
    /// </summary>
    async Task<(bool ok, string msg)> LoginViaPlaywrightAsync()
    {
        if (Chromium.Status == PlaywrightStatus.disabled)
            return (false, "Chromium disabled (init.conf: chromium.enable / executablePath)");

        try
        {
            using var browser = new PlaywrightBrowser();
            var page = await browser.NewPageAsync("XNhau", new Dictionary<string, string>
            {
                ["Referer"] = init.host + "/"
            }, keepopen: true, imitationHuman: true);

            if (page == null)
                return (false, "Playwright trả page = null (Chromium chưa launch)");

            await page.GotoAsync(init.host + "/", new PageGotoOptions
            {
                Timeout = 45_000,
                WaitUntil = WaitUntilState.DOMContentLoaded
            });

            // mở popup đăng nhập như người dùng thật
            try
            {
                var open = page.Locator("a[data-fancybox][data-href*='login'], a[data-fancybox][href*='login'], a[data-href*='/login']");
                if (await open.CountAsync() > 0)
                {
                    await open.First.ClickAsync(new LocatorClickOptions { Timeout = 10_000 });
                    await page.WaitForTimeoutAsync(2500);
                }
            }
            catch { }

            try
            {
                await page.WaitForSelectorAsync("#login_username",
                    new PageWaitForSelectorOptions { Timeout = 15_000, State = WaitForSelectorState.Visible });
            }
            catch
            {
                return (false, "không mở được form đăng nhập");
            }

            await page.FillAsync("#login_username", init.login);
            await page.FillAsync("#login_pass", init.passwd);

            // Cloudflare Turnstile: bấm checkbox rồi chờ token tối đa ~20 s
            string token = null;
            for (int i = 0; i < 10; i++)
            {
                token = await GetTurnstileTokenAsync(page);
                if (!string.IsNullOrEmpty(token))
                    break;

                if (i == 0)
                {
                    try
                    {
                        var box = await page.Locator("div[data-recaptcha-key]").First.BoundingBoxAsync();
                        if (box != null)
                        {
                            await page.Mouse.MoveAsync(box.X + 24, box.Y + box.Height / 2);
                            await page.WaitForTimeoutAsync(300);
                            await page.Mouse.ClickAsync(box.X + 24, box.Y + box.Height / 2);
                        }
                    }
                    catch { }
                }

                await page.WaitForTimeoutAsync(2000);
            }

            System.Console.WriteLine($"XNhau login: turnstile token len={(token == null ? -1 : token.Length)}");

            await page.ClickAsync("form[data-form='ajax'] input[type='submit'], input.submit");

            string html = null;
            for (int i = 0; i < 6; i++)
            {
                await page.WaitForTimeoutAsync(2500);
                try { html = await page.ContentAsync(); } catch { html = null; }
                if (html != null && html.Contains("Đăng xuất"))
                    break;
            }

            if (html == null || !html.Contains("Đăng xuất"))
            {
                return (false, string.IsNullOrEmpty(token)
                    ? "Turnstile không trả token (Cloudflare chặn headless Chrome) — cần dán cookie thủ công vào init.conf"
                    : "đã có token nhưng vẫn chưa đăng nhập (sai mật khẩu?)");
            }

            var cookies = await page.Context.CookiesAsync();
            var sess = cookies.FirstOrDefault(c => c.Name == "PHPSESSID" && !string.IsNullOrWhiteSpace(c.Value));
            if (sess == null)
                return (false, "không thấy cookie PHPSESSID sau khi đăng nhập");

            // kt_member=1 (HttpOnly) mới là cookie quyết định "thành viên xem được video"
            var km = cookies.FirstOrDefault(c => c.Name == "kt_member" && !string.IsNullOrWhiteSpace(c.Value));
            cookieCache = $"PHPSESSID={sess.Value}; kt_member={(km == null ? "1" : km.Value)}; kt_tcookie=1";
            init.cookie = cookieCache;
            return (true, "ok");
        }
        catch (System.Exception ex)
        {
            return (false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
    #endregion

    async Task<string> FetchAsync(string url)
    {
        if (init.httpversion == 1)
            httpHydra.RegisterHttp(httpClient);

        string html = null;
        await httpHydra.GetSpan(Relay(url), span =>
        {
            html = span.ToString();
        }, addheaders: HeadersModel.Init(
            ("User-Agent", "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36"),
            ("Referer", init.host + "/"),
            ("Cookie", Cookie)));

        return html;
    }

    async Task<(Dictionary<string, string> links, bool userch)> ResolveLinksAsync(string uri)
    {
        SemaphorManager semaphore = null;
        string semaphoreKey = $"xnhau:view:{uri}";

        if (rch?.enable != true)
        {
            semaphore ??= new SemaphorManager(semaphoreKey, System.TimeSpan.FromSeconds(30));
            bool _acquired = await semaphore.WaitAsync();
            if (!_acquired)
                return (null, false);
        }

        try
        {
            string memKey = ipkey(semaphoreKey);
            if (!hybridCache.TryGetValue(memKey, out (Dictionary<string, string> links, bool userch) cache))
            {
                if (init.httpversion == 1)
                    httpHydra.RegisterHttp(httpClient);

                string url = XNhauTo.StreamLinksUri(uri);

                // Bookmark/history chi luu id so: mo trang video truc tiep theo id
                // Nếu bookmark lưu số thuần (vd: "540932") hoặc URL đầy đủ chứa ?uri=
                if (!string.IsNullOrEmpty(url)) {
                    if (System.Text.RegularExpressions.Regex.IsMatch(url, @"^[0-9]+$"))
                        url = $"{init.host}/video/{url}/";
                    else if (url.Contains("?uri=")) {
                        // Bookmark lưu URL như: https://xnhau.cab/video/540932/?uri=https://xnhau.cab/video/540932/
                        // Giữ nguyên URL (đã có host và video path)
                        url = url.Split("?uri=")[0]; // Lấy phần trước ?uri= để đảm bảo URL sạch
                        if (url.EndsWith("/")) url = url.TrimEnd('/');
                    } else if (url.StartsWith("/")) {
                        url = $"{init.host}{url}";
                    }
                }

                if (url == null)
                    return (null, false);

                // XNHAU_RELAY: TLS từ IP máy hay bị reset (SSL EOF) → lấy trang qua Worker
                string fetchUrl = Relay(url);

                await httpHydra.GetSpan(fetchUrl, span =>
                {
                    cache.links = XNhauTo.StreamLinks(span);
                }, addheaders: HeadersModel.Init(
                    ("User-Agent", "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36"),
                    ("Referer", init.host + "/"),
                    ("Cookie", Cookie)));

                // Video "chỉ thành viên mới xem": cookie chết/hết hạn → tự login lại bằng Chrome rồi thử lại
                if ((cache.links == null || cache.links.Count == 0) && CanLogin
                    && (System.DateTime.UtcNow - lastLoginAttemptUtc).TotalMinutes > 5)
                {
                    var (ok, _) = await LoginAsync(force: false);

                    if (ok)
                    {
                        await httpHydra.GetSpan(fetchUrl, span =>
                        {
                            cache.links = XNhauTo.StreamLinks(span);
                        }, addheaders: HeadersModel.Init(
                            ("User-Agent", "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36"),
                            ("Referer", init.host + "/"),
                            ("Cookie", Cookie)));
                    }
                }

                if (cache.links == null || cache.links.Count == 0)
                    return (null, false);

                proxyManager?.Success();

                cache.userch = rch?.enable == true;
                hybridCache.Set(memKey, cache, cacheTime(20));
            }

            return (cache.links, cache.userch);
        }
        finally
        {
            semaphore?.Release();
        }
    }

    /// <summary>Kiểm tra cookie đăng nhập còn sống không. Mở /xnhau/check.</summary>
    [HttpGet, Staticache(manually: true)]
    [Route("xnhau/check")]
    async public Task<ActionResult> Check()
    {
        string html = await FetchAsync(init.host + "/");
        bool member = html != null && (html.Contains("data-member=\"2\"") || html.Contains("Đăng xuất"));

        System.DateTime cookieFileAt;
        try { cookieFileAt = System.IO.File.Exists(CookieFile) ? System.IO.File.GetLastWriteTimeUtc(CookieFile) : default; }
        catch { cookieFileAt = default; }

        return Json(new
        {
            cookie = string.IsNullOrEmpty(Cookie) ? "missing" : (member ? "alive" : "dead"),
            member,
            login = CanLogin ? "da-cau-hinh" : "chua-cau-hinh",
            script = System.IO.File.Exists(LoginScript) ? "co" : "khong",
            cookieFile = cookieFileAt == default ? null : cookieFileAt.ToString("yyyy-MM-dd HH:mm:ss") + " UTC",
            chromium = Chromium.Status.ToString()
        });
    }

    /// <summary>
    /// Refresh cookie: chạy script headful (Chrome + Xvnc) rồi xác minh với site.
    /// Mở /xnhau/login để gọi thủ công; module cũng tự gọi khi gặp video members-only.
    /// </summary>
    [HttpGet, Staticache(manually: true)]
    [Route("xnhau/login")]
    async public Task<ActionResult> Login()
    {
        var (ok, msg) = await LoginAsync(force: true);
        return Json(new { ok, msg, cookie = ok ? "PHPSESSID=***" : null });
    }

    [HttpGet, Staticache(manually: true)]
    [Route("xnhau/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

    reset:
        var (links, userch) = await ResolveLinksAsync(uri);

        if (links == null || links.Count == 0)
        {
            if (IsRhubFallback())
                goto reset;

            return OnError("stream_links", refresh_proxy: true);
        }

        if (userch)
            return OnResult(links);

        return Json(links.ToDictionary(k => k.Key, v =>
            $"{host}/xnhau/strem?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}"));
    }


    [HttpGet]
    [Route("xnhau/uploader")]
    async public Task<ActionResult> Uploader(string uri)
    {
        if (await IsRequestBlocked(rch: true))
            return Json(new { name = "", member = "" });

        string url = XNhauTo.StreamLinksUri(uri);
        if (!string.IsNullOrEmpty(url) && System.Text.RegularExpressions.Regex.IsMatch(url, @"^[0-9]+$"))
            url = $"{init.host}/video/{url}/";
        if (string.IsNullOrEmpty(url))
            return Json(new { name = "", member = "" });

        string memKey = ipkey($"xnhau:uploader:{url}");
        if (!hybridCache.TryGetValue(memKey, out Dictionary<string, string> info))
        {
            info = null;
            await httpHydra.GetSpan(Relay(url), span =>
            {
                var m = System.Text.RegularExpressions.Regex.Match(span.ToString(),
                    @"<a class=""avatar"" href=""((?:https?://[^/]+)?/members/[0-9]+/)""[^>]*title=""([^""]+)""");
                if (m.Success)
                {
                    string murl = m.Groups[1].Value;
                    if (murl.StartsWith("/"))
                        murl = $"{init.host}{murl}";
                    info = new Dictionary<string, string>
                    {
                        ["name"] = HttpUtility.HtmlDecode(m.Groups[2].Value),
                        ["member"] = murl
                    };
                }
            }, addheaders: HeadersModel.Init(
                ("User-Agent", "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36"),
                ("Referer", init.host + "/"),
                ("Cookie", Cookie)));

            if (info == null)
                return OnError("uploader");

            hybridCache.Set(memKey, info, cacheTime(60));
        }

        return Json(info);
    }

    [HttpGet]
    [Route("xnhau/strem")]
    async public Task<ActionResult> Strem(string link, string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (rch?.enable == true && 484 > rch.InfoConnected()?.apkVersion)
        {
            rch.Disabled();
            if (!init.rhub_fallback)
                return OnError("apkVersion", false);
        }

        if (!string.IsNullOrEmpty(uri) && !string.IsNullOrEmpty(q))
        {
            var (links, _) = await ResolveLinksAsync(uri);
            if (links == null || !links.TryGetValue(q, out link) || string.IsNullOrEmpty(link))
                return OnError("stream_links", refresh_proxy: true);
        }

        if (string.IsNullOrEmpty(link))
            return OnError("link");

        SemaphorManager semaphore = null;
        string semaphoreKey = $"xnhau:strem:{link}";

        if (rch?.enable != true)
        {
            semaphore ??= new SemaphorManager(semaphoreKey, System.TimeSpan.FromSeconds(30));
            bool _acquired = await semaphore.WaitAsync();
            if (!_acquired)
                return OnError();
        }

        try
        {
            string memKey = ipkey(semaphoreKey);
            if (!hybridCache.TryGetValue(memKey, out string location))
            {
                var headers = httpHeaders(init, HeadersModel.Init(
                    ("sec-fetch-dest", "document"),
                    ("sec-fetch-mode", "navigate"),
                    ("sec-fetch-site", "none"),
                    ("referer", init.host + "/")
                ));

                if (rch?.enable == true)
                {
                    var res = await rch.Headers(init.cors(link, headers, requestInfo), null, headers);
                    location = res.currentUrl;
                }
                else
                {
                    location = await Http.GetLocation(init.cors(Relay(link), headers, requestInfo), timeoutSeconds: init.httptimeout, httpversion: init.httpversion, proxy: proxy, headers: headers);
                }

                if (string.IsNullOrEmpty(location) || link == location)
                {
                    proxyManager?.Success();
                    var direct = httpHeaders(init, HeadersModel.Init(
                        ("referer", init.host + "/")
                    ));
                    hybridCache.Set(memKey, link, cacheTime(40));
                    return Redirect(HostStreamProxy(Relay(link), direct));
                }

                proxyManager?.Success();
                hybridCache.Set(memKey, location, cacheTime(40));
            }

            return Redirect(HostStreamProxy(Relay(location)));
        }
        finally
        {
            semaphore?.Release();
        }
    }
}
