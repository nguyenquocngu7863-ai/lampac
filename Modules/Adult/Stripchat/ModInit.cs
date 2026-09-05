using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;

namespace Stripchat;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("stripchat.com", conf, "strp")
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
        const string ua = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Mobile Safari/537.36";
        const string site = "https://stripchat.com";

        conf = ModuleInvoke.Init("Stripchat", new SisiSettings("Stripchat", site)
        {
            displayindex = 20,
            streamproxy = true,
            qualitys_proxy = false,
            rhub = true,
            rhub_fallback = true,
            rhub_streamproxy = true,
            httpversion = 2,
            headers = HeadersModel.Init(
                ("User-Agent", ua),
                ("Referer", site + "/"),
                ("Accept-Language", "en-US,en;q=0.9")
            ).ToDictionary(),
            headers_stream = HeadersModel.Init(
                ("User-Agent", ua),
                ("Referer", site + "/"),
                ("Origin", site),
                ("Accept", "*/*"),
                ("Accept-Language", "en-US,en;q=0.9")
            ).ToDictionary()
        });
    }
}
