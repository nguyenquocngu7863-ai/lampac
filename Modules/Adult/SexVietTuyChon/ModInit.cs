using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;

namespace SexVietTuyChon;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("SexVietTuyChon", conf, "sexviettuyenchon")
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
        conf = ModuleInvoke.Init("SexVietTuyChon", new SisiSettings("SexVietTuyChon", "https://sexviettuyenchon.com")
        {
            displayindex = 8,
            streamproxy = true,
            rch_access = "apk",
            stream_access = "apk,cors",
            headers_stream = HeadersModel.Init(
                ("User-Agent", SexVietTuyChonTo.ChromeUA),
                ("Referer", "https://sexviettuyenchon.com/")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", SexVietTuyChonTo.ChromeUA)
            ).ToDictionary()
        });
    }
}