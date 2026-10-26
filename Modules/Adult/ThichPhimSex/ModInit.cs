using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;

namespace ThichPhimSex;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("ThichPhimSex", conf, "thichphimsex")
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
        // thichphimsex.net: HTML tinh (khong CSR), fetch direct duoc.
        // Stream m3u8 tra ve tu /api/player (jwplayer sources hoac iframe).
        conf = ModuleInvoke.Init("ThichPhimSex", new SisiSettings("ThichPhimSex", "https://thichphimsex.net")
        {
            displayindex = 23,
            streamproxy = true,
            httpversion = 1,
            httptimeout = 25,
            rch_access = "apk",
            stream_access = "apk",
            headers_stream = HeadersModel.Init(
                ("User-Agent", ThichPhimSexTo.ChromeUA),
                ("Referer", "https://thichphimsex.net/")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", ThichPhimSexTo.ChromeUA),
                ("Referer", "https://thichphimsex.net/")
            ).ToDictionary()
        });
    }
}
