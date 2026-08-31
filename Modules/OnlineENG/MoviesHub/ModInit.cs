using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Online.Settings;
using Shared.Services;
using System.Collections.Generic;

namespace MoviesHub;

/// <summary>
/// MoviesHub — hai nguồn file-host (Google Drive / HubCloud) trong một module:
///   * MoviesDrive (new3.moviesdrive.christmas) — search theo IMDb id, mỗi quality là
///     một link HubCloud `…/drive/search-recover.php?from_ac=…&q=<base64 tên file>`.
///   * Movies4U (new5.movies4u.clinic) — WordPress search `?s=<title>+<year>` với
///     `Cookie: xla=s4t`, link nằm trong `div.download-links-div a.btn`.
///
/// Cả hai đều dừng ở file-host, nên phần khó (file page -> URL chơi được) nằm trong
/// HubController và DÙNG CHUNG. Không Playwright, không enc-dec: regex + redirect.
///
/// `host`  = MoviesDrive, `apihost` = Movies4U (theo đúng nghĩa 2 key của OnlinesSettings,
/// đổi được trong init.conf vì domain nhóm này đổi gần như mỗi tuần).
/// </summary>
public class ModInit : IModuleLoaded, IModuleOnline
{
    public static OnlinesSettings conf;

    public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        var online = new List<ModuleOnlineItem>();

        // enabled: true -> hiện cả khi disableEng bật (giống VidCore/VidLink).
        bool allowWhenEngDisabled = conf?.enabled == true;
        if ((args.original_language == null || args.original_language == "en") &&
            (CoreInit.conf.disableEng == false || allowWhenEngDisabled))
        {
            if (args.source != null && (args.source is "tmdb" or "cub") && long.TryParse(args.id, out long id) && id > 0)
            {
                online.Add(new(conf, "moviesdrive", "MoviesDrive", " (ENG)"));
                online.Add(new(conf, "movies4u", "Movies4U", " (ENG)"));
            }
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
        conf = ModuleInvoke.Init("MoviesHub", new OnlinesSettings("MoviesHub", "https://new3.moviesdrive.christmas", "https://new5.movies4u.clinic")
        {
            displayindex = 1017,
            kit = false,
            rhub = false,
            httptimeout = 30,
            streamproxy = true,

            // Giá trị MẶC ĐỊNH (init.conf vẫn override được bằng "MoviesHub": {"enable": false}).
            // Thiếu dòng này thì với disableEng:true, IsRequestBlockedRchOrDisable trả
            // OnError("disable", 403) và im lặng — nguồn hiện trong list nhưng không play được.
            enable = true,
            enabled = true
        });

        conf.kit = false;
        conf.rhub = false;
        conf.httptimeout = 30;
        conf.streamproxy = true;

        // KHÔNG set conf.overridehost/overridehosts: đó là cơ chế chuyển request sang Lampac
        // khác (IsOverridehost -> RedirectResult rồi dừng), không phải "danh sách domain".

        // Referer KHÔNG đặt ở đây: link cuối thuộc hubcloud.foo|cx|… (mirror xoay vòng) hoặc
        // drive.usercontent.google.com, nên Referer được gắn theo TỪNG stream trong
        // HubController — đặt tĩnh một host là 403 cho các mirror còn lại.
        conf.headers_stream ??= HeadersModel.Init(
            ("User-Agent", Http.UserAgent),
            ("Accept", "*/*")
        ).ToDictionary();
    }

    private string OnlineApiQuality(EventOnlineApiQuality e)
    {
        if (e.balanser is "moviesdrive" or "movies4u")
            return " ~ 4K/1080p";

        return null;
    }
}
