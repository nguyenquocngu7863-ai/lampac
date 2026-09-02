using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Models.Base;
using Shared.Models.Module;
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

    // ============================ Plugin ============================
    [HttpGet]
    [Route("onejav.js")]
    [Route("onejav/js/{token}")]
    public ActionResult Plugin(string token = null)
    {
        string js = FileCache.ReadAllText($"{ModInit.modpath}/plugin.js", "onejav.js")
            .Replace("{localhost}", host)
            .Replace("{token}", token ?? "");
        return ContentTo(js, "application/javascript; charset=utf-8");
    }

    // ============================ List / search / tag ============================
    [HttpGet]
    [Route("onejav/list")]
    public async Task<ActionResult> List(string path = "", string q = "", int page = 1)
    {
        if (!ModInit.conf.enable) return StatusCode(403);
        if (page < 1) page = 1;

        string url;
        if (!string.IsNullOrWhiteSpace(q))
            url = $"{ModInit.conf.host}/search/{HttpUtility.UrlPathEncode(q.Trim())}" + (page > 1 ? $"?page={page}" : "");
        else if (!string.IsNullOrWhiteSpace(path))
            url = $"{ModInit.conf.host}/tag/{HttpUtility.UrlPathEncode(path.Trim())}" + (page > 1 ? $"?page={page}" : "");
        else
            url = $"{ModInit.conf.host}/" + (page > 1 ? $"?page={page}" : "");

        string html = await Http.Get(url, timeoutSeconds: 25, headers: BrowserHeaders, useDefaultHeaders: false);
        var items = ParseCards(html);
        return Json(new { results = items, hasMore = items.Count >= 20, page });
    }

    // ============================ Danh sách torrent của một mã ============================
    [HttpGet]
    [Route("onejav/torrents")]
    public async Task<ActionResult> Torrents(string id)
    {
        if (!ModInit.conf.enable) return StatusCode(403);
        if (string.IsNullOrWhiteSpace(id)) return Json(new { torrents = new List<object>() });

        string code = id.Trim();
        var torrents = new List<JObject>();
        var seenHash = new HashSet<string>();

        void Add(string source, string title, string link, string magnet, int seed, double gb)
        {
            string key = magnet ?? link;
            if (string.IsNullOrEmpty(key) || !seenHash.Add((magnet ?? link))) return;
            torrents.Add(new JObject
            {
                ["source"] = source,
                ["title"] = title,
                ["link"] = link,       // http(s) .torrent
                ["magnet"] = magnet,   // magnet
                ["seed"] = seed,
                ["gb"] = gb
            });
        }

        // 1) Trang onejav: link .torrent chính chủ
        string pageUrl = $"{ModInit.conf.host}/torrent/{code}";
        string html = await Http.Get(pageUrl, timeoutSeconds: 25, headers: BrowserHeaders, useDefaultHeaders: false);
        string poster = "", jtitle = code.ToUpperInvariant();

        if (!string.IsNullOrEmpty(html))
        {
            try
            {
                var doc = new HtmlDocument(); doc.LoadHtml(html);

                string og = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']")?.GetAttributeValue("content", "");
                if (!string.IsNullOrEmpty(og)) poster = Abs(og);

                string ogt = doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']")?.GetAttributeValue("content", "");
                if (!string.IsNullOrEmpty(ogt)) jtitle = WebUtility.HtmlDecode(ogt);

                foreach (var a in doc.DocumentNode.SelectNodes("//a[contains(@href,'/download/')]") ?? new HtmlNodeCollection(null))
                {
                    string href = a.GetAttributeValue("href", "");
                    if (string.IsNullOrEmpty(href) || !href.Contains(".torrent")) continue;
                    Add("OneJAV", WebUtility.HtmlDecode(a.InnerText?.Trim()).NullIfEmpty() ?? "Download .torrent", Abs(href), null, 0, 0);
                }
            }
            catch { }
        }

        // 2) Sukebei (có seed) + 3) ijav — dò theo các biến thể mã
        var extra = new List<JObject>();
        foreach (string sq in OneJavTo.SearchQueries(code))
        {
            if (ModInit.conf.use_sukebei) extra.AddRange(await SearchSukebei(sq));
            if (ModInit.conf.use_ijav) extra.AddRange(await SearchIjav(sq));
            if (extra.Any(e => e.Value<int?>("seed") > 0)) break;
        }

        foreach (var t in extra)
            Add((string)t["source"], (string)t["title"], (string)t["link"], (string)t["magnet"],
                t.Value<int?>("seed") ?? 0, t.Value<double?>("gb") ?? 0);

        // Xếp: seed cao trước, rồi tới OneJAV .torrent, còn lại sau.
        var sorted = torrents
            .OrderByDescending(t => t.Value<int?>("seed") ?? 0)
            .ThenBy(t => ((string)t["source"]).StartsWith("OneJAV") ? 0 : 1)
            .ToList();

        return Json(new { id = code, title = jtitle, poster, torrents = sorted });
    }

    // ============================ Phát: add vào TorrServer ngoài rồi trả stream URL ============================
    [HttpGet]
    [Route("onejav/play")]
    public async Task<ActionResult> Play(string link = null, string magnet = null)
    {
        if (!ModInit.conf.enable) return StatusCode(403);

        string ts = ModInit.TsHost();
        var tsHeaders = TsHeaders();

        string addLink = !string.IsNullOrWhiteSpace(magnet) ? magnet : link;
        if (string.IsNullOrWhiteSpace(addLink))
            return Json(new { ok = false, error = "Thiếu link torrent/magnet" });

        string payload = "{\"action\":\"add\",\"link\":\"" + addLink.Replace("\\", "\\\\").Replace("\"", "\\\"") +
                         "\",\"title\":\"\",\"poster\":\"\",\"save_to_db\":false}";

        string resp = await Http.Post($"{ts}/torrents", payload, timeoutSeconds: 40, headers: tsHeaders, useDefaultHeaders: false);
        string hash = Rx.Match(resp ?? "", "\"hash\"\\s*:\\s*\"([^\"]+)\"");
        if (string.IsNullOrEmpty(hash))
            return Json(new { ok = false, error = "TorrServer không nhận torrent (kiểm tra server/link)." });

        int index = 0;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            string stat = await Http.Get($"{ts}/stream?link={hash}&index=0&stat", timeoutSeconds: 20, headers: tsHeaders, useDefaultHeaders: false);
            int picked = OneJavTo.PickVideoIndex(stat);
            if (picked >= 0) { index = picked; break; }
            await Task.Delay(2500);
        }

        return Json(new { ok = true, url = $"{ts}/stream?link={HttpUtility.UrlEncode(hash)}&index={index}&play" });
    }

    // ============================ Parsing danh sách (HtmlAgilityPack) ============================
    List<JObject> ParseCards(string html)
    {
        var result = new List<JObject>();
        var seen = new HashSet<string>();
        if (string.IsNullOrEmpty(html)) return result;

        try
        {
            var doc = new HtmlDocument(); doc.LoadHtml(html);

            // Mọi link tới /torrent/<code> trên trang (bỏ link download).
            foreach (var a in doc.DocumentNode.SelectNodes("//a[contains(@href,'/torrent/')]") ?? new HtmlNodeCollection(null))
            {
                string href = a.GetAttributeValue("href", "");
                if (href.Contains("/download/")) continue;

                var m = Regex.Match(href, "/torrent/([^?#/]+)");
                if (!m.Success) continue;
                string code = m.Groups[1].Value;
                if (!seen.Add(code)) continue;

                var img = a.SelectSingleNode(".//img")
                          ?? a.ParentNode?.SelectSingleNode(".//img");
                string pic = img?.GetAttributeValue("src", "");
                if (string.IsNullOrWhiteSpace(pic)) pic = img?.GetAttributeValue("data-src", "");
                if (string.IsNullOrWhiteSpace(pic)) continue;

                string text = WebUtility.HtmlDecode(Regex.Replace(a.InnerText ?? "", "<[^>]+>", " "));
                text = Regex.Replace(text, "\\s+", " ").Trim();

                result.Add(new JObject
                {
                    ["id"] = code,
                    ["code"] = code.ToUpperInvariant(),
                    ["title"] = string.IsNullOrWhiteSpace(text) ? code.ToUpperInvariant() : text,
                    ["poster"] = Abs(pic),
                    ["img"] = Abs(pic)
                });
            }
        }
        catch { }

        return result;
    }

    // ============================ Sukebei ============================
    async Task<List<JObject>> SearchSukebei(string code)
    {
        var list = new List<JObject>();
        try
        {
            string url = "https://sukebei.nyaa.si/?f=0&c=0_0&q=" + HttpUtility.UrlEncode(code);
            string html = await Http.Get(url, timeoutSeconds: 20, useDefaultHeaders: false);
            if (string.IsNullOrEmpty(html) || html.Contains("No results found")) return list;

            var doc = new HtmlDocument(); doc.LoadHtml(html);
            foreach (var row in doc.DocumentNode.SelectNodes("//table//tr") ?? new HtmlNodeCollection(null))
            {
                var magA = row.SelectSingleNode(".//a[starts-with(@href,'magnet:')]");
                if (magA == null) continue;
                string magnet = magA.GetAttributeValue("href", "");
                string hm = Regex.Match(magnet, "btih:([a-zA-Z0-9]+)", RegexOptions.IgnoreCase) is Match h && h.Success
                    ? h.Groups[1].Value.ToLowerInvariant() : null;
                if (string.IsNullOrEmpty(hm)) continue;

                var titleA = row.SelectSingleNode(".//td[@colspan]//a") ?? row.SelectSingleNode(".//a[not(starts-with(@href,'magnet:'))]");
                string title = WebUtility.HtmlDecode(titleA?.GetAttributeValue("title", "").NullIfEmpty() ?? titleA?.InnerText?.Trim() ?? code);

                int seed = 0;
                var tds = row.SelectNodes(".//td") ?? new HtmlNodeCollection(null);
                foreach (var td in tds)
                {
                    var cls = td.GetAttributeValue("class", "");
                    if (cls.Contains("success") || cls.Contains("text-center"))
                    {
                        var mm = Regex.Match(td.InnerText ?? "", "([0-9,]+)");
                        if (mm.Success && int.TryParse(mm.Groups[1].Value.Replace(",", ""), out int v)) { seed = v; break; }
                    }
                }

                list.Add(new JObject
                {
                    ["source"] = seed > 0 ? $"Sukebei · seed {seed}" : "Sukebei",
                    ["title"] = title,
                    ["magnet"] = magnet,
                    ["link"] = (string)null,
                    ["seed"] = seed,
                    ["gb"] = 0.0
                });
                if (list.Count >= 8) break;
            }
        }
        catch { }
        return list;
    }

    // ============================ ijavtorrent ============================
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
                    ["magnet"] = magnet,
                    ["link"] = (string)null,
                    ["seed"] = 0,
                    ["gb"] = 0.0
                });
                if (list.Count >= 6) break;
            }
        }
        catch { }
        return list;
    }

    // ============================ Helpers ============================
    IReadOnlyList<HeadersModel> TsHeaders()
    {
        if (!string.IsNullOrWhiteSpace(ModInit.conf.ts_login) && !string.IsNullOrWhiteSpace(ModInit.conf.ts_passwd))
            return HeadersModel.Init("Authorization", "Basic " + Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes(ModInit.conf.ts_login + ":" + ModInit.conf.ts_passwd)));
        return null;
    }

    string Abs(string u)
    {
        if (string.IsNullOrEmpty(u)) return "";
        u = WebUtility.HtmlDecode(u);
        if (u.StartsWith("//")) return "https:" + u;
        if (u.StartsWith("http")) return u;
        string h = ModInit.conf.host.TrimEnd('/');
        return u.StartsWith('/') ? h + u : h + "/" + u;
    }
}

internal static class OneJavStr
{
    public static string NullIfEmpty(this string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
