using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;

namespace Av123;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("123AV", conf, "av123")
        };
    }

    public void Loaded(InitspaceModel baseconf)
    {
        modpath = baseconf.path;
        updateConf();
        try { Av123Controller.WarmTaxo(); } catch { }
    }

    public void Dispose()
    {
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("Av123",
            new SisiSettings("123AV", Av123To.SiteHost)
        {
            displayindex = 1,
            streamproxy = true,
            httptimeout = 10,
            headers_stream = HeadersModel.Init(
                ("User-Agent", Av123To.ChromeUA),
                ("Referer", Av123To.SiteHost + "/")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", Av123To.ChromeUA),
                ("Referer", Av123To.SiteHost + "/")
            ).ToDictionary()
        });
    }
}
