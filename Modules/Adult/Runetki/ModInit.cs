using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.PlaywrightCore;
using Shared.Services;
using System.Collections.Generic;

namespace Runetki;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        if (conf.priorityBrowser == "http" || conf.rhub || PlaywrightBrowser.Status != PlaywrightStatus.disabled || !string.IsNullOrEmpty(conf.overridehost) || conf.overridehosts?.Length > 0)
        {
            return new List<SisiModuleItem>()
            {
                new("runetki.com", conf, "runetki")
            };
        }

        return null;
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
        const string browserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
        const string site = "https://rus.runetki5.com";

        conf = ModuleInvoke.Init("Runetki", new SisiSettings("Runetki", site)
        {
            spider = false,
            httpversion = 2,
            displayindex = 23,
            rch_access = "apk",
            stream_access = "apk,cors,web",
            kit = false,
            rhub = false,
            qualitys_proxy = false,
            streamproxy = true,
            rchstreamproxy = "web",
            headers = HeadersModel.Init(
                ("User-Agent", browserUserAgent),
                ("referer", site + "/"),
                ("x-requested-with", "XMLHttpRequest")
            ).ToDictionary(),
            headers_stream = HeadersModel.Init(
                ("User-Agent", browserUserAgent),
                ("Referer", site + "/"),
                ("Origin", site),
                ("Accept", "*/*")
            ).ToDictionary()
        });

        // Live bcvcdn HLS is token/CORS bound — Android cannot play it directly.
        conf.kit = false;
        conf.rhub = false;
        conf.qualitys_proxy = false;
        conf.streamproxy = true;
    }
}
