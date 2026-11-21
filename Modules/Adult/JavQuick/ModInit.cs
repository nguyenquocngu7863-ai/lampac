using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;

namespace JavQuick;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("JavQuick", conf, "javquick")
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
        conf = ModuleInvoke.Init("JavQuick", new SisiSettings("JavQuick", "https://javquick.com")
        {
            displayindex = 8,
            streamproxy = true,
            // httpversion = 1 BAT BUOC (2026-11-21): javquick.com (sau Cloudflare)
            // cat TLS ngay khi client chao ALPN h2 -> "unexpected eof while reading".
            // Do: curl mac dinh (h2) / curl_cffi chrome124 / openssl = fail het;
            //     curl --http1.1 + UA Chrome = 200 (2/2 lan). .NET de h2 thi module
            //     tra 503 "playlists" sau ~50s (thu 2 lan).
            httpversion = 1,
            rch_access = "apk",
            stream_access = "apk",
            headers_stream = HeadersModel.Init(
                ("User-Agent", JavQuickTo.ChromeUA),
                ("Referer", "https://javquick.com/")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", JavQuickTo.ChromeUA),
                ("Referer", "https://javquick.com/")
            ).ToDictionary()
        });
    }
}
