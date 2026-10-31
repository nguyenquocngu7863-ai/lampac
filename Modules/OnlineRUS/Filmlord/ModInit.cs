using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using System.Collections.Generic;

namespace Filmlord;

public class ModInit : IModuleLoaded, IModuleOnline, IModuleOnlineSpider
{
    public static FilmlordConf conf;

    public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        return new List<ModuleOnlineItem>()
        {
            new(conf)
        };
    }

    public List<ModuleOnlineSpiderItem> Spider(HttpContext httpContext, RequestModel requestInfo, string host, OnlineSpiderModel args)
    {
        return new List<ModuleOnlineSpiderItem>()
        {
            new(conf, "filmlord-search")
        };
    }

    public void Loaded(InitspaceModel baseconf)
    {
        CoreInit.conf.online.with_search.Add("filmlord");

        updateConf();
        EventListener.UpdateInitFile += updateConf;
        EventListener.OnlineApiQuality += onlineApiQuality;
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
        EventListener.OnlineApiQuality -= onlineApiQuality;
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("Filmlord", new FilmlordConf("Filmlord", "https://filmlord.tv", streamproxy: true)
        {
            displayindex = 576,
            httpversion = 2,
            rch_access = "apk,cors,web",
            stream_access = "apk,cors,web"
        });

        // filmlord.tv: поиск через dle_filter, поток — плеер Collaps (api.embess.ws).
        // Плеер Alloha (absciss, 4K) требует JS — подключается через Playwright в Controller.
    }

    string onlineApiQuality(EventOnlineApiQuality e)
    {
        if (e.balanser != "filmlord")
            return null;

        return " ~ 720p";
    }
}
