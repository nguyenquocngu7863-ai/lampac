using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Av01;

public class Av01Controller : BaseSisiController
{
    public Av01Controller() : base(ModInit.conf) { }

    static readonly SemaphoreSlim menuLock = new(1, 1);

    [HttpGet, Staticache(manually: true)]
    [Route("av01")]
    async public Task<ActionResult> Index(string c, int pg = 1, string sort = null, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

        if (pg < 1) pg = 1;

        sort = Av01To.ClampSort(sort);

        string url = ListUrl(c, sort, pg);
        if (url == null)
            return OnError("playlists", refresh_proxy: true);

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"av01:list:{c}:{sort}:{pg}"), 10, async e =>
        {
            string json = await GetJsonAsync(url);
            var playlists = Av01To.Playlist(json, "av01/vidosik", host);

            if (playlists.Count == 0)
                return pg > 1 ? e.Success((new List<PlaylistItem>(), 0))
                              : e.Fail("playlists", refresh_proxy: true);

            return e.Success((playlists, Av01To.Pages(json)));
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(
            cache.Value.playlists,
            cache.ISingleCache,
            await MenuAsync(c, sort),
            total_pages: cache.Value.total_pages
        );
    }

    static string ListUrl(string c, string sort, int pg)
    {
        if (string.IsNullOrWhiteSpace(c))
            return Av01To.ListUrl(sort, pg);

        string val = c.Trim();
        int sep = val.IndexOf(':');
        if (sep <= 0) return null;

        string kind = val.Substring(0, sep);
        string id = val.Substring(sep + 1);
        if (string.IsNullOrEmpty(id) || !id.All(char.IsDigit))
            return null;

        return kind switch
        {
            "tag" => Av01To.TagUrl(id, pg),
            "actress" => Av01To.ActressUrl(id, pg),
            "maker" => Av01To.MakerUrl(id, pg),
            _ => null
        };
    }

    // ---- HTTP ----

    async Task<string> GetJsonAsync(string url, string data = null)
    {
        try
        {
            string res = null;
            var headers = HeadersModel.Init(
                ("User-Agent", Av01To.ChromeUA),
                ("Referer", Av01To.SiteHost + "/"),
                ("Accept", "application/json, text/plain, */*")
            );

            if (data == null)
            {
                await httpHydra.GetSpan(url, span => { res = span.ToString(); }, addheaders: headers);
            }
            else
            {
                await httpHydra.PostSpan(url, data, span => { res = span.ToString(); }, addheaders: headers);
            }

            return res;
        }
        catch
        {
            return null;
        }
    }

    // ---- menu ----

    async Task<List<MenuItem>> MenuAsync(string c, string sort)
    {
        string key = ipkey("av01:menu:v1");

        if (hybridCache.TryGetValue(key,
            out (List<(string id, string name)> tags,
                 List<(string id, string name)> actresses,
                 List<(string id, string name)> makers) hit))
        {
            return Av01To.Menu(host, sort, c, hit.tags, hit.actresses, hit.makers);
        }

        if (!await menuLock.WaitAsync(8000))
            return Av01To.Menu(host, sort, c);

        try
        {
            if (hybridCache.TryGetValue(key, out (List<(string id, string name)> tags, List<(string id, string name)> actresses, List<(string id, string name)> makers) hit2))
                return Av01To.Menu(host, sort, c, hit2.tags, hit2.actresses, hit2.makers);

            var tagsTask = GetJsonAsync(Av01To.CatalogUrl("tags"));
            var actTask = GetJsonAsync(Av01To.CatalogUrl("actresses"));
            var mkTask = GetJsonAsync(Av01To.CatalogUrl("makers"));

            var all = Task.WhenAll(tagsTask, actTask, mkTask);
            if (all != await Task.WhenAny(all, Task.Delay(12000)))
                return Av01To.Menu(host, sort, c);

            var tags = Av01To.Catalog(tagsTask.Result, "tags")
                .Select(x => (x.id, x.name)).ToList();
            var actresses = Av01To.Catalog(actTask.Result, "actresses")
                .Select(x => (x.id, x.name)).ToList();
            var makers = Av01To.Catalog(mkTask.Result, "makers")
                .Select(x => (x.id, x.name)).ToList();

            if (tags.Count > 0 || actresses.Count > 0 || makers.Count > 0)
                hybridCache.Set(key, (tags, actresses, makers), cacheTime(720));

            return Av01To.Menu(host, sort, c, tags, actresses, makers);
        }
        catch
        {
            return Av01To.Menu(host, sort, c);
        }
        finally
        {
            menuLock.Release();
        }
    }

