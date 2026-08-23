using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Online.Settings;
using Shared.Services;
using System.Collections.Generic;

namespace NguonC;

public class ModInit : IModuleLoaded, IModuleOnline, IModuleOnlineSpider
{
    public static OnlinesSettings conf;

    public List<ModuleOnlineItem> Invoke(
        HttpContext httpContext,
        RequestModel requestInfo,
        string host,
        OnlineEventsModel args)
    {
        return new List<ModuleOnlineItem>
        {
            new(conf, plugin: "nguonc", name: "NguonC")
        };
    }

    public List<ModuleOnlineSpiderItem> Spider(
        HttpContext httpContext,
        RequestModel requestInfo,
        string host,
        OnlineSpiderModel args)
    {
        return new List<ModuleOnlineSpiderItem>
        {
            new(conf, "nguonc-search")
        };
    }

    public void Loaded(InitspaceModel baseconf)
    {
        CoreInit.conf.online.with_search.Add("nguonc");

        updateConf();
        EventListener.UpdateInitFile += updateConf;
        EventListener.OnlineApiQuality += onlineApiQuality;
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
        EventListener.OnlineApiQuality -= onlineApiQuality;
    }

    private static void updateConf()
    {
        conf = ModuleInvoke.Init(
            "NguonC",
            new OnlinesSettings(
                "NguonC",
                "https://phim.nguonc.com",
                streamproxy: true,
                rch_access: "apk,cors",
                stream_access: "apk,cors,web"
            )
            {
                displayindex = 585,
                headers = HeadersModel.Init(Http.defaultFullHeaders).ToDictionary(),
                headers_stream = HeadersModel.Init(
                    Http.defaultFullHeaders,
                    ("accept", "*/*"),
                    ("origin", "https://phim.nguonc.com"),
                    ("referer", "https://phim.nguonc.com/")
                ).ToDictionary()
            }
        );
    }

    private static string onlineApiQuality(EventOnlineApiQuality e)
    {
        return e.balanser == "nguonc" ? " ~ 1080p" : null;
    }
}
