using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;

namespace SupJav;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("SupJav", conf, "supjav")
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
        // supjav.com CF chan proxy SIN (403 challenge) nhung mo direct VN (200).
        // De useproxy=false, fetch direct + retry (SSL abort theo dot). Stream
        // HLS (StreamHg) + mp4 (StreamTape) deu direct duoc.
        conf = ModuleInvoke.Init("SupJav", new SisiSettings("SupJav", SupJavTo.SiteHost)
        {
            displayindex = 8,
            streamproxy = true,
            httpversion = 1,
            httptimeout = 25,
            rch_access = "apk",
            stream_access = "apk",
            headers_stream = HeadersModel.Init(
                ("User-Agent", SupJavTo.ChromeUA),
                ("Referer", SupJavTo.SiteHost + "/")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", SupJavTo.ChromeUA),
                ("Referer", SupJavTo.SiteHost + "/")
            ).ToDictionary()
        });
    }
}
