using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MissAV;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("MissAV", conf, "missav")
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

    // surrit.com chan TLS fingerprint cua .NET (curl/Chromium 200)
    // -> ep proxy dung h2 + full Chrome UA + referer missav
    static Task ForceHttp2(EventProxyApiCreateHttpRequest e)
    {
        try
        {
            string h = e?.uri?.Host ?? "";
            if (h.Contains("surrit") && e.requestMessage != null)
                e.requestMessage.Version = HttpVersion.Version20;
        }
        catch { }

        return Task.CompletedTask;
    }

    static readonly Regex UrlRx = new("(https?://[^\\s\"']+)", RegexOptions.Compiled);

    // .NET stall/403 tren surrit -> override: tai bang curl process (200),
    // m3u8 thi rewrite moi URL thanh /proxy/, segment thi passthrough bytes.
    // Tra false = da tu xu ly, true = de pipeline mac dinh xu ly.
    static async Task<bool> ProxyOverride(EventProxyApiOverride e)
    {
        try
        {
            string uri = e?.decryptLink?.uri;
            if (string.IsNullOrEmpty(uri) || !uri.Contains("surrit.com"))
                return true;

            string host = CoreInit.Host(e.httpContext);

            if (uri.Contains(".m3u8"))
            {
                string m3u = await MissAVTo.CurlGetRetry(uri, "https://missav.live/");
                if (string.IsNullOrWhiteSpace(m3u) || !m3u.Contains("#EXTM3U"))
                {
                    // Het ca retries: KHONG cho fallthrough ve pipeline mac dinh
                    // (HttpClient cua Lampac luon bi surrit.com Cloudflare chan 403).
                    // Tra loi loi ro rang de player bao loi va cho client thu lai.
                    e.httpContext.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                    e.httpContext.Response.ContentType = "text/plain; charset=utf-8";
                    await e.httpContext.Response.WriteAsync("missav: master playlist fetch failed");
                    return false;
                }

                // surrit master/variant dung path relative -> resolve ve absolute truoc
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

                e.httpContext.Response.ContentType = "application/vnd.apple.mpegurl";
                await e.httpContext.Response.WriteAsync(sb.ToString(), Encoding.UTF8);
                return false;
            }
            else
            {
                byte[] data = await MissAVTo.CurlGetBytes(uri, "https://missav.live/");
                if (data == null || data.Length == 0)
                {
                    e.httpContext.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                    e.httpContext.Response.ContentType = "text/plain; charset=utf-8";
                    await e.httpContext.Response.WriteAsync("missav: segment fetch failed");
                    return false;
                }

                e.httpContext.Response.ContentType =
                    uri.Contains(".jpeg") || uri.Contains(".jpg") ? "image/jpeg" :
                    uri.Contains(".png") ? "image/png" :
                    uri.Contains(".mp4") ? "video/mp4" :
                    uri.Contains(".key") ? "application/octet-stream" :
                    "video/mp2t";
                await e.httpContext.Response.Body.WriteAsync(data);
                return false;
            }
        }
        catch
        {
            return true;
        }
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("MissAV", new SisiSettings("MissAV", "https://missav.live")
        {
            displayindex = 18,
            streamproxy = true,
            httpversion = 2,
            rch_access = "apk",
            stream_access = "apk",
            headers_stream = HeadersModel.Init(
                ("User-Agent", MissAVTo.ChromeUA),
                ("Referer", "https://missav.live/"),
                ("Origin", "https://missav.live")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", MissAVTo.ChromeUA),
                ("Referer", "https://missav.live/")
            ).ToDictionary()
        });
    }
}
