using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;

namespace JavHD;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("JavHD", conf, "javhd")
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
        conf = ModuleInvoke.Init("JavHD", new SisiSettings("JavHD", "https://javhd.today")
        {
            displayindex = 20,
            streamproxy = true,
            httpversion = 2,
            rch_access = "apk",
            stream_access = "apk",
            rchstreamproxy = "web,cors",
            headers_stream = HeadersModel.Init(
                ("User-Agent", JavHDTo.ChromeUA),
                ("Referer", "https://turbovid.vip/"),
                ("Origin", "https://turbovid.vip")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", JavHDTo.ChromeUA),
                ("Referer", "https://javhd.today/")
            ).ToDictionary()
        });
    }
}
