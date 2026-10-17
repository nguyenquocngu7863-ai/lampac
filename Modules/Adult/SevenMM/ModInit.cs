using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;

namespace SevenMM;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("7mmtv.sx", conf, "sevenmm")
        };
    }

    public void Loaded(InitspaceModel baseconf)
    {
        updateConf();
        EventListener.UpdateInitFile += updateConf;
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
    }

    void updateConf()
    {
        // 7mmtv.sx nam sau Cloudflare: IP Termux bi 403/SSL-reset khi fetch
        // don dap -> uu tien rch (proxy) neu bat, con lai fetch direct.
        // CDN stream (1024cdn/acek-cdn/intelligentworkflow.space) BAT BUOC co
        // Referer -> stream di qua HostStreamProxy kem header.
        conf = ModuleInvoke.Init("SevenMM", new SisiSettings("7MMTV", SevenMMTo.SiteHost)
        {
            displayindex = 23,
            streamproxy = true,
            httpversion = 1,
            httptimeout = 25,
            rch_access = "apk,cors",
            stream_access = "apk,cors",
            headers_stream = HeadersModel.Init(
                ("User-Agent", SevenMMTo.ChromeUA),
                ("Referer", SevenMMTo.SiteHost + "/")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", SevenMMTo.ChromeUA),
                ("Referer", SevenMMTo.SiteHost + "/")
            ).ToDictionary()
        });
    }
}
