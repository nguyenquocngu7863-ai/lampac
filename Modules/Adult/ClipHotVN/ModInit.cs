using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using Shared.Services.Hybrid;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace ClipHotVN;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("ClipHotVN", conf, "cliphotvn")
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
        conf = ModuleInvoke.Init("ClipHotVN", new SisiSettings("ClipHotVN", ClipHotVNTo.SiteHost)
        {
            displayindex = 30,
            streamproxy = true,
            httpversion = 2,
            httptimeout = 20,
            rch_access = "apk",
            stream_access = "apk",
            headers_stream = HeadersModel.Init(
                ("User-Agent", ClipHotVNTo.ChromeUA),
                ("Referer", ClipHotVNTo.SiteHost + "/"),
                ("Origin", ClipHotVNTo.SiteHost)
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", ClipHotVNTo.ChromeUA),
                ("Referer", ClipHotVNTo.SiteHost + "/")
            ).ToDictionary()
        });
    }
}