    // ---- popup (genres) ----

    [HttpGet, Staticache(manually: true)]
    [Route("av01/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        string id = Av01To.VideoId(uri);
        if (string.IsNullOrEmpty(id))
            return Json(new { items = new object[0], groups = new object[0] });

        string cacheKey = ipkey($"av01:info:{id}");
        if (hybridCache.TryGetValue(cacheKey, out List<(string kind, string id, string name)> chips)
            && chips != null)
        {
            return InfoResult(chips, uri);
        }

        string json = await GetJsonAsync(Av01To.DetailApiUrl(id));
        var list = Av01To.Chips(json);

        if (list.Count > 0)
            hybridCache.Set(cacheKey, list, cacheTime(720));

        return InfoResult(list, uri);
    }

    static string KindLabel(string kind) => kind switch
    {
        "actress" => "Diễn viên",
        "tag" => "Thể loại",
        "maker" => "Nhà sản xuất",
        _ => "Khác"
    };

    static int KindRank(string kind) => kind switch
    {
        "actress" => 0,
        "tag" => 1,
        "maker" => 2,
        _ => 99
    };

    ActionResult InfoResult(List<(string kind, string id, string name)> chips, string uri)
    {
        var groups = new List<object>();

        foreach (string kind in chips.Select(x => x.kind).Distinct().OrderBy(KindRank))
        {
            var items = chips.Where(x => x.kind == kind).Select(x => new
            {
                title = x.name,
                url = $"{host}/av01?c={HttpUtility.UrlEncode(kind + ":" + x.id)}"
            }).ToList();

            if (items.Count > 0)
                groups.Add(new { title = KindLabel(kind), items });
        }

        groups.Add(new
        {
            title = "Liên quan",
            items = new[]
            {
                new
                {
                    title = "Video liên quan",
                    plain = true,
                    url = $"{host}/av01?rel={HttpUtility.UrlEncode(uri)}"
                }
            }
        });

        return Json(new { items = new object[0], groups });
    }

    async Task<ActionResult> RelatedList(string rel, int pg)
    {
        string id = Av01To.VideoId(rel);
        if (string.IsNullOrEmpty(id))
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        if (pg < 1) pg = 1;

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"av01:rel:{id}:{pg}"), 20, async e =>
        {
            string json = await GetJsonAsync(Av01To.SimilarsUrl(id, pg));
            var pl = Av01To.Playlist(json, "av01/vidosik", host);
            return e.Success((pl, Av01To.Pages(json)));
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            await MenuAsync(null, "latest"), total_pages: cache.Value.total_pages);
    }

    // ---- vidosik ----

    [HttpGet, Staticache(manually: true)]
    [Route("av01/vidosik")]
    async public Task<ActionResult> Vidosik(string uri, bool related)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        string id = Av01To.VideoId(uri);
        if (string.IsNullOrEmpty(id))
            return OnError("stream_links", refresh_proxy: true);

        var variants = await VariantsAsync(id);
        if (variants == null || variants.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        var recomends = await RecomendsAsync(id, uri);
        if (related)
            return PlaylistResult(recomends, false, null, total_pages: 1);

        string enc = HttpUtility.UrlEncode(uri);

        return Json(new
        {
            qualitys = variants.ToDictionary(k => k.Key, v =>
                $"{host}/av01/video.m3u8?uri={enc}&q={HttpUtility.UrlEncode(v.Key)}"),
            recomends
        });
    }

