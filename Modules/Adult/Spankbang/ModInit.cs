using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.PlaywrightCore;
using Shared.Services;
using System.Collections.Generic;

namespace Spankbang;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        if (conf.priorityBrowser == "http" || conf.rhub || PlaywrightBrowser.Status != PlaywrightStatus.disabled || !string.IsNullOrEmpty(conf.overridehost) || conf.overridehosts?.Length > 0)
        {
            return new List<SisiModuleItem>()
            {
                new("spankbang.com", conf, "sbg")
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
        const string site = "https://ru.spankbang.com";

        conf = ModuleInvoke.Init("Spankbang", new SisiSettings("Spankbang", site)
        {
            httpversion = 2,
            displayindex = 16,
            rch_access = "apk,cors,web",
            stream_access = "apk,cors,web",
            streamproxy = true,
            headers = HeadersModel.Init(
                ("referer", site + "/")
            ).ToDictionary(),
            headers_stream = HeadersModel.Init(
                ("Referer", site + "/"),
                ("Origin", site),
                ("Accept", "*/*")
            ).ToDictionary()
        });

        // CDN MP4 is referer-locked; inner Android player cannot send Referer.
        conf.streamproxy = true;
    }
}
