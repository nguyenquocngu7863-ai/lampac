using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.PlaywrightCore;
using Shared.Services;
using System.Collections.Generic;

namespace Stripchat;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        if (conf.priorityBrowser == "http" || conf.rhub || PlaywrightBrowser.Status != PlaywrightStatus.disabled ||
            !string.IsNullOrEmpty(conf.overridehost) || conf.overridehosts?.Length > 0)
            return new() { new("stripchat.com", conf, "stripchat") };
        return null;
    }

    public void Loaded(InitspaceModel baseconf)
    {
        UpdateConf();
        EventListener.UpdateInitFile += UpdateConf;
    }

    public void Dispose() => EventListener.UpdateInitFile -= UpdateConf;

    void UpdateConf()
    {
        const string site = "https://stripchat.com";
        const string ua = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36";
        conf = ModuleInvoke.Init("Stripchat", new SisiSettings("Stripchat", site)
        {
            spider = false,
            displayindex = 26,
            rch_access = "apk,cors",
            stream_access = "apk,cors,web",
            kit = false,
            rhub = false,
            streamproxy = true,
            rchstreamproxy = "web",
            headers = HeadersModel.Init(("User-Agent", ua), ("Referer", site + "/"), ("Accept", "application/json")).ToDictionary(),
            headers_stream = HeadersModel.Init(("User-Agent", ua), ("Referer", site + "/"), ("Origin", site), ("Accept", "*/*")).ToDictionary()
        });
        conf.kit = false;
        conf.streamproxy = true;
    }
}
