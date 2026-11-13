using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;

namespace VNJav;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("VNJav", conf, "vnjav")
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
        conf = ModuleInvoke.Init("VNJav", new SisiSettings("VNJav", "https://vnjav.net")
        {
            displayindex = 7,
            streamproxy = true,
            httpversion = 2,
            rch_access = "apk",
            stream_access = "apk",
            headers_stream = HeadersModel.Init(
                ("User-Agent", VNJavTo.ChromeUA),
                ("Referer", "https://vnjav.net/")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", VNJavTo.ChromeUA),
                ("Referer", "https://vnjav.net/")
            ).ToDictionary()
        });
    }
}
