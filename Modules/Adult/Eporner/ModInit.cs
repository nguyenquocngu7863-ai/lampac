using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;

namespace Eporner;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("eporner.com", conf, "epr")
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
        const string browserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

        conf = ModuleInvoke.Init("Eporner", new SisiSettings("Eporner", "https://www.eporner.com")
        {
            httpversion = 2,
            displayindex = 17,
            rch_access = "apk,cors",
            stream_access = "apk,cors",

            // Eporner's CDN rejects naked app/player requests and asks the user
            // to watch on the website. Fetch streams through Lampac with the
            // same origin context as the Eporner web player.
            streamproxy = true,
            rchstreamproxy = "web",
            headers = HeadersModel.Init(
                ("User-Agent", browserUserAgent),
                ("Referer", "https://www.eporner.com/"),
                ("Accept-Language", "en-US,en;q=0.9")
            ).ToDictionary(),
            headers_stream = HeadersModel.Init(
                ("User-Agent", browserUserAgent),
                ("Referer", "https://www.eporner.com/"),
                ("Origin", "https://www.eporner.com"),
                ("Accept", "video/webm,video/mp4,video/*;q=0.9,*/*;q=0.5"),
                ("Accept-Language", "en-US,en;q=0.9")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("Accept", "image/jpeg,image/png,image/*;q=0.8,*/*;q=0.5"),
                ("User-Agent", browserUserAgent),
                ("Referer", "https://www.eporner.com/"),
                ("Cache-Control", "max-age=0")
            ).ToDictionary()
        });
    }
}
