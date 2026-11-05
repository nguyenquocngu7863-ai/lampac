using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;

namespace Suong3x;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("Suong3x", conf, "suong3x")
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
        conf = ModuleInvoke.Init("Suong3x", new SisiSettings("Suong3x", Suong3xTo.SiteHost)
        {
            displayindex = 15,
            streamproxy = true,
            httpversion = 2,
            rch_access = "apk",
            stream_access = "apk",
            headers_stream = HeadersModel.Init(
                ("User-Agent", Suong3xTo.ChromeUA),
                ("Referer", Suong3xTo.SiteHost + "/"),
                ("Origin", Suong3xTo.SiteHost)
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", Suong3xTo.ChromeUA),
                ("Referer", Suong3xTo.SiteHost + "/")
            ).ToDictionary()
        });
    }
}
