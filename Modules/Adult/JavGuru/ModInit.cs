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

    // maxstream.org (STREAM JK) tra 403 nginx neu request khong "gia" nhu mot
    // trinh duyet. Do lai 2 nguyen nhan, deu do bang cach bo header:
    //
    //  1) accept-language: bo het header nao cung 403 (ru-RU/ru/uk-UA/en-US/vi-VN
    //     deu 403, khong co moi 200).
    //  2) client-hints mau thuan: ProxyAPI them san sec-ch-ua-platform:"Windows"
    //     + UA Windows, trong khi module dat UA mobile Android → hai manh tinh
    //     mau thuan nhau, maxstream cho 403. Bo sec-ch-ua-* de chi con UA Android.
    //
    // Chi lo khi host la maxstream de khong anh huong module khac.
    static Task StripMaxstreamHeaders(EventProxyApiCreateHttpRequest em)
    {
        try
        {
            string host = em.uri?.Host;
            if (string.IsNullOrEmpty(host) || host.IndexOf("maxstream.org", StringComparison.OrdinalIgnoreCase) < 0)
                return Task.CompletedTask;

            em.requestMessage.Headers.Remove("accept-language");
            em.requestMessage.Headers.Remove("sec-ch-ua-platform");
            em.requestMessage.Headers.Remove("sec-ch-ua-mobile");
            em.requestMessage.Headers.Remove("sec-ch-ua");
            em.requestMessage.Headers.Remove("sec-ch-ua-arch");
            em.requestMessage.Headers.Remove("sec-ch-ua-model");
            em.requestMessage.Headers.Remove("sec-ch-ua-full-version-list");
            em.requestMessage.Headers.Remove("sec-ch-ua-bitness");
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
