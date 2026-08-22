using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using System;
using System.Collections.Generic;

namespace WebStreamr;

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

        if (!HasSupportedId(args))
            return null;

        return new List<ModuleOnlineItem>
        {
            new(conf, "webstreamr", "WebStreamr", " (HTTP)")
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
        conf = ModuleInvoke.Init("WebStreamr", new ModuleConf());
    }

    static string OnlineApiQuality(EventOnlineApiQuality e)
    {
        return e.balanser == "webstreamr" ? " ~ HTTP" : null;
    }

    static bool HasSupportedId(OnlineEventsModel args)
    {
        if (!string.IsNullOrWhiteSpace(args.imdb_id) &&
            args.imdb_id.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(args.id) &&
            (args.id.StartsWith("tt", StringComparison.OrdinalIgnoreCase) ||
             args.id.StartsWith("tmdb:", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return (args.source is "tmdb" or "cub") &&
            long.TryParse(args.id, out long tmdbId) &&
            tmdbId > 0;
    }
}
