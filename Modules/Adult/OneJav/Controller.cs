using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Services;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace OneJav;

[Route("onejav")]
public class OneJavController : BaseController
{
    const RegexOptions RX = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline;

    static readonly IReadOnlyList<HeadersModel> BrowserHeaders = HeadersModel.Init(
        ("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36"),
        ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"),
        ("Accept-Language", "en-US,en;q=0.9"),
        ("Referer", "https://onejav.com/")
    );

    // ============================ Plugin JS ============================
    [HttpGet]
    [Route("onejav.js")]
    [Route("onejav/js/{token}")]
    public ActionResult Plugin(string token = null)
    {
        if (!ModInit.conf.enable)
            return StatusCode(403);

        string js = FileCache.ReadAllText($"{ModInit.modpath}/plugin.js", "onejav.js")
            .Replace("{localhost}", host)
            .Replace("{token}", token ?? string.Empty);
        return ContentTo(js, "application/javascript; charset=utf-8");
    }

    // ============================ List (newest / tag) ============================
    [HttpGet]
    [Route("onejav/list")]
    public async Task<ActionResult> List(string path = "", int page = 1)
    {
        if (!ModInit.conf.enable) return StatusCode(403);

        string url = string.IsNullOrWhiteSpace(path)
            ? $"{ModInit.conf.host}/"
            : $"{ModInit.conf.host}/tag/{HttpUtility.UrlPathEncode(path)}";
        if (page > 1) url += (url.Contains('?') ? "&" : "?") + "page=" + page;

        var items = await ParseList(url);
        return Json(new { results = items, hasMore = items.Count >= 24, page });
    }

    // ============================ Search ============================
    [HttpGet]
    [Route("onejav/search")]
    public async Task<ActionResult> Search(string q, int page = 1)
    {
        if (!ModInit.conf.enable) return StatusCode(403);
        if (string.IsNullOrWhiteSpace(q)) return Json(new { results = new List<object>(), hasMore = false });

        string url = $"{ModInit.conf.host}/search/{HttpUtility.UrlPathEncode(q.Trim())}";
        if (page > 1) url += "?page=" + page;

        var items = await ParseList(url);
        return Json(new { results = items, hasMore = items.Count >= 20, page, title = q.Trim() });
    }

