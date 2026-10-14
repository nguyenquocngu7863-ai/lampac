using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;

namespace Manko;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("Manko", conf, "manko")
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
        // manko.fun: SPA, API tren healertanker.com (fallback robustx.shop).
        // Stream wasabi direct duoc khi co Referer embed; helvid IP-bound.
        conf = ModuleInvoke.Init("Manko", new SisiSettings("Manko", MankoTo.SiteHost)
        {
            displayindex = 33,
            streamproxy = true,
            httpversion = 1,
            httptimeout = 25,
            rch_access = "apk",
            stream_access = "apk",
            headers_stream = HeadersModel.Init(
                ("User-Agent", MankoTo.ChromeUA),
                ("Referer", "https://manko.fun/")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", MankoTo.ChromeUA),
                ("Referer", "https://manko.fun/")
            ).ToDictionary()
        });
    }
}
