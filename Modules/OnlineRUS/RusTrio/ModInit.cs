using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using System.Collections.Generic;

namespace RusTrio;

public class ModInit : IModuleLoaded, IModuleOnline
{
    public static BaseSettings conf;

    public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        if (conf?.enable != true)
            return null;

        return new List<ModuleOnlineItem>()
        {
            new(conf)
        };
    }

    public void Loaded(InitspaceModel baseconf)
    {
        UpdateConf();
        EventListener.UpdateInitFile += UpdateConf;
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= UpdateConf;
    }

    void UpdateConf()
    {
        conf = ModuleInvoke.Init("RusTrio", new BaseSettings()
        {
            plugin = "rustrio",
            enable = true,
            displayindex = 509
        });
    }
}
