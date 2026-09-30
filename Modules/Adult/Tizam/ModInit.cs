using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;

namespace Tizam;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("tizam.pw", conf, "tizam")
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
        // `tv4.tizam.org` da 301 sang `amu.tizam.org`; cau truc HTML
        // giong het, chi doi host la chay.
        conf = ModuleInvoke.Init("Tizam", new SisiSettings("Tizam", "https://amu.tizam.org")
        {
            displayindex = 29,
            rch_access = "apk,cors",
            stream_access = "apk,cors",
            rchstreamproxy = "web"
        });
    }
}
