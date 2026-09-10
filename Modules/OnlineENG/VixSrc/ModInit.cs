using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Online.Settings;
using Shared.Services;
using System.Collections.Generic;

namespace VixSrc;

public class ModInit : IModuleLoaded, IModuleOnline
{
    public static OnlinesSettings conf;

    public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        var online = new List<ModuleOnlineItem>();

        bool allowWhenEngDisabled = conf?.enabled == true;
        if ((args.original_language == null || args.original_language == "en") &&
            (CoreInit.conf.disableEng == false || allowWhenEngDisabled))
        {
            if (args.source != null && (args.source is "tmdb" or "cub") && long.TryParse(args.id, out long id) && id > 0)
                online.Add(new(conf, "vixsrc", "VixSrc.to", " (ENG)"));
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
        conf = ModuleInvoke.Init("VixSrc", new OnlinesSettings("VixSrc", "https://vixsrc.to")
        {
            displayindex = 1022,
            kit = false,
            rhub = false,
            httptimeout = 20,
            streamproxy = true
        });

        conf.kit = false;
        conf.rhub = false;
        conf.httptimeout = 20;
        conf.streamproxy = true;
        conf.headers_stream ??= HeadersModel.Init(
            ("User-Agent", Http.UserAgent),
            ("Referer", "https://vixsrc.to/"),
            ("Origin", "https://vixsrc.to")
        ).ToDictionary();
    }

    private string OnlineApiQuality(EventOnlineApiQuality e)
    {
        return e.balanser == "vixsrc" ? " ~ 1080p" : null;
    }
}
