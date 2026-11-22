using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;
namespace FapClub;
public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("fapclub.vip", conf, "fapclub")
        };
    }
    public void Loaded(InitspaceModel baseconf)
    {
        updateConf();
        EventListener.UpdateInitFile += updateConf;
    }
    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
    }
    void updateConf()
    {
        conf = ModuleInvoke.Init("FapClub", new SisiSettings("FapClub", "https://xxx.fapclub.vip")
        {
            displayindex = 30
        });
    }
}
