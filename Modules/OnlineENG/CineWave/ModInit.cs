using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using System.Collections.Generic;

namespace CineWave;

public sealed class ModInit : IModuleLoaded, IModuleOnline
{
    public static ModuleConf conf;

    public List<ModuleOnlineItem> Invoke(
        HttpContext httpContext,
        RequestModel requestInfo,
        string host,
        OnlineEventsModel args
    )
    {
        if (conf == null || !conf.enable || args == null)
            return null;

        // Keep the rest of the ENG embed group hidden. `CineWave.enabled: true`
        // is an explicit per-source opt-in while global disableEng stays true.
        bool allowWhenEngDisabled = conf.enabled;
        if ((CoreInit.conf?.disableEng != false && !allowWhenEngDisabled) ||
            (args.original_language != null && args.original_language != "en"))
            return null;

        // Direct HdHub/Stremio catalog does not require a browser. Chromium is
        // used only as a fallback when the direct catalog has no stream.

        // Nguồn chỉ key theo TMDB id.
        if (!(args.source is "tmdb" or "cub") ||
            !long.TryParse(args.id, out long tmdbId) || tmdbId <= 0)
            return null;

        return new List<ModuleOnlineItem>
        {
            new(conf, "cinewave", "CineWave", " (ENG)")
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
        conf = ModuleInvoke.Init("CineWave", new ModuleConf());
    }

    static string OnlineApiQuality(EventOnlineApiQuality e)
    {
        return e.balanser == "cinewave" ? " ~ 2160p" : null;
    }
}
