using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;

namespace JAVSnap;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("JAVSnap", conf, "javsnap")
        };
    }

    public void Loaded(InitspaceModel baseconf)
    {
        modpath = baseconf.path;
        updateConf();
    }

    public void Dispose()
    {
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("JAVSnap",
            new SisiSettings("JAVSnap", JAVSnapTo.SiteHost)
        {
            displayindex = 50,
            streamproxy = true,
            httptimeout = 8,
            headers_stream = HeadersModel.Init(
                ("User-Agent", JAVSnapTo.ChromeUA),
                ("Referer", JAVSnapTo.SiteHost + "/")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", JAVSnapTo.ChromeUA),
                ("Referer", "https://javsnap.com/")
            ).ToDictionary()
        });
    }
}
