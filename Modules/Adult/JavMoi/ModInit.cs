using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;

namespace JavMoi;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("JavMoi", conf, "javmoi")
        };
    }

    public void Loaded(InitspaceModel baseconf)
    {
        modpath = baseconf.path;
        updateConf();
        EventListener.ProxyApiOverride += StripPngJunk;
        EventListener.UpdateInitFile += updateConf;
    }

    public void Dispose()
    {
        EventListener.ProxyApiOverride -= StripPngJunk;
        EventListener.UpdateInitFile -= updateConf;
    }

    // Phim loai index.m3u8 bi boc header PNG 95 byte o dau TS nen app
    // phia khong nhan dien la MPEG-TS. Chi can 376 byte dau de kiem tra,
    // probe bang Range roi moi tai ca segment (vai MB) khi that su co junk.
    // Giong Tazzly - cung host ibyteimg.com.
    static async System.Threading.Tasks.Task<bool> StripPngJunk(
        EventProxyApiOverride e)
    {
        try
        {
            string uri = e?.decryptLink?.uri;
            if (string.IsNullOrEmpty(uri)
                || uri.IndexOf("ibyteimg.com",
                    StringComparison.OrdinalIgnoreCase) < 0)
                return true;

            // Do 4096 byte dau de kiem tra, retry 2 lan. Host nay
            // cung chap xac xao trong lan, khong retry se mat segment.
            byte[] head = null;
            for (int i = 0; i < 2; i++)
            {
                if (i > 0)
                    await System.Threading.Tasks.Task.Delay(300);

                head = await Http.Download(
                    uri,
                    referer: "https://z.javmoi.blog/",
                    timeoutSeconds: 8,
                    MaxResponseContentBufferSize: 4096,
                    headers: HeadersModel.Init(("Range", "bytes=0-376")),
                    useDefaultHeaders: false);

                if (head != null && head.Length >= 377)
                    break;
            }

            // Da la TS thoi -> de pipeline mac dinh chay.
            if (head != null && head.Length >= 377
                && head[0] == 0x47 && head[188] == 0x47
                && head[376] == 0x47)
                return true;

            // Probe het retry van rong -> de pipeline mac dinh tu lo,
            // tranh tai ca segment (vai MB) them mot lan that bai.
            if (head == null || head.Length < 377)
                return true;

            byte[] data = null;
            for (int i = 0; i < 2; i++)
            {
                if (i > 0)
                    await System.Threading.Tasks.Task.Delay(300);

                data = await Http.Download(
                    uri,
                    referer: "https://z.javmoi.blog/",
                    timeoutSeconds: 8);

                if (data != null && data.Length >= 376)
                    break;
            }

            if (data == null || data.Length < 376)
                return true;

            if (data[0] == 0x47 && data[188] == 0x47 && data[376] == 0x47)
                return true;

            int start = -1;
            int limit = System.Math.Min(8192, data.Length - 376);
            for (int i = 0; i < limit; i++)
            {
                if (data[i] == 0x47 && data[i + 188] == 0x47
                    && data[i + 376] == 0x47)
                {
                    start = i;
                    break;
                }
            }

            if (start <= 0)
                return true;

            e.httpContext.Response.ContentType = "video/mp2t";
            await e.httpContext.Response.Body.WriteAsync(
                data, start, data.Length - start);
            return false;
        }
        catch
        {
            return true;
        }
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("JavMoi",
            new SisiSettings("JavMoi", "https://z.javmoi.blog")
        {
            displayindex = 21,
            streamproxy = true,
            // httpversion = 1 BAT BUOC (giong PubJav): h2 cua .NET tra ve
            // rong (0 byte, khong exception) trong khi curl h2 van 200.
            // Do truc tiep xac nhan.
            httpversion = 1,
            // Site treo het bo dem roi drop TLS, khong cham that.
            // Timeout 20s la 3 lan thu cho mot lan that su, xem ghi chu
            // o FetchHtmlAsync.
            httptimeout = 8,
            rch_access = "apk",
            stream_access = "apk",
            headers_stream = HeadersModel.Init(
                ("User-Agent", JavMoiTo.ChromeUA),
                ("Referer", "https://z.javmoi.blog/")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", JavMoiTo.ChromeUA),
                ("Referer", "https://z.javmoi.blog/")
            ).ToDictionary()
        });
    }
}
