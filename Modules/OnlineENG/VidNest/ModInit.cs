using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Online.Settings;
using Shared.Services;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace VidNest;

public class ModInit : IModuleLoaded, IModuleOnline
{
    public static OnlinesSettings conf;

    public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        var online = new List<ModuleOnlineItem>();

        bool allowWhenEngDisabled = conf?.enabled == true;
        if ((args.original_language == null || args.original_language == "en") &&
            (CoreInit.conf.disableEng == false || allowWhenEngDisabled))
        {
            if (args.source != null && (args.source is "tmdb" or "cub") && long.TryParse(args.id, out long id) && id > 0)
                online.Add(new(conf, "vidnest", "VidNest", " (ENG)"));
        }

        return online;
    }

    public void Loaded(InitspaceModel baseconf)
    {
        UpdateConf();
        EventListener.UpdateInitFile += UpdateConf;
        EventListener.OnlineApiQuality += OnlineApiQuality;
        EventListener.VideoTpl += VideoTplHls;
        EventListener.ProxyApiCreateHttpRequest += ForceHttp2;
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= UpdateConf;
        EventListener.OnlineApiQuality -= OnlineApiQuality;
        EventListener.VideoTpl -= VideoTplHls;
        EventListener.ProxyApiCreateHttpRequest -= ForceHttp2;
    }

    // goodstream.cc tra 403 + challenge cho request HTTP/1.1 (da kiem chung:
    // curl mac dinh h2 thi 200, curl --http1.1 va python thi 403). HttpClient
    // cua proxy mac dinh dung h1.1 nen ep len h2 cho moi request toi host nay.
    static Task ForceHttp2(EventProxyApiCreateHttpRequest e)
    {
        try
        {
            if (e?.uri?.Host?.Contains("goodstream.cc") == true && e.requestMessage != null)
                e.requestMessage.Version = HttpVersion.Version20;
        }
        catch { }

        return Task.CompletedTask;
    }

    private void UpdateConf()
    {
        conf = ModuleInvoke.Init("VidNest", new OnlinesSettings("VidNest", "https://new.vidnest.fun")
        {
            displayindex = 1023,
            kit = false,
            rhub = false,
            httptimeout = 20,
            streamproxy = true
        });

        conf.kit = false;
        conf.rhub = false;
        conf.httptimeout = 20;
        conf.streamproxy = true;
        conf.headers_stream ??= HeadersModel.Init(
            ("User-Agent", Http.UserAgent),
            ("Referer", "https://vidnest.fun/"),
            ("Origin", "https://vidnest.fun")
        ).ToDictionary();
    }

    private string OnlineApiQuality(EventOnlineApiQuality e)
    {
        return e.balanser == "vidnest" ? " ~ 720p" : null;
    }

    // URL proxy khong co duoi .m3u8 nen player trong cua app khong nhan ra HLS
    // (bao "no supported source"). Chen hls_type de client ep hls.js.
    private string VideoTplHls(EventVideoTpl e)
    {
        try
        {
            if (e?.httpContext?.Request?.Path.Value?.Contains("vidnest") != true)
                return null;

            string json = System.Text.Json.JsonSerializer.Serialize(
                e.video, Shared.Models.Templates.VideoJsonContext.Default.VideoDto);

            if (string.IsNullOrWhiteSpace(json) || !json.EndsWith("}"))
                return null;

            return json.Substring(0, json.Length - 1) + ",\"hls_type\":\"hlsjs\"}";
        }
        catch
        {
            return null;
        }
    }
}
