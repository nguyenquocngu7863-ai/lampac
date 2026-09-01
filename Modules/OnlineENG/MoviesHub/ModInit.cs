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
/// MoviesHub — hai nguồn file-host (MoviesDrive, Movies4U) trong MỘT assembly, vì resolver
/// HubCloud/Google Drive của chúng là một (dynamic module của Lampac compile riêng từng thư
/// mục nên không thể tham chiếu chéo — muốn dùng chung thật sự thì phải ở cùng module).
///
/// Nhưng mỗi nguồn vẫn có CONFIG SECTION RIÊNG ("MoviesDrive" / "Movies4U" trong init.conf
/// và Admin Panel): host, enable, httptimeout, displayindex của từng thằng độc lập. Không
/// mượn apihost của thằng này làm host của thằng kia.
///
/// Cùng một assembly cũng là lúc cần nếu một ngày HubCloud đổi markup: sửa HubController.cs
/// một lần là cả hai nguồn theo.
/// </summary>
public class ModInit : IModuleLoaded, IModuleOnline
{
    public static OnlinesSettings drive;
    public static OnlinesSettings fouru;
    public static OnlinesSettings xd;

    public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        var online = new List<ModuleOnlineItem>();

        if (args.original_language != null && args.original_language != "en")
            return online;

        if (args.source == null || !(args.source is "tmdb" or "cub") || !long.TryParse(args.id, out long id) || id <= 0)
            return online;

        // enable = tắt nguồn đó (403 im lặng nếu Lampa vẫn gọi thẳng), enabled = nguồn có hiện
        // khi cả nhóm ENG bị disableEng ẩn. Mỗi nguồn tự quyết, không dính nhau.
        if (Allow(drive))
            online.Add(new(drive, "moviesdrive", "MoviesDrive", " (ENG)"));

        if (Allow(fouru))
            online.Add(new(fouru, "movies4u", "Movies4U", " (ENG)"));

        if (Allow(xd))
            online.Add(new(xd, "xdmovies", "XdMovies", " (ENG)"));

        // UhdMovies ĐÓNG 2026-09-01 (log test OK nhưng file của họ không ai bảo trì: link cũ không
        // stream được, site thiên về tải về). Code vẫn ở Modules/OnlineENG/MoviesHub/
        // UhdmoviesController.cs, chỉ rút khỏi manifest tree -> thiết bị không compile nữa. Nó là
        // nguyên liệu (Bypass/ResumeLink/LabelBlocks) cho XdMovies đang mở ở notes/XDMOVIES.md.

        return online;
    }

    static bool Allow(OnlinesSettings conf)
        => conf != null && (CoreInit.conf.disableEng == false || conf.enabled == true);

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

    void UpdateConf()
    {
        drive = Section("MoviesDrive", "https://new3.moviesdrive.christmas", 1017);
        fouru = Section("Movies4U", "https://new5.movies4u.clinic", 1018);
        xd = Section("XdMovies", "https://top.xdmovies.wtf", 1019);
        // XdMovies BẮT BUỘC rhub=true: trang link của họ là đếm ngược JS + Cloudflare Turnstile,
        // chỉ rch (trình thật của client) qua được. Hai hệ quả có lợi: (1) Http.Get(safety:true)
        // mới thật sự đi qua rch (RchClient.enable => init.rhub && ...); (2) IsCacheError
        // (BaseController.cs:1005) return false ngay khi rhub=true -> một lần lỗi không đầu độc
        // cả nguồn bằng 503 nữa. MoviesDrive/Movies4U giữ rhub=false như cũ.
        xd.rhub = true;
    }

    OnlinesSettings Section(string name, string host, int displayindex)
    {
        var conf = ModuleInvoke.Init(name, new OnlinesSettings(name, host)
        {
            displayindex = displayindex,
            kit = false,
            rhub = false,
            httptimeout = 30,
            streamproxy = false,   // MoviesHub phát link trần; /proxy chỉ bật cho host đòi header (VideoCore.PlayUrl)

            // Giá trị MẶC ĐỊNH — init.conf vẫn override được bằng "MoviesDrive": {"enable": false}.
            // Thiếu enable=true thì với disableEng:true, IsRequestBlockedRchOrDisable trả
            // OnError("disable", 403) và không in dòng log nào.
            enable = true,
            enabled = true
        });

        conf.kit = false;
        conf.rhub = false;
        conf.httptimeout = 30;
        conf.streamproxy = false;  // xem ghi chú ở Section(): link extractor trả sao phát vậy

        // KHÔNG set conf.overridehost/overridehosts: ở Lampac đó là cơ chế chuyển request sang
        // Lampac instance khác (IsOverridehost -> RedirectResult rồi dừng), không phải danh
        // sách domain của nguồn.

        // Referer KHÔNG đặt ở đây: link cuối thuộc mirror nào của hubcloud.* (hay
        // drive.usercontent.google.com) — HubController.StreamHeaders gắn theo từng stream.
        conf.headers_stream ??= HeadersModel.Init(
            ("User-Agent", Http.UserAgent),
            ("Accept", "*/*")
        ).ToDictionary();

        return conf;
    }

    private string OnlineApiQuality(EventOnlineApiQuality e)
    {
        if (e.balanser is "moviesdrive" or "movies4u" or "xdmovies")
            return " ~ 4K/1080p";

        return null;
    }
}