    async Task<List<PlaylistItem>> RecomendsAsync(string id, string uri)
    {
        string memKey = ipkey($"av01:rec:{id}");
        if (hybridCache.TryGetValue(memKey, out List<PlaylistItem> hit) && hit != null)
            return hit;

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri : Av01To.DetailUrl(id, null);

        string json = await GetJsonAsync(Av01To.SimilarsUrl(id, 1));
        var rec = Av01To.Playlist(json, "av01/vidosik", host);

        if (rec.Count > 0)
        {
            foreach (var p in rec)
                if (!string.IsNullOrEmpty(p.video) && !p.video.StartsWith("http"))
                    p.video = host + "/" + p.video.TrimStart('/');

            hybridCache.Set(memKey, rec, cacheTime(20));
        }

        return rec;
    }

    // ---- poster ----

    // Host anh tinh (img1.iw01.xyz) tra 403; anh that nam tren R2 va phai co
    // token_v2/expires/ip tu geo.js. Route nay lay geo roi 302 sang URL con han.
    [HttpGet]
    [Route("av01/poster")]
    async public Task<ActionResult> PosterRoute(string uri)
    {
        string id = Av01To.VideoId(uri);
        if (string.IsNullOrEmpty(id))
            return NotFound();

        var geo = await GeoAsync();
        if (geo == null)
            return NotFound();

        Response.Headers["Cache-Control"] = "no-cache";
        return Redirect($"https://files.iw01.xyz/covers/{id}/800.webp"
            + $"?token_v2={HttpUtility.UrlEncode(geo.Value.token)}"
            + $"&expires={HttpUtility.UrlEncode(geo.Value.expires)}"
            + $"&ip={HttpUtility.UrlEncode(geo.Value.ip)}");
    }

    // ---- stream ----