    // ============================ Detail / magnets ============================
    [HttpGet]
    [Route("onejav/card")]
    public async Task<ActionResult> Card(string id)
    {
        if (!ModInit.conf.enable) return StatusCode(403);
        if (string.IsNullOrWhiteSpace(id)) return BadRequest();

        string url = $"{ModInit.conf.host}/torrent/{id}";
        string html = await Http.Get(url, timeoutSeconds: 25, headers: BrowserHeaders, useDefaultHeaders: false);
        if (string.IsNullOrEmpty(html))
            return Json(new { id, error = "Không tải được trang OneJAV (bị chặn mạng?)" });

        string poster = Abs(Rx.Match(html, "<meta[^>]+property=[\"']og:image[\"'][^>]+content=[\"']([^\"']+)[\"']")
            ?? Rx.Match(html, "<img[^>]+class=[\"'][^\"']*card-img[^\"']*[\"'][^>]+src=[\"']([^\"']+)[\"']"));

        string name = WebUtility.HtmlDecode(
            Rx.Match(html, "<meta[^>]+property=[\"']og:title[\"'][^>]+content=[\"']([^\"']+)[\"']") ?? id);
        string desc = WebUtility.HtmlDecode(
            Rx.Match(html, "<meta[^>]+property=[\"']og:description[\"'][^>]+content=[\"']([^\"']+)[\"']") ?? "");

        var actresses = new List<string>();
        foreach (var m in Rx.Matches("href=[\"']/actress/([^\"'?#]+)[\"']", html))
        {
            string a = WebUtility.UrlDecode(m.Groups[1].Value.Replace('-', ' '));
            if (!actresses.Contains(a)) actresses.Add(a);
        }

        // magnets nhúng trong trang
        var magnets = new List<JObject>();
        var seenHash = new HashSet<string>();

        void addMagnet(string source, string title, string hash, string magnet, int seeders)
        {
            if (string.IsNullOrEmpty(hash) || !seenHash.Add(hash)) return;
            magnets.Add(new JObject
            {
                ["source"] = source,
                ["title"] = string.IsNullOrWhiteSpace(title) ? name : title,
                ["hash"] = hash,
                ["seeders"] = seeders,
                // Lampa mở link này; controller add vào TorrServer rồi redirect tới stream.
                ["url"] = $"{host}/onejav/play?hash={hash}&magnet={HttpUtility.UrlEncode(magnet)}"
            });
        }

        foreach (Match m in Regex.Matches(html, "magnet:\\?xt=urn:btih:([a-zA-Z0-9]+)[^\"'\\s<>]*", RX))
        {
            string hash = m.Groups[1].Value.ToLowerInvariant();
            if (hash.Length < 32) continue;
            string dn = Regex.Match(m.Value, "dn=([^&\"'\\s<>]+)", RegexOptions.IgnoreCase) is Match d && d.Success
                ? WebUtility.UrlDecode(d.Groups[1].Value.Replace('+', ' ')) : name;
            addMagnet("OneJAV", dn, hash, m.Value, 0);
        }

        // fallback: sukebei / ijav theo mã
        if (magnets.Count == 0)
        {
            string code = id.ToUpperInvariant();
            if (ModInit.conf.use_sukebei)
                foreach (var s in await SearchSukebei(code))
                    addMagnet((string)s["source"], (string)s["title"], (string)s["hash"], (string)s["magnet"], s.Value<int>("seeders"));
            if (magnets.Count == 0 && ModInit.conf.use_ijav)
                foreach (var s in await SearchIjav(code))
                    addMagnet((string)s["source"], (string)s["title"], (string)s["hash"], (string)s["magnet"], s.Value<int>("seeders"));
        }

        return Json(new
        {
            id,
            title = name,
            original_title = id.ToUpperInvariant(),
            poster,
            img = poster,
            description = desc,
            actresses,
            magnets = magnets.OrderByDescending(x => x.Value<int>("seeders")).ThenBy(x => (string)x["source"]).Take(12).ToList()
        });
    }

    // ============================ Play (qua TorrServer) ============================
    [HttpGet]
    [Route("onejav/play")]
    public async Task<ActionResult> Play(string hash, int index = 0, string magnet = null)
    {
        if (!ModInit.conf.enable) return StatusCode(403);

        string ts = ModInit.TsHost();
        string addLink = !string.IsNullOrWhiteSpace(magnet) ? magnet : $"magnet:?xt=urn:btih:{hash}";

        string payload = "{\"action\":\"add\",\"link\":\"" + addLink.Replace("\\", "\\\\").Replace("\"", "\\\"") +
                         "\",\"title\":\"\",\"poster\":\"\",\"save_to_db\":false}";

        string resp = await Http.Post($"{ts}/torrents", payload, timeoutSeconds: 25, useDefaultHeaders: false);
        if (string.IsNullOrWhiteSpace(resp))
            return StatusCode(503, "TorrServer không phản hồi (" + ts + "). Cài/bật module TorrServer hoặc đặt torrserver trong init.conf.");

        string realHash = Rx.Match(resp, "\"hash\"\\s*:\\s*\"([^\"]+)\"");
        if (string.IsNullOrEmpty(realHash)) realHash = hash;

        // Chọn file video lớn nhất (bỏ sample/trailer). Hỏi tối đa vài lần để TorrServer kịp nạp metadata.
        int bestIndex = index;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            string stat = await Http.Get($"{ts}/stream?link={realHash}&index=0&stat", timeoutSeconds: 20, useDefaultHeaders: false);
            int picked = PickVideoIndex(stat);
            if (picked >= 0) { bestIndex = picked; break; }
            await Task.Delay(2000);
        }

