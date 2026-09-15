using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VietSexBlog;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("VietSexBlog", conf, "vietsexblog")
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

    // Segment cua nguon nay bi boc header PNG (~200 byte dau) khien player
    // phia app nhan dien sai. Lot toi sync-byte TS dau tien (0x47 cach nhau
    // 188 byte) roi tra video/mp2t sach. Giong SexViet100.
    // Tra false = da tu xu ly, true = de pipeline mac dinh chay.
    static async System.Threading.Tasks.Task<bool> StripPngJunk(EventProxyApiOverride e)
    {
        try
        {
            string uri = e?.decryptLink?.uri;
            if (string.IsNullOrEmpty(uri) || (!uri.Contains("tiktokcdn.com") && !uri.Contains("nidplay.blog")))
                return true;

            byte[] data = await Http.Download(uri, referer: "https://x.vietsex.blog/", timeoutSeconds: 20);
            if (data == null || data.Length < 376)
                return true;

            // Da sach thi thoi
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
        conf = ModuleInvoke.Init("VietSexBlog", new SisiSettings("VietSexBlog", "https://x.vietsex.blog")
        {
            displayindex = 10,
            streamproxy = true,
            rch_access = "apk",
            stream_access = "apk",
            headers_stream = HeadersModel.Init(
                ("referer", "https://x.vietsex.blog/")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("referer", "https://x.vietsex.blog/")
            ).ToDictionary()
        });
    }
}
