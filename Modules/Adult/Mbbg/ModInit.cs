using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;

namespace Mbbg;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("Mbbg", conf, "mbbg")
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
        conf = ModuleInvoke.Init("Mbbg", new SisiSettings("Mbbg", MbbgTo.SiteHost)
        {
            // 16: gan ngay sau SexVietDam (15) — nhom module cua minh cho gan nhau.
            displayindex = 16,
            // BAT BUOC: URL googlevideo gan IP client nen phai di qua proxy cua server.
            streamproxy = true,
            httpversion = 1,
            httptimeout = 20,
            rch_access = "apk",
            stream_access = "apk",
            headers_stream = HeadersModel.Init(
                ("User-Agent", MbbgTo.ChromeUA),
                ("Referer", MbbgTo.SiteHost + "/")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", MbbgTo.ChromeUA),
                ("Referer", MbbgTo.SiteHost + "/")
            ).ToDictionary()
        });
    }
}
