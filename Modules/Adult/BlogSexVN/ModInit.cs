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

namespace BlogSexVN;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("BlogSexVN", conf, "blogsexvn")
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

    // Segment cua domain115.com (TikTok CDN) bi boc header PNG 238 byte truoc
    // MPEG-TS that. hls.js ky vong byte 0 la 0x47 nen khong cat thi player bao
    // "Found N TS packet/s that do not start with 0x47" -> man hinh den.
    // Lot toi sync-byte TS dau tien (0x47 cach nhau 188 byte) roi tra video/mp2t sach.
    // Tra false = da tu xu ly, true = de pipeline mac dinh chay.
    static async Task<bool> StripPngJunk(EventProxyApiOverride e)
    {
        try
        {
            string uri = e?.decryptLink?.uri;
            if (string.IsNullOrEmpty(uri)) return true;
            if (!uri.Contains("tiktokcdn.com", StringComparison.OrdinalIgnoreCase) &&
                !uri.Contains("domain115.com", StringComparison.OrdinalIgnoreCase))
                return true;

            byte[] data = await Http.Download(uri, referer: BsvTo.SiteHost + "/",
                timeoutSeconds: 20, useDefaultHeaders: false,
                headers: HeadersModel.Init(
                    ("User-Agent", BsvTo.ChromeUA),
                    ("Referer", BsvTo.SiteHost + "/")
                ));
            if (data == null || data.Length < 377)
                return true;

            // Da la TS thuan -> de pipeline mac dinh chay
            if (data[0] == 0x47 && data[188] == 0x47 && data[376] == 0x47)
                return true;

            int start = TsOffset(data);
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
        conf = ModuleInvoke.Init("BlogSexVN", new SisiSettings("BlogSexVN", BsvTo.SiteHost)
        {
            displayindex = 0,
            streamproxy = true,
            httpversion = 1,
            httptimeout = 25,
            rch_access = "apk",
            stream_access = "apk",
            headers_stream = HeadersModel.Init(
                ("User-Agent", BsvTo.ChromeUA),
                ("Referer", BsvTo.SiteHost + "/")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", BsvTo.ChromeUA),
                ("Referer", BsvTo.SiteHost + "/")
            ).ToDictionary()
        });
    }
}
