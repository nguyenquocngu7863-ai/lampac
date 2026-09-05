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
        const string browserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
        const string site = "https://ru.chaturbate.com";

        conf = ModuleInvoke.Init("Chaturbate", new SisiSettings("Chaturbate", site)
        {
            spider = false,
            httpversion = 2,
            displayindex = 24,
            rch_access = "apk,cors",
            stream_access = "apk,cors,web",

            // Do not let account/device Kit profiles replace the local proxy
            // policy with direct CDN playback. Chaturbate HLS tokens are
            // bound to the IP that requested the room page; Android cannot
            // play the mmcdn URL itself (CORS + token IP mismatch).
            kit = false,
            rhub = true,
            rhub_fallback = true,
            rhub_streamproxy = true,
            qualitys_proxy = false,
            url_reserve = false,

            streamproxy = true,
            rchstreamproxy = "web",
            headers = HeadersModel.Init(
                ("User-Agent", browserUserAgent),
                ("Referer", site + "/"),
                ("Accept-Language", "en-US,en;q=0.9")
            ).ToDictionary(),
            headers_stream = HeadersModel.Init(
                ("User-Agent", browserUserAgent),
                ("Referer", site + "/"),
                ("Origin", site),
                ("Accept", "*/*"),
                ("Accept-Language", "en-US,en;q=0.9")
            ).ToDictionary()
        });

        // Keep the live HLS proxy on even if an older init.conf/Kit profile
        // still has streamproxy=false from before this fix.
        conf.kit = false;
        conf.rhub = true;
        conf.rhub_fallback = true;
        conf.rhub_streamproxy = true;
        conf.qualitys_proxy = false;
        conf.url_reserve = false;
        conf.streamproxy = true;
    }
}
