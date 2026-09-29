using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;

namespace DevFetch;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(
        HttpContext httpContext, RequestModel requestInfo,
        string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("DevFetch", conf, "devfetch")
        };
    }

    public void Loaded(InitspaceModel baseconf)
    {
        modpath = baseconf.path;
        updateConf();
        EventListener.UpdateInitFile += updateConf;
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("DevFetch",
            new SisiSettings("DevFetch", "http://localhost")
        {
            displayindex = 9999,
            group_hide = true
        });
    }
}
