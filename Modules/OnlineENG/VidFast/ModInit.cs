using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Online.Settings;
using Shared.PlaywrightCore;
using Shared.Services;
using System.Collections.Generic;

namespace VidFast;

public class ModInit : IModuleLoaded, IModuleOnline
{
    public static OnlinesSettings conf;

    public List<ModuleOnlineItem> Invoke(
        HttpContext httpContext, RequestModel requestInfo,
        string host, OnlineEventsModel args)
    {
        var online = new List<ModuleOnlineItem>();

        // Isolated opt-in while the rest of the ENG group stays hidden.
        bool allowWhenEngDisabled = conf?.enabled == true;
        if ((args.original_language == null
                || args.original_language == "en")
            && (CoreInit.conf.disableEng == false
                || allowWhenEngDisabled))
        {
            if (args.source != null
                && (args.source is "tmdb" or "cub")
                && long.TryParse(args.id, out long id) && id > 0)
            {
                if (PlaywrightBrowser.Status
                    != PlaywrightStatus.disabled)
                    online.Add(new(conf, "vidfast",
                        "VidFast", " (ENG)"));
            }
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
        conf = ModuleInvoke.Init("VidFast",
            new OnlinesSettings("VidFast", "https://vidfast.vc")
        {
            displayindex = 1077,
            kit = false,
            rhub = false,
            streamproxy = true
        });

        // Force the documented endpoint so stale init/Kit values
        // cannot route elsewhere.
        conf.host = "https://vidfast.vc";
        conf.kit = false;
        conf.rhub = false;
        conf.streamproxy = true;
    }

    private string OnlineApiQuality(EventOnlineApiQuality e)
    {
        return e.balanser == "vidfast" ? " ~ HD" : null;
    }
}
