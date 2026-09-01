using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Online.Settings;
using Shared.Services;
using System.Collections.Generic;

namespace VidLink;

public class ModInit : IModuleLoaded, IModuleOnline
{
    public static OnlinesSettings conf;

    public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        var online = new List<ModuleOnlineItem>();

        // Trạng thái 2026-09-01: NGUỒN ĐÃ ĐÓNG, mặc định tắt. Người dùng test độc lập bằng
        // plugin CloudStream (Kotlin) cùng trang vidlink.pro và vẫn fail => đây là tường
        // "chỉ cho embed" của site (token/Referer/Origin sống chết theo session embed), không phải
        // selector hay resolver sai. Code giữ nguyên: bật lại bằng
        //   "VidLink": { "enable": true, "enabled": true }
        // trong init.conf. Mặc định tắt vì mỗi lần mở phim module lại gọi vidlink.pro với
        // httptimeout 20s — nguồn chết mà để trống thì trả giá bằng độ trễ cho mọi title.
        if (conf?.enable != true)
            return online;

        bool allowWhenEngDisabled = conf?.enabled == true;
        if (CoreInit.conf.disableEng == false || allowWhenEngDisabled)
        {
            if (args.source != null && (args.source is "tmdb" or "cub") && long.TryParse(args.id, out long id) && id > 0)
                online.Add(new(conf, "vidlink", "VidLink", " (ENG)"));
        }

        return online;
    }

    public void Loaded(InitspaceModel baseconf)
    {
        UpdateConf();
        EventListener.UpdateInitFile += UpdateConf;
        EventListener.OnlineApiQuality += OnlineApiQuality;
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= UpdateConf;
        EventListener.OnlineApiQuality -= OnlineApiQuality;
    }

    private void UpdateConf()
    {
        conf = ModuleInvoke.Init("VidLink", new OnlinesSettings("VidLink", "https://vidlink.pro")
        {
            displayindex = 1015,
            kit = false,
            rhub = false,
            httptimeout = 20,
            streamproxy = true,

            // MẶC ĐỊNH TẮT (đóng dự án 2026-09-01) — chỉ là giá trị khởi đầu, init.conf override
            // được cả hai: enable để nguồn hoạt động, enabled để hiện khi disableEng:true.
            enable = false,
            enabled = false
        });

        conf.kit = false;
        conf.rhub = false;
        conf.httptimeout = 20;
        conf.streamproxy = true;
        // Origin on CDN GETs is a common 403. Referer is enough.
        conf.headers_stream = HeadersModel.Init(
            ("User-Agent", Http.UserAgent),
            ("Referer", "https://vidlink.pro/"),
            ("Accept", "*/*")
        ).ToDictionary();
    }

    private string OnlineApiQuality(EventOnlineApiQuality e)
    {
        return e.balanser == "vidlink" ? " ~ 1080p" : null;
    }
}
