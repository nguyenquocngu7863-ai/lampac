using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.PlaywrightCore;
using Shared.Services;
using System.Collections.Generic;

namespace Chaturbate;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        if (conf.priorityBrowser == "http" || conf.rhub || PlaywrightBrowser.Status != PlaywrightStatus.disabled || !string.IsNullOrEmpty(conf.overridehost) || conf.overridehosts?.Length > 0)
        {
            return new List<SisiModuleItem>()
            {
                new("chaturbate.com", conf, "chu")
            };
        }

        return null;
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
        const string site = "https://ru.chaturbate.com";

        conf = ModuleInvoke.Init("Chaturbate", new SisiSettings("Chaturbate", site)
        {
            spider = false,
            httpversion = 2,
            displayindex = 24,
            rch_access = "apk,cors",
            stream_access = "apk,cors,web",

            // Resolve and fetch the signed HLS manifest from the same local
            // Lampac session. Direct Android playback often loads the first
            // segment, then loses an audio/variant playlist at the CDN.
            kit = false,
            rhub = false,
            streamproxy = true,
            headers_stream = HeadersModel.Init(
                ("User-Agent", Http.UserAgent),
                ("Referer", site + "/"),
                ("Origin", site),
                ("Accept", "application/vnd.apple.mpegurl,application/x-mpegURL,video/*,*/*;q=0.8"),
                ("Accept-Language", "en-US,en;q=0.9")
            ).ToDictionary()
        });

        // Do not let an older init/Kit section restore naked CDN playback.
        conf.kit = false;
        conf.rhub = false;
        conf.streamproxy = true;
        conf.headers_stream = HeadersModel.Init(
            ("User-Agent", Http.UserAgent),
            ("Referer", site + "/"),
            ("Origin", site),
            ("Accept", "application/vnd.apple.mpegurl,application/x-mpegURL,video/*,*/*;q=0.8"),
            ("Accept-Language", "en-US,en;q=0.9")
        ).ToDictionary();
    }
}
