using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Online.Settings;
using Shared.Services;
using System.Collections.Generic;

namespace Mapple4K;

public sealed class ModInit : IModuleLoaded, IModuleOnline
{
    public static OnlinesSettings conf;

    public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        if (conf == null || !conf.enable || args == null)
            return null;

        bool allowWhenEngDisabled = conf.enabled;
        if ((CoreInit.conf?.disableEng != false && !allowWhenEngDisabled) ||
            (args.original_language != null && args.original_language != "en"))
            return null;

        if (!(args.source is "tmdb" or "cub") ||
            !long.TryParse(args.id, out long tmdbId) || tmdbId <= 0)
            return null;

        return new List<ModuleOnlineItem>
        {
            new(conf, "mapple4k", "Mapple 4K", " (ENG)")
        };
    }

    public void Loaded(InitspaceModel baseconf)
    {
        UpdateConf();
        EventListener.UpdateInitFile += UpdateConf;
        EventListener.OnlineApiQuality += OnlineApiQuality;
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= UpdateConf;
        EventListener.OnlineApiQuality -= OnlineApiQuality;
    }

    static void UpdateConf()
    {
        conf = ModuleInvoke.Init("Mapple4K", new OnlinesSettings("Mapple4K", "https://mapple.uk")
        {
            displayindex = 1002,
            kit = false,
            rhub = false,
            httptimeout = 25,
            streamproxy = true
        });

        conf.host = "https://mapple.uk";
        conf.kit = false;
        conf.rhub = false;
        conf.httptimeout = 25;
        conf.streamproxy = true;
    }

    static string OnlineApiQuality(EventOnlineApiQuality e)
        => e.balanser == "mapple4k" ? " ~ 2160p" : null;
}
