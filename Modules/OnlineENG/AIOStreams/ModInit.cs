using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using System;
using System.Collections.Generic;

namespace AIOStreams;

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
        if (conf == null || !conf.enable || !conf.streams ||
            string.IsNullOrWhiteSpace(conf.manifest) || args == null)
            return null;

        if (!HasSupportedId(args))
            return null;

        return new List<ModuleOnlineItem>
        {
            new(conf, "aiostreams", "AIOStreams", " (HTTP)")
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
        conf = ModuleInvoke.Init("AIOStreams", new ModuleConf());
    }

    static string OnlineApiQuality(EventOnlineApiQuality e)
    {
        return e.balanser == "aiostreams" ? " ~ HTTP" : null;
    }

    static bool HasSupportedId(OnlineEventsModel args)
    {
        if (IsSupportedId(args.imdb_id))
            return true;

        if (IsSupportedId(args.id))
            return true;

        return (args.source is "tmdb" or "cub") &&
            long.TryParse(args.id, out long tmdbId) &&
            tmdbId > 0;
    }

    static bool IsSupportedId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string normalized = value.Trim();
        return normalized.StartsWith("tt", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("tmdb:", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("kitsu:", StringComparison.OrdinalIgnoreCase);
    }
}