        // Stream qua proxy /ts của lampac để TV không cần truy cập localhost TorrServer.
        return Redirect($"{host}/ts/stream?link={HttpUtility.UrlEncode(realHash)}&index={bestIndex}&play");
    }

    // ============================ Parsing list ============================
    async Task<List<JObject>> ParseList(string url)
    {
        string html = await Http.Get(url, timeoutSeconds: 25, headers: BrowserHeaders, useDefaultHeaders: false);
        var result = new List<JObject>();
        if (string.IsNullOrEmpty(html)) return result;

        var seen = new HashSet<string>();

        // Card: <a href="/torrent/CODE" ...><img ... src="..."> ...text... </a>
        foreach (Match block in Regex.Matches(html, "<a[^>]+href=[\"'](/torrent/[^\"']+)[\"'][^>]*>(.*?)</a>", RX))
        {
            string href = block.Groups[1].Value;
            string inner = block.Groups[2].Value;
            string code = href.Split('/').Last().Split('?')[0];
            if (string.IsNullOrEmpty(code) || !seen.Add(code)) continue;

            string img = Rx.Match(inner, "<img[^>]+src=[\"']([^\"']+)[\"']")
                ?? Rx.Match(inner, "<img[^>]+data-src=[\"']([^\"']+)[\"']");
            if (string.IsNullOrEmpty(img)) continue;

            string title = Regex.Replace(inner, "<[^>]+>", " ");
            title = WebUtility.HtmlDecode(Regex.Replace(title, "\\s+", " ").Trim());
            if (string.IsNullOrEmpty(title)) title = code;

            result.Add(new JObject
            {
                ["id"] = code,
                ["title"] = title,
                ["code"] = code.ToUpperInvariant(),
                ["poster"] = Abs(img),
                ["img"] = Abs(img),
                ["source"] = "onejav"
            });
        }

        // Fallback theo div.container (giống javfast)
        if (result.Count == 0)
        {
            foreach (Match c in Regex.Matches(html, "<div[^>]+class=[\"'][^\"']*container[^\"']*[\"'][^>]*>(.*?)</div>", RX))
            {
                string seg = c.Groups[1].Value;
                if (seg.Contains("Popular tags")) continue;

                string link = Rx.Match(seg, "href=[\"'](/torrent/[^\"']+)[\"']");
                string img = Rx.Match(seg, "<img[^>]+class=[\"'][^\"']*image[^\"']*[\"'][^>]+(?:src|data-src)=[\"']([^\"']+)[\"']");
                if (string.IsNullOrEmpty(link) || string.IsNullOrEmpty(img)) continue;

                string code = link.Split('/').Last().Split('?')[0];
                if (!seen.Add(code)) continue;

                result.Add(new JObject
                {
                    ["id"] = code,
                    ["title"] = code,
                    ["code"] = code.ToUpperInvariant(),
                    ["poster"] = Abs(img),
                    ["img"] = Abs(img),
                    ["source"] = "onejav"
                });
            }
        }

        return result;
    }

    // ============================ Sukebei / iJav fallback ============================
    async Task<List<JObject>> SearchSukebei(string code)
    {
        var list = new List<JObject>();
        try
        {
            string url = "https://sukebei.nyaa.si/?f=0&c=0_0&q=" + HttpUtility.UrlEncode(code);
            string html = await Http.Get(url, timeoutSeconds: 20, useDefaultHeaders: false);
            if (string.IsNullOrEmpty(html) || html.Contains("No results found")) return list;

            foreach (Match row in Regex.Matches(html, "<tr[^>]*>(.*?)</tr>", RX))
            {
                string seg = row.Groups[1].Value;
                string magnet = Rx.Match(seg, "href=[\"'](magnet:\\?xt=urn:btih:[^\"']+)[\"']");
                if (string.IsNullOrEmpty(magnet)) continue;

                string hm = Regex.Match(magnet, "btih:([a-zA-Z0-9]+)", RegexOptions.IgnoreCase) is Match h && h.Success
                    ? h.Groups[1].Value.ToLowerInvariant() : null;
                if (string.IsNullOrEmpty(hm)) continue;

                string title = WebUtility.HtmlDecode(Rx.Match(seg, "<a[^>]+title=[\"']([^\"']+)[\"']") ?? code);
                int seed = 0;
                var seedM = Regex.Matches(seg, "<td[^>]*class=[\"'][^\"']*(?:text-center|text-success)[^\"']*[\"'][^>]*>\\s*([0-9,]+)\\s*</td>", RX);
                if (seedM.Count > 0) int.TryParse(seedM[0].Groups[1].Value.Replace(",", ""), out seed);

                list.Add(new JObject
                {
                    ["source"] = seed > 0 ? $"Sukebei · S{seed}" : "Sukebei",
                    ["title"] = title.Trim(),
                    ["hash"] = hm,
                    ["magnet"] = magnet,
                    ["seeders"] = seed
                });
                if (list.Count >= 6) break;
            }
        }
        catch { }
        return list;
    }

    async Task<List<JObject>> SearchIjav(string code)
    {
        var list = new List<JObject>();
        try
        {
            string url = "https://ijavtorrent.com/?searchTerm=" + HttpUtility.UrlEncode(code);
            string html = await Http.Get(url, timeoutSeconds: 20, useDefaultHeaders: false);
            if (string.IsNullOrEmpty(html)) return list;

            foreach (Match m in Regex.Matches(html, "href=[\"'](magnet:\\?xt=urn:btih:[a-zA-Z0-9]+)[^\"']*[\"']", RX))
            {
                string magnet = m.Groups[1].Value;
                string hm = Regex.Match(magnet, "btih:([a-fA-F0-9]{40})") is Match h && h.Success
                    ? h.Groups[1].Value.ToLowerInvariant() : null;
                if (string.IsNullOrEmpty(hm)) continue;

                string dn = Regex.Match(magnet, "dn=([^&\"']+)", RegexOptions.IgnoreCase) is Match d && d.Success
                    ? WebUtility.UrlDecode(d.Groups[1].Value.Replace('+', ' ')) : code;

                list.Add(new JObject
                {
                    ["source"] = "iJavTorrent",
                    ["title"] = dn.Trim(),
                    ["hash"] = hm,
                    ["magnet"] = magnet,
                    ["seeders"] = 0
                });
                if (list.Count >= 5) break;
            }
        }
        catch { }
        return list;
    }

    // ============================ Helpers ============================
    static readonly string[] VideoExt = { ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".m4v", ".ts", ".flv" };
    static readonly string[] BadNames = { "sample", "trailer", "opening", "ending", "preview", "menu", "extra", "bonus" };

    /// <summary>Đọc JSON stat của TorrServer, chọn file video lớn nhất (bỏ sample). Trả về id hoặc -1.</summary>
    static int PickVideoIndex(string statJson)
    {
        if (string.IsNullOrWhiteSpace(statJson)) return -1;
        try
        {
            var stat = JObject.Parse(statJson);
            var files = stat["file_stats"] as JArray;
            if (files == null || files.Count == 0) return -1;

            int best = -1;
            long bestLen = -1;
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                string path = (f["path"]?.ToString() ?? "").ToLowerInvariant();
                string ext = System.IO.Path.GetExtension(path);
                if (!VideoExt.Contains(ext)) continue;

                string baseName = System.IO.Path.GetFileName(path);
                if (BadNames.Any(b => baseName.Contains(b))) continue;

                long len = f["length"]?.Value<long?>() ?? 0;
                if (len > bestLen) { bestLen = len; best = f["id"]?.Value<int?>() ?? i; }
            }
            return best;
        }
        catch { return -1; }
    }

    string Abs(string u)
    {
        if (string.IsNullOrEmpty(u)) return "";
        u = WebUtility.HtmlDecode(u);
        if (u.StartsWith("//")) return "https:" + u;
        if (u.StartsWith("http")) return u;
        if (u.StartsWith('/')) return ModInit.conf.host.TrimEnd('/') + u;
        return ModInit.conf.host.TrimEnd('/') + "/" + u;
    }
}
