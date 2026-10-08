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

namespace SupJav;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("SupJav", conf, "supjav")
        };
    }

    public void Loaded(InitspaceModel baseconf)
    {
        modpath = baseconf.path;
        updateConf();
        EventListener.UpdateInitFile += updateConf;
        EventListener.ProxyApiCreateHttpRequest += StripLuluHeaders;
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
        EventListener.ProxyApiCreateHttpRequest -= StripLuluHeaders;
    }

    // LuluStream (server LUC, master tren *.tnmr.org) tra 403 nginx neu
    // request khong "gia" nhu trinh duyet — chom tu JavGuru (STREAM LU):
    // UA android + KHONG Accept-Language/sec-ch-ua* (da bisect 2026-09-27).
    // Copy sang day de SupJav tu dung, khong phu thuoc JavGuru load.
    // KHONG ghim version o day (de pipeline tu dam phan nhu JavGuru).
    static Task StripLuluHeaders(EventProxyApiCreateHttpRequest em)
    {
        try
        {
            string host = em.uri?.Host ?? "";
            bool lulu = host.IndexOf("tnmr.org", StringComparison.OrdinalIgnoreCase) >= 0
                || host.IndexOf("streamhihi", StringComparison.OrdinalIgnoreCase) >= 0
                || host.IndexOf("lulu", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!lulu || em.requestMessage == null)
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
            em.requestMessage.Headers.TryAddWithoutValidation("User-Agent", SupJavTo.ChromeUA);
        }
        catch { }

        return Task.CompletedTask;
    }

    void updateConf()
    {
        // supjav.com CF chan proxy SIN (403 challenge) nhung mo direct VN (200).
        // De useproxy=false, fetch direct + retry (SSL abort theo dot). Stream
        // HLS (StreamHg) + mp4 (StreamTape) deu direct duoc.
        conf = ModuleInvoke.Init("SupJav", new SisiSettings("SupJav", SupJavTo.SiteHost)
        {
            displayindex = 8,
            streamproxy = true,
            httpversion = 1,
            httptimeout = 25,
            rch_access = "apk",
            stream_access = "apk",
            headers_stream = HeadersModel.Init(
                ("User-Agent", SupJavTo.ChromeUA),
                ("Referer", SupJavTo.SiteHost + "/")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("User-Agent", SupJavTo.ChromeUA),
                ("Referer", SupJavTo.SiteHost + "/")
            ).ToDictionary()
        });
    }
}
