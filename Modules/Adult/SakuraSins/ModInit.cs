using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;

namespace SakuraSins;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("SakuraSins", conf, "sakurasins")
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
        conf = ModuleInvoke.Init("SakuraSins", new SisiSettings("SakuraSins", "https://sakurasins.com")
        {
            displayindex = 7,
            streamproxy = true,
            httpversion = 2,
            rch_access = "apk",
            stream_access = "apk",
            headers_stream = HeadersModel.Init(
                ("User-Agent", SakuraSinsTo.ChromeUA),
                ("Referer", "https://sakurasins.com/"),
                ("Origin", "https://sakurasins.com")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", SakuraSinsTo.ChromeUA),
                ("Referer", "https://sakurasins.com/")
            ).ToDictionary()
        });
    }
}
