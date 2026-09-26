using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Tazzly;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("Tazzly", conf, "tazzly")
        };
    }

    public void Loaded(InitspaceModel baseconf)
    {
        modpath = baseconf.path;
        updateConf();
        EventListener.UpdateInitFile += updateConf;
        EventListener.ProxyApiOverride += StripPngJunk;
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
        EventListener.ProxyApiOverride -= StripPngJunk;
    }

    // Segment cua tazzly.com bi boc header PNG (~255 byte dau) o dau TS nen app
    // nhan dien sai. Chi can 376 byte dau de kiem tra; thay vi tai ca segment
    // (vai MB) moi lan, probe bang Range roi moi lot khi that su co junk.
    // Tra false = da tu xu ly, true = de pipeline mac dinh chay.
    static async Task<bool> StripPngJunk(EventProxyApiOverride e)
    {
        try
        {
            string uri = e?.decryptLink?.uri;
            if (string.IsNullOrEmpty(uri) || uri.IndexOf("ibyteimg.com", StringComparison.OrdinalIgnoreCase) < 0)
                return true;

            byte[] head = await Http.Download(
                uri,
                referer: TazzlyTo.EmbedHost + "/",
                timeoutSeconds: 20,
                MaxResponseContentBufferSize: 4096,
                headers: HeadersModel.Init(("Range", "bytes=0-376")),
                useDefaultHeaders: false);

            if (head == null || head.Length < 377)
                return true;

            // Da la TS thoi thi de pipeline mac dinh chay.
            if (head[0] == 0x47 && head[188] == 0x47 && head[376] == 0x47)
                return true;

            byte[] data = await Http.Download(uri, referer: TazzlyTo.EmbedHost + "/", timeoutSeconds: 20);
            if (data == null || data.Length < 376)
                return true;

            if (data[0] == 0x47 && data[188] == 0x47 && data[376] == 0x47)
                return true;

            int start = -1;
            int limit = System.Math.Min(8192, data.Length - 376);
            for (int i = 0; i < limit; i++)
            {
                if (data[i] == 0x47 && data[i + 188] == 0x47 && data[i + 376] == 0x47)
                {
                    start = i;
                    break;
                }
            }

            if (start <= 0)
                return true;

            e.httpContext.Response.ContentType = "video/mp2t";
            await e.httpContext.Response.Body.WriteAsync(data, start, data.Length - start);
            return false;
        }
        catch
        {
            return true;
        }
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("Tazzly", new SisiSettings("Tazzly", TazzlyTo.SiteHost)
        {
            displayindex = 17,
            streamproxy = true,
            httpversion = 2,
            httptimeout = 20,
            rch_access = "apk",
            stream_access = "apk",
            headers_stream = HeadersModel.Init(
                ("User-Agent", TazzlyTo.ChromeUA),
                ("Referer", TazzlyTo.EmbedHost + "/"),
                ("Origin", TazzlyTo.EmbedHost)
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", TazzlyTo.ChromeUA),
                ("Referer", TazzlyTo.SiteHost + "/")
            ).ToDictionary()
        });
    }
}
