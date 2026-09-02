using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;

namespace OneJav;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("onejav.com", conf, "oj")
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
        conf = ModuleInvoke.Init("OneJav", new SisiSettings("OneJav", "https://onejav.com", true, false, true, "apk,cors", "apk,cors")
        {
            displayname = "OneJAV 🎌",
            displayindex = 18,
            rchstreamproxy = "web",
            httpversion = 2,
            headers_image = HeadersModel.Init(
                ("Accept", "image/jpeg,image/png,image/*;q=0.8,*/*;q=0.5"),
                ("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36"),
                ("Referer", "https://onejav.com/")
            ).ToDictionary()
        });
    }

    /// <summary>
    /// Địa chỉ TorrServer nội bộ (dùng instance tích hợp của module TorrServer).
    /// </summary>
    public static string TsHost()
    {
        int port = 9085;
        if (CoreInit.CurrentConf != null &&
            CoreInit.CurrentConf.TryGetValue("TorrServer", out var tsConf))
        {
            var p = tsConf.Value<int?>("tsport");
            if (p.HasValue) port = p.Value;
        }
        return $"http://{CoreInit.conf.listen.localhost}:{port}";
    }
}
