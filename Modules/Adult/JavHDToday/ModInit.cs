using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;

namespace JavHDToday;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("JavHDToday", conf, "javhdtoday")
        };
    }

    public void Loaded(InitspaceModel baseconf)
    {
        modpath = baseconf.path;
        updateConf();
        EventListener.UpdateInitFile += updateConf;
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("JavHDToday", new SisiSettings("JavHDToday", "https://javhd.today")
        {
            // 7 = ngay truoc JavGuru (8), dau nhom tu lam.
            // 20 cu trung PornHub nen doi.
            displayindex = 2,
            streamproxy = true,
            httpversion = 2,
            rch_access = "apk",
            stream_access = "apk",
            rchstreamproxy = "web,cors",
            // KHONG dat Referer o headers_stream: CDN Turbo
            // 429 khi thay Referer (giong JavGuru/JavTsunami).
            // DoodStream gan rieng Referer o StreamLink.
            headers_stream = HeadersModel.Init(
                ("User-Agent", JavHDTodayTo.ChromeUA)
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", JavHDTodayTo.ChromeUA),
                ("Referer", "https://javhd.today/")
            ).ToDictionary()
        });
    }
}
