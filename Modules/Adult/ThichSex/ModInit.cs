using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace ThichSex;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("ThichSex", conf, "thichsex")
        };
    }

    public void Loaded(InitspaceModel baseconf)
    {
        modpath = baseconf.path;
        updateConf();
        EventListener.ProxyApiOverride += ProxyOverride;
        EventListener.UpdateInitFile += updateConf;
    }

    public void Dispose()
    {
        EventListener.ProxyApiOverride -= ProxyOverride;
        EventListener.UpdateInitFile -= updateConf;
    }

    // CDN lh3-ggcontent.top chan TLS fingerprint .NET bang Cloudflare (403
    // "Attention Required!") trong khi curl_cffi chrome124 van 200 -> segment
    // phai tai qua Worker xn-relay (env THICHSEX_RELAY).
    // Segment la PNG 1x1 (89504e47) boc 265 byte truoc header TS 0x47 -> phai
    // cat prefix truoc khi tra cho player, neu khong hls.js khong nhan ra TS.
    static bool IsCdn(string uri)
        => !string.IsNullOrEmpty(uri)
           && (uri.IndexOf("lh3-ggcontent.top", StringComparison.OrdinalIgnoreCase) >= 0
            || uri.IndexOf("ap4r.com", StringComparison.OrdinalIgnoreCase) >= 0);

    static async Task<bool> ProxyOverride(EventProxyApiOverride e)
    {
        try
        {
            string uri = e?.decryptLink?.uri;
            if (!IsCdn(uri))
                return true;

            // m3u8 da duoc ProxyAPI.M3u8 rewrite sang /proxy/ truoc khi toi day,
            // o day chi con segment -> tai qua relay de thoat Cloudflare.
            byte[] data = await Http.Download(ThsTo.Relay(uri),
                referer: ThsTo.SiteHost + "/",
                timeoutSeconds: 20,
                useDefaultHeaders: false,
                headers: HeadersModel.Init(
                    ("User-Agent", ThsTo.ChromeUA),
                    ("Referer", ThsTo.PlayerHost + "/")
                ));

            if (data == null || data.Length == 0)
            {
                e.httpContext.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                e.httpContext.Response.ContentType = "text/plain; charset=utf-8";
                await e.httpContext.Response.WriteAsync("thichsex: segment fetch failed");
                return false;
            }

            int start = TsOffset(data);
            e.httpContext.Response.ContentType = "video/mp2t";
            await e.httpContext.Response.Body.WriteAsync(data, start, data.Length - start);
            return false;
        }
        catch
        {
            return true;
        }
    }

    // Header TS (0x47) lap lai moi 188 byte; 0 = da la TS thuan.
    static int TsOffset(byte[] data)
    {
        if (data == null || data.Length < 377)
            return 0;
        if (data[0] == 0x47 && data[188] == 0x47 && data[376] == 0x47)
            return 0;

        int limit = Math.Min(8192, data.Length - 376);
        for (int i = 1; i < limit; i++)
        {
            if (data[i] == 0x47 && data[i + 188] == 0x47 && data[i + 376] == 0x47)
                return i;
        }
        return 0;
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("ThichSex", new SisiSettings("ThichSex", ThsTo.SiteHost)
        {
            displayindex = 0,
            streamproxy = true,
            httpversion = 1,
            httptimeout = 20,
            rch_access = "apk",
            stream_access = "apk",
            headers_stream = HeadersModel.Init(
                ("User-Agent", ThsTo.ChromeUA),
                ("Referer", ThsTo.SiteHost + "/")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", ThsTo.ChromeUA),
                ("Referer", ThsTo.SiteHost + "/")
            ).ToDictionary()
        });
    }
}
