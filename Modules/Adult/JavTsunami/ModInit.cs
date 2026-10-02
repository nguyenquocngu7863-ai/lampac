using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace JavTsunami;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("JavTsunami", conf, "javtsunami")
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
        conf = ModuleInvoke.Init("JavTsunami", new SisiSettings("JavTsunami", "https://javtsunami.com")
        {
            // 9 = nam ke JavGuru (8), tuc la cuoi nhom tu lam truoc VietSexBlog 10.
            displayindex = 4,
            streamproxy = true,
            httpversion = 2,
            rch_access = "apk",
            stream_access = "apk",
            // KHONG dat Referer/Origin o headers_stream: segment cua server
            // Turbo nam o turbosplayer/turboviplay/lh3.googleusercontent, da do
            // thi ca 3 deu 200 du co hay khong Referer — nhung de an toan thi bo
            // han (giong server 1 cua JavGuru, CDN doi 429 khi thay Referer).
            headers_stream = HeadersModel.Init(
                ("User-Agent", JavTsunamiTo.ChromeUA)
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", JavTsunamiTo.ChromeUA),
                ("Referer", "https://javtsunami.com/")
            ).ToDictionary()
        });
    }
}