    [HttpGet, Staticache(manually: true)]
    [Route("av01/video")]
    [Route("av01/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        string id = Av01To.VideoId(uri);
        if (string.IsNullOrEmpty(id))
            return OnError("stream_links", refresh_proxy: true);

        var variants = await VariantsAsync(id);
        if (variants == null || variants.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Content-Type"] = "application/vnd.apple.mpegurl";

        // Khong chi dinh chat luong -> tra master da rewrite ve route nay.
        if (string.IsNullOrWhiteSpace(q) || q.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            var sb = new StringBuilder();
            sb.Append("#EXTM3U\n#EXT-X-VERSION:3\n");
            foreach (var kv in variants)
            {
                var v = kv.Value;
                sb.Append("#EXT-X-STREAM-INF:PROGRAM-ID=1,BANDWIDTH=1000000,RESOLUTION=")
                  .Append(v.res).Append("\n");
                sb.Append($"{host}/av01/video.m3u8?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(kv.Key)}\n");
            }
            return Content(sb.ToString(), "application/vnd.apple.mpegurl");
        }

        if (!variants.TryGetValue(q, out var chosen))
            chosen = variants.First().Value;

        // Tra manifest qua /proxy cua Lampac: ProxyM3u8 se rewrite ca segment lan
        // EXT-X-MAP ve cung origin, nen player web/TV khong bi CORS chan.
        string manifest = Av01To.ManifestUrl(id, chosen.file, chosen.token);
        string link = HostStreamProxy(manifest + "#.m3u8",
            HeadersModel.Init(("User-Agent", Av01To.ChromeUA), ("Referer", Av01To.SiteHost + "/")),
            force_streamproxy: true);

        Response.Headers["Cache-Control"] = "no-cache";
        return Redirect(link);
    }

    async Task<string> GetTextAsync(string url)
    {
        try
        {
            string res = null;
            await httpHydra.GetSpan(url, span => { res = span.ToString(); },
                addheaders: HeadersModel.Init(
                    ("User-Agent", Av01To.ChromeUA),
                    ("Referer", Av01To.SiteHost + "/")
                ));
            return res;
        }
        catch
        {
            return null;
        }
    }

    public sealed class Variant
    {
        public string file;
        public string token;
        public string res;
    }

    // Geo token (30 phut) -> playlist API (master da ky san access_token theo tung variant)

    // token_v2/expires/ip tu geo.js (song ~30 phut, cache 20 phut).
    async Task<(string token, string expires, string ip)?> GeoAsync()
    {
        string geoKey = ipkey("av01:geo");

        if (!hybridCache.TryGetValue(geoKey, out string geoJson) || string.IsNullOrEmpty(geoJson))
        {
            geoJson = await GetTextAsync(Av01To.GeoUrl);
            if (string.IsNullOrEmpty(geoJson))
                return null;
            hybridCache.Set(geoKey, geoJson, cacheTime(20));
        }

        try
        {
            using var doc = JsonDocument.Parse(geoJson);
            var r = doc.RootElement;
            string token = r.TryGetProperty("token_v2", out var t) ? t.GetString() : null;
            string expires = r.TryGetProperty("expires", out var e) ? e.ToString() : null;
            string ip = r.TryGetProperty("ip", out var i) ? i.GetString() : null;

            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(expires) || string.IsNullOrEmpty(ip))
                return null;

            return (token, expires, ip);
        }
        catch
        {
            return null;
        }
    }

    async Task<Dictionary<string, Variant>> VariantsAsync(string id)
    {
        string memKey = ipkey($"av01:pl:{id}");
        if (hybridCache.TryGetValue(memKey, out Dictionary<string, Variant> hit) && hit != null && hit.Count > 0)
            return hit;

        var geo = await GeoAsync();
        if (geo == null)
            return null;

        string plJson = await GetTextAsync(Av01To.PlaylistUrl(id, geo.Value.expires, geo.Value.ip, geo.Value.token));
        if (string.IsNullOrEmpty(plJson))
            return null;

        string src = null;
        try
        {
            using var doc = JsonDocument.Parse(plJson);
            if (doc.RootElement.TryGetProperty("src", out var s))
                src = s.GetString();
        }
        catch { }

        if (string.IsNullOrEmpty(src) || !src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return null;

        int comma = src.IndexOf(',');
        if (comma < 0)
            return null;

        string master;
        try
        {
            master = Encoding.UTF8.GetString(Convert.FromBase64String(src.Substring(comma + 1)));
        }
        catch
        {
            return null;
        }

        var result = ParseMaster(master);
        if (result.Count > 0)
            hybridCache.Set(memKey, result, cacheTime(20));

        return result;
    }

    static Dictionary<string, Variant> ParseMaster(string master)
    {
        var res = new Dictionary<string, Variant>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(master))
            return res;

        string pending = null;

        foreach (string raw in master.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("#EXT-X-STREAM-INF", StringComparison.OrdinalIgnoreCase))
            {
                var m = System.Text.RegularExpressions.Regex.Match(line, @"RESOLUTION=(\d+)x(\d+)");
                pending = m.Success ? m.Groups[2].Value + "p" : "Auto";
                continue;
            }

            if (line.StartsWith("#") || pending == null)
                continue;

            string token = null;
            try
            {
                var q = HttpUtility.ParseQueryString(new Uri(line).Query);
                token = q["access_token"];
            }
            catch { }

            string file = line;
            int qs = file.IndexOf('?');
            if (qs > 0) file = file.Substring(0, qs);
            file = file.Substring(file.LastIndexOf('/') + 1);

            string label = pending;
            int n = 2;
            while (res.ContainsKey(label))
                label = pending + "_" + n++;

            res[label] = new Variant { file = file, token = token, res = pending };
            pending = null;
        }

        return res;
    }
}
