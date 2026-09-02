using Shared;
using Shared.Models.AppConf;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using System.Collections.Generic;

namespace CubProxy;

public class ModInit : IModuleLoaded
{
    public static string modpath;
    public static ModuleConf conf;

    public void Loaded(InitspaceModel baseconf)
    {
        modpath = baseconf.path;

        updateConf();
        EventListener.UpdateInitFile += updateConf;

        foreach (var m in conf.limit_map)
            CoreInit.conf.WAF.limit_map.Insert(0, m);
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("cub", new ModuleConf()
        {
            // Cub TMDB catalog: viewru=1 forces Russian titles/posters on home/browse.
            // Card details still come from TMDB (already English). Keep Cub source, English art.
            viewru = false,
            scheme = CoreInit.conf.cub.scheme,
            domain = CoreInit.conf.cub.domain,
            mirror = CoreInit.conf.cub.mirror,
            cache_api = 180, // 3h
            limit_map = new List<WafLimitRootMap>()
            {
                new("^/cub/", new WafLimitMap { limit = 50, second = 1 })
            }
        });
    }
}
