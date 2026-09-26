using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Module;
using Shared.Models.Events;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;

namespace JavEng;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("JavEng", conf, "javeng")
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
        conf = ModuleInvoke.Init("JavEng", new SisiSettings("JavEng", "https://javeng.tv")
        {
            displayindex = 20,
            streamproxy = true,
            httpversion = 2,
            rch_access = "apk",
            stream_access = "apk",
            headers_stream = HeadersModel.Init(
                ("User-Agent", JavEngTo.ChromeUA),
                ("Referer", "https://javeng.tv/"),
                ("Origin", "https://javeng.tv")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", JavEngTo.ChromeUA),
                ("Referer", "https://javeng.tv/")
            ).ToDictionary()
        });
    }
}
