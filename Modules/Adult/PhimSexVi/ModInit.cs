using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;

namespace PhimSexVi;
public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;
    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("PhimSexVi", conf, "phimsexvi")
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
    static void updateConf()
    {
        conf = ModuleInvoke.Init("PhimSexVi", new SisiSettings("PhimSexVi", PhimSexViTo.SiteHost)
        {
            spider = false,
            displayindex = 75,
            httpversion = 2,
            httptimeout = 20,
            streamproxy = true,
            rch_access = "apk",
            stream_access = "apk,cors,web",
            headers = new Dictionary<string, string>()
            {
                ["User-Agent"] = PhimSexViTo.ChromeUA,
                ["referer"] = PhimSexViTo.SiteHost + "/"
            },
        });
    }
}
