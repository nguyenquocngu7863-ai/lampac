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

namespace SexViet100;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("SexViet100", conf, "sexviet100")
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

    // Segment của nguồn này bị bọc header PNG rác (~99-131 byte đầu) khiến
    // player phía app nhận diện sai kích thước hình. Lột tới sync-byte TS
    // đầu tiên (0x47 cách nhau 188 byte) rồi trả video/mp2t sạch.
    // Trả false = đã tự xử lý, true = để pipeline mặc định chạy.
    static async Task<bool> StripPngJunk(EventProxyApiOverride e)
    {
        try
        {
            string uri = e?.decryptLink?.uri;
            if (string.IsNullOrEmpty(uri) || !uri.Contains("yoru.35xcas.com"))
                return true;

            byte[] data = await Http.Download(uri, referer: "https://sexviet100.com/", timeoutSeconds: 20);
            if (data == null || data.Length < 376)
                return true;

            int start = -1;
            int limit = Math.Min(4096, data.Length - 376);
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
        conf = ModuleInvoke.Init("SexViet100", new SisiSettings("SexViet100", "https://sexviet100.com")
        {
            displayindex = 12,
            streamproxy = true,
            rch_access = "apk",
            stream_access = "apk",
            headers_stream = HeadersModel.Init(
                ("referer", "https://sexviet100.com/")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("referer", "https://sexviet100.com/")
            ).ToDictionary()
        });
    }
}
