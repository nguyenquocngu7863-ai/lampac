using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;

namespace PhimSexVN88;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("PhimSexVN88", conf, "phimsexvn88")
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
        // phimsexvn88.com: HTML tinh (list/detail fetch direct duoc).
        // Player: detail data-video-id -> POST /api/player {id,server} -> jwplayer sources[0].file (m3u8 domain115.com)
        conf = ModuleInvoke.Init("PhimSexVN88", new SisiSettings("PhimSexVN88", "https://phimsexvn88.com")
        {
            displayindex = 35,
            streamproxy = true,
            httpversion = 1,
            httptimeout = 25,
            rch_access = "apk",
            stream_access = "apk",
            headers_stream = HeadersModel.Init(
                ("User-Agent", PhimSexVN88To.ChromeUA),
                ("Referer", "https://phimsexvn88.com/")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", PhimSexVN88To.ChromeUA),
                ("Referer", "https://phimsexvn88.com/")
            ).ToDictionary()
        });
    }
}
