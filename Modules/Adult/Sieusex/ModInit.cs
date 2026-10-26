using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;

namespace Sieusex;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("Sieusex", conf, "sieusex")
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
        // sieusex.net = WordPress theme video3x, khong CF challenge, fetch direct OK.
        // Stream: hidecdn.net (embed) -> playlist XOR 0x5A -> segment TikTok CDN
        // (khong can Referer). hidecdn hay ngat SSL lan dau -> co retry o fetch.
        conf = ModuleInvoke.Init("Sieusex", new SisiSettings("Sieusex", SieusexTo.SiteHost)
        {
            displayindex = 19,
            streamproxy = true,
            httpversion = 1,
            httptimeout = 25,
            rch_access = "apk",
            stream_access = "apk",
            headers_stream = HeadersModel.Init(
                ("User-Agent", SieusexTo.ChromeUA),
                ("Referer", SieusexTo.SiteHost + "/")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", SieusexTo.ChromeUA),
                ("Referer", SieusexTo.SiteHost + "/")
            ).ToDictionary()
        });
    }
}
