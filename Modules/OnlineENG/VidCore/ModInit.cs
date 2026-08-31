using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Online.Settings;
using Shared.Services;
using System.Collections.Generic;

namespace VidCore;

/// <summary>
/// VidCore (https://vidcore.io) — embed host do CSX/CineStream dùng, có 4K.
///
/// Player của VidCore không trả m3u8 trần: token/stream bị mã hoá, và Lampac
/// giải mã qua dịch vụ `enc-dec.app` (đường dẫn đặt ở `apihost` để tự host
/// nếu một ngày dịch vụ này đổi hoặc bị gỡ — xem notes/MAPPLE-SCRAPER.md,
/// chính enc-dec đã từng gỡ endpoint `enc-mapple` và làm chết 2 nguồn khác).
///
/// `enabled: true` để VidCore vẫn hiện khi cả nhóm ENG bị `disableEng` ẩn.
/// </summary>
public class ModInit : IModuleLoaded, IModuleOnline
{
    public static OnlinesSettings conf;

    public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        var online = new List<ModuleOnlineItem>();

        // HTTP resolver — không cần Playwright/Chromium.
        bool allowWhenEngDisabled = conf?.enabled == true;
        if ((args.original_language == null || args.original_language == "en") &&
            (CoreInit.conf.disableEng == false || allowWhenEngDisabled))
        {
            if (args.source != null && (args.source is "tmdb" or "cub") && long.TryParse(args.id, out long id) && id > 0)
                online.Add(new(conf, "vidcore", "VidCore", " (ENG)"));
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
        conf = ModuleInvoke.Init("VidCore", new OnlinesSettings("VidCore", "https://vidcore.io", "https://enc-dec.app/api")
        {
            displayindex = 1016,
            kit = false,
            rhub = false,
            httptimeout = 25,
            streamproxy = true,

            // enable = true: đây là GIÁ TRỊ MẶC ĐỊNH của module, không phải bản ghi đè.
            // ModuleInvoke.Init hợp nhất section trong init.conf lên trên nó, nên anh
            // vẫn tắt được bằng "VidCore": { "enable": false }. Nếu không có dòng này
            // thì khi base.conf để disableEng:true, init.conf chưa có section VidCore
            // => BaseSettings.enable = false => IsRequestBlockedRchOrDisable trả
            // OnError("disable", 403) và KHÔNG in log nào (nguồn vẫn hiện trong
            // danh sách vì Invoke() chỉ cần conf.enabled == true).
            enable = true,
            enabled = true
        });

        conf.kit = false;
        conf.rhub = false;
        conf.httptimeout = 25;
        conf.streamproxy = true;
        conf.overridehosts ??= ["https://vidcore.io"];

        // Stream đi qua CDN của VidCore: thiếu Referer là 403.
        conf.headers_stream = HeadersModel.Init(
            ("User-Agent", Http.UserAgent),
            ("Referer", $"{conf.host.TrimEnd('/')}/"),
            ("Accept", "*/*")
        ).ToDictionary();
    }

    private string OnlineApiQuality(EventOnlineApiQuality e)
    {
        return e.balanser == "vidcore" ? " ~ 4K" : null;
    }
}
