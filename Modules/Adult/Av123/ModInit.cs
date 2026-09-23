using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Av123;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("Av123", conf, "123av")
        };
    }

    public void Loaded(InitspaceModel baseconf)
    {
        modpath = baseconf.path;
        updateConf();
        EventListener.UpdateInitFile += updateConf;
        EventListener.ProxyApiCreateHttpRequest += ForceHttp2;
        EventListener.ProxyApiOverride += ProxyOverride;
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
        EventListener.ProxyApiCreateHttpRequest -= ForceHttp2;
        EventListener.ProxyApiOverride -= ProxyOverride;
    }

    static readonly ConcurrentDictionary<string, (DateTime ts, string body)> _m3u8cache = new();
    static readonly System.Threading.SemaphoreSlim _curlGate = new System.Threading.SemaphoreSlim(32);

    static bool TryGetCached(string uri, out string body)
    {
        body = null;
        try
        {
            if (_m3u8cache.TryGetValue(uri, out var e) && DateTime.UtcNow - e.ts < TimeSpan.FromMinutes(5))
            {
                body = e.body;
                return true;
            }
        }
        catch { }
        return false;
    }

    static void SetCached(string uri, string body)
    {
        try
        {
            if (_m3u8cache.Count > 10)
                _m3u8cache.Clear();
            _m3u8cache[uri] = (DateTime.UtcNow, body);
        }
        catch { }
    }

    static bool IsOurs(EventProxyApiOverride e)
    {
        try
        {
            string plugin = e?.decryptLink?.plugin;
            if (!string.IsNullOrEmpty(plugin))
                return plugin.Equals("Av123", StringComparison.OrdinalIgnoreCase);
            // fallback: nhan dien theo referer da dong dau khi tao link
            var hs = e?.decryptLink?.headers;
            if (hs != null)
            {
                foreach (var h in hs)
                {
                    if (h != null && h.name != null && h.name.Equals("Referer", StringComparison.OrdinalIgnoreCase)
                        && h.val != null && (h.val.Contains("javplayer.cc") || h.val.Contains("123av.com")))
                        return true;
                }
            }
        }
        catch { }
        return false;
    }

    static Task ForceHttp2(EventProxyApiCreateHttpRequest e)
    {
        try
        {
            string h = e?.uri?.Host ?? "";
            if (h.Contains("hot-desert") && e.requestMessage != null)
                e.requestMessage.Version = HttpVersion.Version20;
        }
        catch { }

        return Task.CompletedTask;
    }

    // CDN stream (hot-desert): master/variant dung path relative,
    // doi referer (khong referer -> 403). Tai bang curl + rewrite ve /proxy/.
    // CHI xu ly link cua Av123 (check plugin), link module khac tra true.
    static async Task<bool> ProxyOverride(EventProxyApiOverride e)
    {
        try
        {
            string uri = e?.decryptLink?.uri;
            if (string.IsNullOrEmpty(uri) || !uri.Contains("hot-desert"))
                return true;
            if (!IsOurs(e))
                return true;

            string host = CoreInit.Host(e.httpContext);

            if (uri.Contains(".m3u8"))
            {
                if (TryGetCached(uri, out string cached))
                {
                    e.httpContext.Response.ContentType = "application/vnd.apple.mpegurl";
                    await e.httpContext.Response.WriteAsync(cached, Encoding.UTF8);
                    return false;
                }

                var sw = System.Diagnostics.Stopwatch.StartNew();
                string m3u = await Av123To.CurlGet(uri, "https://javplayer.cc/");
                sw.Stop();
                if (sw.Elapsed.TotalSeconds > 3 || string.IsNullOrWhiteSpace(m3u) || !m3u.Contains("#EXTM3U"))
                {
                    try { System.IO.File.AppendAllText("/tmp/av123_slow.log", $"{DateTime.UtcNow:HH:mm:ss} m3u8 {sw.Elapsed.TotalSeconds:F1}s len={(m3u ?? "").Length} {uri[..100]}\n"); } catch { }
                }
                if (string.IsNullOrWhiteSpace(m3u) || !m3u.Contains("#EXTM3U"))
                {
                    // fail-fast 502 thay vi fallback pipeline mac dinh (treo lau)
                    try
                    {
                        e.httpContext.Response.StatusCode = 502;
                        await e.httpContext.Response.WriteAsync("upstream fetch failed", Encoding.UTF8);
                    }
                    catch { }
                    return false;
                }

                var baseUri = new Uri(uri);
                var sb = new StringBuilder();
                foreach (string raw in m3u.Split('\n'))
                {
                    string line = raw.TrimEnd('\r');
                    string t = line.Trim();
                    if (string.IsNullOrEmpty(t) || t.StartsWith("#"))
                    {
                        sb.AppendLine(line);
                        continue;
                    }
                    string abs = t;
                    if (Uri.TryCreate(baseUri, t, out var a))
                        abs = a.ToString();
                    sb.AppendLine(ProxyLink.Encrypt(abs.AsSpan(), e.decryptLink, prefix: [host, "/proxy/"]));
                }

                string body = sb.ToString();
                SetCached(uri, body);
                e.httpContext.Response.ContentType = "application/vnd.apple.mpegurl";
                await e.httpContext.Response.WriteAsync(body, Encoding.UTF8);
                return false;
            }
            else
            {
                // ho tro Range (seek): forward Range cua player xuong upstream
                // bang curl -r, tra dung 206 + Content-Range. Khong Range -> 200 full.
                // Gioi han curl song song de retry-storm khong lam chet may.
                string range = null;
                try { range = e.httpContext.Request.Headers["Range"].ToString(); } catch { }
                await _curlGate.WaitAsync();
                var sw2 = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var (data, status, contentRange) = await Av123To.CurlGetRangeAsync(uri, "https://javplayer.cc/", range);
                    sw2.Stop();
                    if (sw2.Elapsed.TotalSeconds > 3 || status == 0 || data == null || data.Length == 0)
                    {
                        try { System.IO.File.AppendAllText("/tmp/av123_slow.log", $"{DateTime.UtcNow:HH:mm:ss} seg {sw2.Elapsed.TotalSeconds:F1}s st={status} len={(data ?? System.Array.Empty<byte>()).Length} range={range ?? "-"} {uri[..100]}\n"); } catch { }
                    }
                    if (data == null || data.Length == 0)
                    {
                        // fail-fast 502 thay vi fallback pipeline mac dinh (treo lau)
                        try
                        {
                            e.httpContext.Response.StatusCode = 502;
                            await e.httpContext.Response.WriteAsync("upstream fetch failed", Encoding.UTF8);
                        }
                        catch { }
                        return false;
                    }

                    var resp = e.httpContext.Response;
                    resp.ContentType = "video/mp2t";
                    resp.Headers["Accept-Ranges"] = "bytes";
                    if (status == 206)
                    {
                        resp.StatusCode = 206;
                        if (!string.IsNullOrEmpty(contentRange))
                            resp.Headers["Content-Range"] = contentRange;
                    }
                    else if (status == 416)
                    {
                        resp.StatusCode = 416;
                        return false;
                    }
                    resp.ContentLength = data.Length;
                    await resp.Body.WriteAsync(data);
                    return false;
                }
                finally
                {
                    _curlGate.Release();
                }
            }
        }
        catch
        {
            return true;
        }
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("Av123", new SisiSettings("Av123", "https://123av.com")
        {
            displayindex = 20,
            streamproxy = true,
            httpversion = 2,
            rch_access = "apk",
            stream_access = "apk",
            // stream CDN doi referer javplayer (khong referer -> 403)
            headers_stream = HeadersModel.Init(
                ("User-Agent", Av123To.ChromeUA),
                ("Referer", "https://javplayer.cc/"),
                ("Origin", "https://javplayer.cc")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", Av123To.ChromeUA),
                ("Referer", "https://123av.com/")
            ).ToDictionary()
        });
    }
}
