using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace OneJav;

public class OneJavController : BaseSisiController
{
    const RegexOptions RX = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline;

    static readonly IReadOnlyList<HeadersModel> BrowserHeaders = HeadersModel.Init(
        ("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36"),
        ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"),
        ("Accept-Language", "en-US,en;q=0.9"),
        ("Referer", "https://onejav.com/")
    );

    public OneJavController() : base(ModInit.conf) { }

    // ============================ List / search / tag ============================
    [HttpGet]
    [Route("oj")]
    public async Task<ActionResult> Index(string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        int page = pg < 1 ? 1 : pg;

        string url;
        if (!string.IsNullOrWhiteSpace(search))
            url = $"{init.host}/search/{HttpUtility.UrlPathEncode(search.Trim())}" + (page > 1 ? $"?page={page}" : "");
        else if (!string.IsNullOrWhiteSpace(c))
            url = $"{init.host}/tag/{HttpUtility.UrlPathEncode(c.Trim())}" + (page > 1 ? $"?page={page}" : "");
        else
            url = $"{init.host}/" + (page > 1 ? $"?page={page}" : "");

        string html = await httpHydra.Get(url, BrowserHeaders, useDefaultHeaders: false);
        var playlists = ParsePlaylist(html, "oj/view");

        if (playlists == null || playlists.Count == 0)
            return OnError("playlists", refresh_proxy: string.IsNullOrWhiteSpace(search));

        return PlaylistResult(playlists, false, OneJavTo.Menu(host, search), total_pages: page + 1);
    }

    // ============================ Detail: danh sách magnet (chọn nguồn) ============================
    [HttpGet]
    [Route("oj/view")]
    public async Task<ActionResult> View(string uri)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        string code = (uri ?? "").Trim();
        var qualitys = new Dictionary<string, string>();

        string pageUrl = $"{init.host}/torrent/{code}";
        string html = await httpHydra.Get(pageUrl, BrowserHeaders, useDefaultHeaders: false);

        var seen = new HashSet<string>();

        void addSource(string label, string hash, string magnet)
        {
            if (string.IsNullOrEmpty(hash) || !seen.Add(hash)) return;
            string play = $"{host}/oj/play?hash={hash}&magnet={HttpUtility.UrlEncode(magnet)}";
            string key = label;
            int n = 2;
            while (qualitys.ContainsKey(key)) key = $"{label} #{n++}";
            qualitys[key] = play;
        }

        if (!string.IsNullOrEmpty(html))
        {
            foreach (Match m in Regex.Matches(html, "magnet:\\?xt=urn:btih:([a-zA-Z0-9]+)[^\"'\\s<>]*", RX))
            {
                string hash = m.Groups[1].Value.ToLowerInvariant();
                if (hash.Length < 32) continue;
                addSource("OneJAV", hash, m.Value);
            }
        }

        // Luôn dò thêm Sukebei + ijav theo mã (và các biến thể mã); dừng ở biến thể
        // đầu tiên cho kết quả (biến thể đầu là mã gốc — chính xác nhất).
        var found = new List<JObject>();
        foreach (string q in OneJavTo.SearchQueries(code))
        {
            var sk = await SearchSukebei(q);
            var ij = sk.Count == 0 ? await SearchIjav(q) : new List<JObject>();
            found.AddRange(sk);
            found.AddRange(ij);
            if (found.Count > 0) break;
        }

        // Sắp xếp theo seed giảm dần (Sukebei có seed; OneJAV/ijav seed=0) rồi gán link.
        found = found.OrderByDescending(s => s.Value<int?>("seeders") ?? 0).ToList();
        foreach (var s in found)
            addSource((string)s["source"], (string)s["hash"], (string)s["magnet"]);

        if (qualitys.Count == 0)
            return OnError("stream_links.qualitys", refresh_proxy: true);

