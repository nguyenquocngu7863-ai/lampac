using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;

namespace Jable;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("Jable", conf, "jable")
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
        conf = ModuleInvoke.Init("Jable", new SisiSettings("Jable", JableTo.SiteHost)
        {
            // 5: module mau chuan cua repo nay — doc file nay truoc khi viet module
            // Adult moi (xem skill lampac-adult-module, muc "MODULE MAU CHUAN").
            displayindex = 5,
            streamproxy = true,
            httpversion = 1,
            httptimeout = 20,
            rch_access = "apk",
            stream_access = "apk",
            headers_stream = HeadersModel.Init(
                ("User-Agent", JableTo.ChromeUA),
                ("Referer", JableTo.SiteHost + "/")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", JableTo.ChromeUA),
                ("Referer", JableTo.SiteHost + "/")
            ).ToDictionary()
        });
    }
}