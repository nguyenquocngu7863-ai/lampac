using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using Shared;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace XszAv;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;
    static readonly HttpClient _http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.None })
    {
        DefaultRequestVersion = HttpVersion.Version20,
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
        Timeout = TimeSpan.FromSeconds(30)
    };

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("XszAv", conf, "xszav")
        };
    }

    public void Loaded(InitspaceModel baseconf)
    {
        modpath = baseconf.path;
        updateConf();
        EventListener.UpdateInitFile += updateConf;
        EventListener.ProxyApiOverride += ProxyOverride;
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
        EventListener.ProxyApiOverride -= ProxyOverride;
    }

    static async Task<bool> ProxyOverride(EventProxyApiOverride e)
    {
        string uri = e?.decryptLink?.uri;
        if (string.IsNullOrEmpty(uri) ||
            !(uri.Contains("xsz-av.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Contains("xsz2-cdn.com", StringComparison.OrdinalIgnoreCase)))
            return true;

        try
        {
            string host = CoreInit.Host(e.httpContext);
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            req.Version = HttpVersion.Version20;
            req.VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
            req.Headers.TryAddWithoutValidation("User-Agent", XszAvTo.ChromeUA);
            req.Headers.TryAddWithoutValidation("Referer", XszAvTo.SiteHost + "/");
            req.Headers.TryAddWithoutValidation("Origin", XszAvTo.SiteHost);
            req.Headers.TryAddWithoutValidation("Accept", "*/*");

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, e.httpContext.RequestAborted);
            if (!resp.IsSuccessStatusCode)
            {
                e.httpContext.Response.StatusCode = (int)resp.StatusCode;
                return false;
            }

            string ct = resp.Content.Headers.ContentType?.ToString() ?? "";
            bool isM3u8 = uri.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) || ct.Contains("mpegurl", StringComparison.OrdinalIgnoreCase);

            if (isM3u8)
            {
                string body = await resp.Content.ReadAsStringAsync(e.httpContext.RequestAborted);
                var sb = new StringBuilder();
                foreach (string raw in body.Split('\n'))
                {
                    string t = raw.Trim();
                    if (string.IsNullOrEmpty(t) || t.StartsWith("#"))
                    {
                        sb.AppendLine(raw.TrimEnd('\r'));
                        continue;
                    }
                    string abs = t;
                    if (!abs.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        if (Uri.TryCreate(new Uri(uri), t, out var a)) abs = a.AbsoluteUri;
                    }
                    string enc = ProxyLink.Encrypt(abs, e.decryptLink, prefix: new[] { host, "/proxy/" });
                    sb.AppendLine(string.IsNullOrEmpty(enc) ? abs : enc);
                }
                e.httpContext.Response.ContentType = "application/vnd.apple.mpegurl";
                await e.httpContext.Response.WriteAsync(sb.ToString(), Encoding.UTF8);
                return false;
            }
            else
            {
                e.httpContext.Response.ContentType = string.IsNullOrEmpty(ct) ? "application/octet-stream" : ct;
                await resp.Content.CopyToAsync(e.httpContext.Response.Body, e.httpContext.RequestAborted);
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
        conf = ModuleInvoke.Init("XszAv", new SisiSettings("XszAv", XszAvTo.SiteHost)
        {
            displayindex = 34,
            streamproxy = true,
            httpversion = 2,
            httptimeout = 25,
            rch_access = "apk",
            stream_access = "apk",
            headers_stream = HeadersModel.Init(
                ("User-Agent", XszAvTo.ChromeUA),
                ("Referer", XszAvTo.SiteHost + "/"),
                ("Origin", XszAvTo.SiteHost)
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", XszAvTo.ChromeUA),
                ("Referer", XszAvTo.SiteHost + "/"),
                ("Origin", XszAvTo.SiteHost)
            ).ToDictionary()
        });
    }
}