using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;

namespace Viet69Av;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("Viet69Av", conf, "viet69av")
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
        conf = ModuleInvoke.Init("Viet69Av", new SisiSettings("Viet69Av", "https://viet69av.com")
        {
            displayindex = 30,
            streamproxy = true,
            rchstreamproxy = "web",
            // httpversion = 1: site sau Cloudflare, h2 cua .NET hay tra rong
            // trong khi curl h1 van 200 (giong JavMoi/PubJav).
            httpversion = 1,
            httptimeout = 12,
            rch_access = "apk",
            stream_access = "apk,cors",
            headers_stream = HeadersModel.Init(
                ("User-Agent", Viet69AvTo.ChromeUA),
                ("Referer", Viet69AvTo.SiteHost + "/"),
                ("Origin", Viet69AvTo.SiteHost),
                ("sec-fetch-dest", "video"),
                ("sec-fetch-mode", "no-cors"),
                ("sec-fetch-site", "same-origin")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", Viet69AvTo.ChromeUA),
                ("Referer", Viet69AvTo.SiteHost + "/")
            ).ToDictionary()
        });
    }
}
