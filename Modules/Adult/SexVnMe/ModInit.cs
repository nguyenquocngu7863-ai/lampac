using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;

namespace SexVnMe;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("SexVnMe", conf, "sexvnme")
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
        // sexvnme.com: HTML tinh (khong CSR), fetch direct duoc.
        // Stream m3u8 tren domain113.com (cdn=tt) direct duoc (200 mpegurl).
        conf = ModuleInvoke.Init("SexVnMe", new SisiSettings("SexVnMe", "https://sexvnme.com")
        {
            displayindex = 21,
            streamproxy = true,
            httpversion = 1,
            httptimeout = 25,
            rch_access = "apk",
            stream_access = "apk",
            headers_stream = HeadersModel.Init(
                ("User-Agent", SexVnMeTo.ChromeUA),
                ("Referer", "https://sexvnme.com/")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", SexVnMeTo.ChromeUA),
                ("Referer", "https://sexvnme.com/")
            ).ToDictionary()
        });
    }
}