        return OnResult(qualitys);
    }

    // ============================ Play: thêm vào TorrServer rồi redirect stream ============================
    [HttpGet]
    [Route("oj/play")]
    public async Task<ActionResult> Play(string hash, string magnet = null)
    {
        var (ts, tsHeaders) = ModInit.TsConn();
        string addLink = !string.IsNullOrWhiteSpace(magnet) ? magnet : $"magnet:?xt=urn:btih:{hash}";

        string payload = "{\"action\":\"add\",\"link\":\"" + addLink.Replace("\\", "\\\\").Replace("\"", "\\\"") +
                         "\",\"title\":\"\",\"poster\":\"\",\"save_to_db\":false}";

        string resp = await Http.Post($"{ts}/torrents", payload, timeoutSeconds: 30, headers: tsHeaders, useDefaultHeaders: false);
        if (string.IsNullOrWhiteSpace(resp))
            return StatusCode(503, "TorrServer không phản hồi (" + ts + "). Bật module TorrServer.");

        string realHash = Rx.Match(resp, "\"hash\"\\s*:\\s*\"([^\"]+)\"");
        if (string.IsNullOrEmpty(realHash)) realHash = hash;

        int bestIndex = 0;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            string stat = await Http.Get($"{ts}/stream?link={realHash}&index=0&stat", timeoutSeconds: 20, headers: tsHeaders, useDefaultHeaders: false);
            int picked = OneJavTo.PickVideoIndex(stat);
            if (picked >= 0) { bestIndex = picked; break; }
            await Task.Delay(2500);
        }

        // stream qua proxy /ts của lampac (proxy tự gắn auth; TV không cần truy cập localhost)
        return Redirect($"{host}/ts/stream?link={HttpUtility.UrlEncode(realHash)}&index={bestIndex}&play");
    }

    // ============================ Parsing ============================
    List<PlaylistItem> ParsePlaylist(string html, string route)
    {
        if (string.IsNullOrEmpty(html)) return null;

        var list = new List<PlaylistItem>();
        var seen = new HashSet<string>();

        void Add(string code, string img, string name)
        {
            if (string.IsNullOrEmpty(code) || !seen.Add(code)) return;
            if (string.IsNullOrEmpty(img)) return;
            if (string.IsNullOrWhiteSpace(name)) name = code;
            list.Add(new PlaylistItem
            {
                name = WebUtility.HtmlDecode(name).Trim(),
                picture = OneJavTo.Abs(img, init.host),
                video = $"{route}?uri={HttpUtility.UrlEncode(code)}",
                json = true
            });
        }

        // Cấu trúc onejav: mỗi bài là một <div class="container"> chứa link /torrent/ + ảnh.
        foreach (Match c in Regex.Matches(html, "<div[^>]+class=[\"'][^\"']*\\bcontainer\\b[^\"']*[\"'][^>]*>(.*?)</div>\\s*(?=<div[^>]+class=[\"'][^\"']*\\bcontainer\\b|<footer|$)", RX))
        {
            string seg = c.Groups[1].Value;
            if (seg.Contains("Popular tags")) continue;

            string link = Rx.Match(seg, "href=[\"'](/torrent/[^\"']+)[\"']");
            if (string.IsNullOrEmpty(link)) continue;
            string code = link.Split('/')[^1].Split('?')[0];

            string img = Rx.Match(seg, "<img[^>]+class=[\"'][^\"']*\\bimage\\b[^\"']*[\"'][^>]+(?:src|data-src)=[\"']([^\"']+)[\"']")
                ?? Rx.Match(seg, "<img[^>]+(?:src|data-src)=[\"']([^\"']+)[\"']");

            string name = Rx.Match(seg, "class=[\"'][^\"']*\\btitle\\b[^\"']*[\"'][^>]*>\\s*<a[^>]*>([^<]+)")
                ?? code;

            Add(code, img, name);
        }

        // Fallback: bắt trực tiếp <a href="/torrent/..."> có ảnh.
        if (list.Count == 0)
        {
            foreach (Match block in Regex.Matches(html, "<a[^>]+href=[\"'](/torrent/[^\"']+)[\"'][^>]*>(.*?)</a>", RX))
            {
                string inner = block.Groups[2].Value;
                string code = block.Groups[1].Value.Split('/')[^1].Split('?')[0];
                string img = Rx.Match(inner, "<img[^>]+(?:src|data-src)=[\"']([^\"']+)[\"']");
                string name = WebUtility.HtmlDecode(Regex.Replace(inner, "<[^>]+>", " "));
                Add(code, img, name);
            }
        }

        return list;
    }

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
                    ["source"] = seed > 0 ? $"Sukebei · seed {seed}" : "Sukebei",
                    ["title"] = title.Trim(),
                    ["hash"] = hm,
                    ["magnet"] = magnet
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

                list.Add(new JObject { ["source"] = "iJavTorrent", ["hash"] = hm, ["magnet"] = magnet });
                if (list.Count >= 5) break;
            }
        }
        catch { }
        return list;
    }
}
