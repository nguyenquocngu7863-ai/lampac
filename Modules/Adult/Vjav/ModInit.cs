using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;

namespace Vjav;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("Vjav", conf, "vjav")
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
        conf = ModuleInvoke.Init("Vjav", new SisiSettings("Vjav", VjavTo.SiteHost)
        {
            // 21: do sau JavCt=20 -> nhom JAV tu lam lien mach 2..21.
            displayindex = 21,
            streamproxy = true,
            httpversion = 2,
            httptimeout = 30,
            rch_access = "apk",
            stream_access = "apk",
            headers_stream = HeadersModel.Init(
                ("User-Agent", VjavTo.ChromeUA),
                ("Referer", VjavTo.SiteHost + "/")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", VjavTo.ChromeUA),
                ("Referer", VjavTo.SiteHost + "/")
            ).ToDictionary()
        });
    }
}
