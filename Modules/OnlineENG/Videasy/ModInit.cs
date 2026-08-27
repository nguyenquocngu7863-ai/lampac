using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Online.Settings;
using Shared.Services;
using System.Collections.Generic;

namespace Videasy;

public class ModInit : IModuleLoaded, IModuleOnline
{
    public static OnlinesSettings conf;

    public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        var online = new List<ModuleOnlineItem>();

        // Keep the broken ENG group globally hidden, but allow Videasy to be
        // tested independently with `Videasy.enabled: true` in init.conf.
        // `enabled` defaults to false, unlike the normal module `enable` flag.
        bool allowWhenEngDisabled = conf?.enabled == true;
        if ((args.original_language == null || args.original_language == "en") &&
            (CoreInit.conf.disableEng == false || allowWhenEngDisabled))
        {
            if (args.source != null && (args.source is "tmdb" or "cub") && long.TryParse(args.id, out long id) && id > 0)
                online.Add(new(conf, "videasy", "Videasy", " (ENG)"));
        }

        return online;
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

    private void UpdateConf()
    {
        conf = ModuleInvoke.Init("Videasy", new OnlinesSettings("Videasy", "https://player.videasy.to")
        {
            displayindex = 1020,
            kit = false,
            rhub = false,
            httptimeout = 20,
            streamproxy = true
        });

        // The maintained resolver is local HTTP + seed decryption. Do not let
        // an older account/Kit profile route it back through the retired
        // browser-click implementation or expose CDN URLs directly.
        conf.kit = false;
        conf.rhub = false;
        conf.httptimeout = 20;
        conf.streamproxy = true;
    }

    private string OnlineApiQuality(EventOnlineApiQuality e)
    {
        return e.balanser == "videasy" ? " ~ 2160p" : null;
    }
}
