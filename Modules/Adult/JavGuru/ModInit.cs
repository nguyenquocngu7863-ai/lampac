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

namespace JavGuru;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("JavGuru", conf, "javguru")
        };
    }

    public void Loaded(InitspaceModel baseconf)
    {
        modpath = baseconf.path;
        updateConf();
        EventListener.UpdateInitFile += updateConf;
        EventListener.ProxyApiCreateHttpRequest += StripMaxstreamHeaders;
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
        EventListener.ProxyApiCreateHttpRequest -= StripMaxstreamHeaders;
    }

    // maxstream.org (STREAM JK) va LuluStream (STREAM LU, host *.tnmr.org)
    // tra 403 nginx neu request khong "gia" nhu mot trinh duyet. Da bisect
    // that tren master.m3u8 cua lulu (2026-09-27):
    //   UA android + khong header        = 200
    //   UA android + Accept-Language     = 403 (en hay ru deu chet)
    //   UA Windows (Chrome 146)          = 403
    //   UA android + Cache-Control/DNT/
    //     sec-ch-ua-platform:Windows     = 200 (vo hai)
    // ProxyAPI luon merge Http.defaultFullHeaders (UA Windows + accept-language
    // ru) nen phai strip o day. Chi lo cac host CDN cua JavGuru de khong anh
    // huong module khac.
    static Task StripMaxstreamHeaders(EventProxyApiCreateHttpRequest em)
    {
        try
        {
            string host = em.uri?.Host ?? "";
            bool lulu = host.IndexOf("tnmr.org", StringComparison.OrdinalIgnoreCase) >= 0
                || host.IndexOf("streamhihi", StringComparison.OrdinalIgnoreCase) >= 0
                || host.IndexOf("lulu", StringComparison.OrdinalIgnoreCase) >= 0;
            if (host.IndexOf("maxstream.org", StringComparison.OrdinalIgnoreCase) < 0 && !lulu)
                return Task.CompletedTask;

            em.requestMessage.Headers.Remove("accept-language");
            em.requestMessage.Headers.Remove("sec-ch-ua-platform");
            em.requestMessage.Headers.Remove("sec-ch-ua-mobile");
            em.requestMessage.Headers.Remove("sec-ch-ua");
            em.requestMessage.Headers.Remove("sec-ch-ua-arch");
            em.requestMessage.Headers.Remove("sec-ch-ua-model");
            em.requestMessage.Headers.Remove("sec-ch-ua-full-version-list");
            em.requestMessage.Headers.Remove("sec-ch-ua-bitness");

            em.requestMessage.Headers.Remove("user-agent");
            em.requestMessage.Headers.TryAddWithoutValidation("User-Agent", JavGuruTo.ChromeUA);
        }
        catch { }

        return Task.CompletedTask;
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("JavGuru", new SisiSettings("JavGuru", "https://jav.guru")
        {
            displayindex = 33,
            streamproxy = true,
            httpversion = 2,
            rch_access = "apk",
            stream_access = "apk",
            // Khong dat Referer/Origin o headers_stream: CDN cua player (turbosplayer,
            // googleusercontent) tra 429 khi nhin thay Referer jav.guru.
            headers_stream = HeadersModel.Init(
                ("User-Agent", JavGuruTo.ChromeUA)
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", JavGuruTo.ChromeUA),
                ("Referer", "https://jav.guru/")
            ).ToDictionary()
        });
    }
}
