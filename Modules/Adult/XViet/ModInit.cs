using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;

namespace XViet;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("XViet", conf, "xviet")
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
        // xviet.live: HTML tinh (khong CSR), fetch direct duoc.
        // Player: detail -> window.VB_PLAY.s1 -> /play/?token= -> embed -> jwplayer sources (m3u8).
        conf = ModuleInvoke.Init("XViet", new SisiSettings("XViet", "https://xviet.live")
        {
            displayindex = 34,
            streamproxy = true,
            httpversion = 1,
            httptimeout = 25,
            rch_access = "apk",
            stream_access = "apk",
            headers_stream = HeadersModel.Init(
                ("User-Agent", XVietTo.ChromeUA),
                ("Referer", "https://xviet.live/")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", XVietTo.ChromeUA),
                ("Referer", "https://xviet.live/")
            ).ToDictionary()
        });
    }
}
