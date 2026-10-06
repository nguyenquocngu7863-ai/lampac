using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;

namespace DuJav;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("d.dujav.com", conf, "dujav")
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
        conf = ModuleInvoke.Init("DuJav", new SisiSettings("DuJav", "https://d.dujav.com")
        {
            displayindex = 32,
            rch_access = "apk,cors",
            stream_access = "apk,cors",

            kit = false,
            rhub = false,
            qualitys_proxy = false,
            url_reserve = false,

            // Stream cdn hien tai khong doi Referer (da do truc tiep 200),
            // nhung di qua proxy local de giu dung origin khi can.
            streamproxy = true,
            rchstreamproxy = "web",
            headers = HeadersModel.Init(
                ("User-Agent", DuJavTo.ChromeUA),
                ("Referer", "https://d.dujav.com/"),
                ("Accept-Language", "en-US,en;q=0.9")
            ).ToDictionary(),
            headers_stream = HeadersModel.Init(
                ("User-Agent", DuJavTo.ChromeUA),
                ("Referer", "https://d.dujav.com/"),
                ("Accept", "video/webm,video/mp4,video/*;q=0.9,*/*;q=0.5"),
                ("Accept-Language", "en-US,en;q=0.9")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("Accept", "image/jpeg,image/png,image/*;q=0.8,*/*;q=0.5"),
                ("User-Agent", DuJavTo.ChromeUA),
                ("Referer", "https://d.dujav.com/"),
                ("Cache-Control", "max-age=0")
            ).ToDictionary()
        });
    }
}
