using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;

namespace XNhau;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;

    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("xNhau", conf, "xnhau")
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
        conf = ModuleInvoke.Init("XNhau", new SisiSettings("XNhau", "https://xnhau.free")
        {
            displayindex = 11,
            streamproxy = true,
            httpversion = 2,
            rch_access = "apk",
            stream_access = "apk",
            headers_stream = HeadersModel.Init(
                ("referer", "https://xnhau.free/")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("referer", "https://xnhau.free/")
            ).ToDictionary()
        });

        XNhauTo.SiteHost = conf.host?.TrimEnd('/') ?? "https://xnhau.free";
    }
}
