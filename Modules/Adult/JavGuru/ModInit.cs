using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;

namespace JavGuru;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("JavGuru", conf, "javguru")
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
        conf = ModuleInvoke.Init("JavGuru", new SisiSettings("JavGuru", JavGuruTo.SiteHost)
        {
            displayindex = 21,
            // stream qua proxy Lampac: can Referer theo tung host + token vidara gan IP server
            streamproxy = true,
            headers_image = HeadersModel.Init(
                ("User-Agent", JavGuruTo.ChromeUA),
                ("Referer", JavGuruTo.SiteHost + "/")
            ).ToDictionary()
        });
    }
}
