using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using System;
using System.Collections.Generic;

namespace K20;

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
            new(conf, "k20", "K20 Phim Việt", " (HTTP)")
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
        conf = ModuleInvoke.Init("K20", new ModuleConf());
    }

    static string OnlineApiQuality(EventOnlineApiQuality e)
    {
        return e.balanser == "k20" ? " ~ HTTP" : null;
    }

    static bool HasSupportedId(OnlineEventsModel args)
    {
        if (IsImdbId(args.imdb_id) || IsImdbId(args.id))
            return true;

        // The K20 manifest currently advertises `tt` and its own catalog
        // prefixes, but not `tmdb:`. Keep the source visible for a numeric
        // TMDB item only when Lampac can resolve TMDB external_ids to IMDb in
        // the controller. This is safer than letting an upstream catalog guess
        // a title from a bare number.
        if (string.IsNullOrWhiteSpace(CoreInit.conf?.cub?.api_key))
            return false;

        if (!string.IsNullOrWhiteSpace(args.id) &&
            args.id.StartsWith("tmdb:", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(args.id[5..], out long prefixedTmdbId) &&
            prefixedTmdbId > 0)
        {
            return true;
        }

        return (args.source is "tmdb" or "cub") &&
            long.TryParse(args.id, out long tmdbId) &&
            tmdbId > 0;
    }

    static bool IsImdbId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !value.StartsWith("tt", StringComparison.OrdinalIgnoreCase) ||
            value.Length <= 2)
        {
            return false;
        }

        for (int index = 2; index < value.Length; index++)
        {
            if (!char.IsDigit(value[index]))
                return false;
        }

        return true;
    }
}
