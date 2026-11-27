using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;

namespace Sxyprn;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("Sxyprn", conf, "sxyprn")
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
        conf = ModuleInvoke.Init("Sxyprn", new SisiSettings("Sxyprn", "https://www.sxyprn.com")
        {
            displayindex = 83,
            streamproxy = false,
            httpversion = 2,
            rch_access = "apk",
            stream_access = "apk",
            headers_stream = HeadersModel.Init(
                ("User-Agent", SxyprnTo.ChromeUA),
                ("Referer", "https://www.sxyprn.com/")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", SxyprnTo.ChromeUA),
                ("Referer", "https://www.sxyprn.com/")
            ).ToDictionary()
        });
    }
}
