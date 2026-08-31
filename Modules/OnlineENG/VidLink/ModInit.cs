using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Online.Settings;
using Shared.Services;
using System.Collections.Generic;

namespace VidLink;

public class ModInit : IModuleLoaded, IModuleOnline
{
    public static OnlinesSettings conf;

    public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        var online = new List<ModuleOnlineItem>();

        // HTTP resolver — no Playwright required. `enabled: true` keeps VidLink
        // visible when the rest of the ENG group is hidden by disableEng.
        bool allowWhenEngDisabled = conf?.enabled == true;
        if (CoreInit.conf.disableEng == false || allowWhenEngDisabled)
        {
            if (args.source != null && (args.source is "tmdb" or "cub") && long.TryParse(args.id, out long id) && id > 0)
                online.Add(new(conf, "vidlink", "VidLink", " (ENG)"));
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
        conf = ModuleInvoke.Init("VidLink", new OnlinesSettings("VidLink", "https://vidlink.pro")
        {
            displayindex = 1015,
            kit = false,
            rhub = false,
            httptimeout = 20,
            streamproxy = true,
            enabled = true
        });

        conf.kit = false;
        conf.rhub = false;
        conf.httptimeout = 20;
        conf.streamproxy = true;
        // Origin on CDN GETs is a common 403. Referer is enough.
        conf.headers_stream = HeadersModel.Init(
            ("User-Agent", Http.UserAgent),
            ("Referer", "https://vidlink.pro/"),
            ("Accept", "*/*")
        ).ToDictionary();
    }

    private string OnlineApiQuality(EventOnlineApiQuality e)
    {
        return e.balanser == "vidlink" ? " ~ 1080p" : null;
    }
}
