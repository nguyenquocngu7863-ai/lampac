using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;

namespace XHeoVL;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("XHeoVL", conf, "xheovl")
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
        // xheovl.site: CSR site (AlpineJS), cần Playwright render
        // Player: VB_PLAY token -> /play.php?token= -> iframe embed
        conf = ModuleInvoke.Init("XHeoVL", new SisiSettings("XHeoVL", "https://xheovl.site")
        {
            displayindex = 25,
            streamproxy = true,
            httpversion = 1,
            httptimeout = 30,
            rch_access = "apk",
            stream_access = "apk",
        });
    }
}
