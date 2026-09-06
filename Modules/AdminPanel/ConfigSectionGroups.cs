using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AdminPanel;

public static class ConfigSectionGroups
{
    public sealed record GroupSpec(string Id, string Title, string Hint, string[] Keys);

    // Groups mirror the Modules/ category folders in the repository so the
    // AdminPanel classification stays in sync with GitHub. Top-level runtime
    // sections are grouped by function.
    public static readonly GroupSpec[] Catalog =
    {
        new("runtime", "Hệ thống", "Các trường hệ thống; thường lấy từ current, không bắt buộc trong init.",
            new[] { "guid", "freeDiskSpace" }),
        new("listen", "Máy chủ HTTP (listen)", "Địa chỉ, cổng, giao thức, timeout, compression.",
            new[] { "listen" }),
        new("security", "Bảo mật và quyền truy cập", "WAF, accsdb, danh sách module và middleware lõi.",
            new[] { "WAF", "accsdb", "BaseModule" }),
        new("network", "Mạng, proxy và CORS", "Proxy cho request đi ra, CORS token, mạng tin cậy.",
            new[] { "serverproxy", "proxy", "globalproxy", "corsehost", "KnownProxies", "CorsMedia", "Corseu" }),

        new("pools", "Pool và hệ thống", "Buffer, APN, kit.",
            new[] { "pool", "apn", "kit" }),
        new("cache-gc", "Cache và bộ nhớ", "Hybrid cache, Staticache, cấu hình GC.",
            new[] { "cache", "Staticache", "GC" }),
        new("media", "Ảnh và poster", "Engine hình ảnh, Poster API.",
            new[] { "imagelibrary", "posterApi" }),

        new("realtime", "WebSocket và RCH", "Socket native và hub từ xa.",
            new[] { "WebSocket", "rch" }),
        new("browser", "Nguồn cần trình duyệt", "Nguồn phải chạy Chromium/Firefox (Mirage/Phantom...). Các nguồn ENG khác xem nhóm ENG.",
            new[] { "chromium", "firefox", "Mirage", "Phantom" }),
        new("diagnostics", "Log và chẩn đoán", "Serilog, xử lý exception, openstat.",
            new[] { "serilog", "useDeveloperExceptionPage", "exceptionHandlerLogTarget", "exceptionHandlerLogFile", "watcherInit", "openstat" }),

        new("app", "Ứng dụng và giao diện", "online, cub, sisi, quảng cáo, mặc định, omdb.",
            new[] { "online", "cub", "sisi", "vast", "disableEng", "defaultOn", "omdbapi_key", "overrideResponse" }),
        new("client", "Client Lampa và API", "Giao diện Lampa, cookie, TMDB, phụ đề tự động.",
            new[] { "tmdb", "LampaWeb", "SubFinder", "Cookie" }),

        new("modules", "Module dịch vụ cục bộ", "Các section module ở cấp cao nhất của config (PidTor, TorrServer, đồng bộ, DLNA, bot...).",
            new[]
            {
                "PidTor", "TorrServer", "gst", "transcoding", "Catalog", "DLNA", "JacRed",
                "Sync", "Storage", "TimeCode", "Tracks", "WebLog", "Music", "WatchTogether",
                "ProxyLimiter", "TelegramAuth", "TelegramAuthBot", "TelegramBot",
                "ForkPlayerXML", "MsxNative", "Telemetry"
            }),

        // ── Nguồn theo đúng thư mục Modules/ trên GitHub ──
        new("src-vn", "Nguồn · Việt Nam (Modules/OnlineVN)", "Các nguồn phim Việt tùy biến, không cần Playwright.",
            new[] { "K20", "KKPhim", "VsMov" }),

        new("src-eng", "Nguồn · Tiếng Anh (Modules/OnlineENG)", "Embed/Stremio tiếng Anh. Phần lớn chạy bằng embed; cần disableEng: false để hiện trong Lampa.",
            new[]
            {
                "AIOStreams", "Autoembed", "Hydraflix", "MovPI", "Playembed", "Rgshows",
                "Smashystream", "Sootio", "Twoembed", "VidCore", "VidLink", "Videasy",
                "Vidsrc", "WebStreamr"
            }),

        new("src-rus", "Nguồn · Nga và CIS (Modules/OnlineRUS)", "Nguồn VOD/CDN Nga.",
            new[]
            {
                "CDNvideohub", "Collaps", "FanCDN", "FlixCDN", "Gencit", "HDVB", "Kinobase", "Kinogo",
                "Kinotochka", "LeProduction", "PizdatoeHD", "RutubeMovie",
                "Spectre", "VeoVeo", "Vibix", "VideoDB", "Videoseed", "VkMovie", "Zetflix", "ZetflixDB"

                // Mirage/Phantom nằm ở nhóm "Nguồn cần trình duyệt" (cần Chromium).
            }),

        new("src-ukr", "Nguồn · Ukraine (Modules/OnlineUKR)", "Nguồn phim Ukraine và mirror liên quan.",
            new[] { "Ashdi", "BamBoo", "Eneyida", "HdvbUA", "Kinoukr", "Tortuga", "UAFilm", "UaKino" }),

        new("src-geo", "Nguồn · Georgia và châu Á (Modules/OnlineGEO)", "Nguồn theo khu vực Georgia/Asia.",
            new[] { "AsiaGe", "Geosaitebi", "Kinoflix" }),

        new("src-anime", "Nguồn · Anime (Modules/OnlineAnime)", "Nguồn anime online và liên quan, bao gồm Kodik.",
            new[]
            {
                "AiLiberty", "AniLiberty", "AniLibria", "AniMedia", "AnimeGo", "AnimeLib",
                "AnimeON", "Animebesst", "Animevost", "Dreamerscast", "Kodik", "Mikai", "MoonAnime"
            }),

        new("src-paid", "Nguồn · cần tài khoản hoặc token (Modules/OnlinePaid)", "Các nguồn thường cần token, cookie hoặc tài khoản riêng.",
            new[]
            {
                "Alloha", "Filmix", "FilmixPartner", "FilmixTV", "GetsTV", "IptvOnline",
                "iRemux", "KinoPub", "Rezka", "RezkaPrem", "SakhTV", "VoKino"
            }),

        new("src-adult", "Nguồn · 18+ (Modules/Adult + NextHUB)", "Module Adult viết bằng C# và site YAML NextHUB phổ biến. Các site YAML còn lại tự động vào nhóm NextHUB bên dưới.",
            new[]
            {
                "NextHUB",
                "BongaCams", "Chaturbate", "Ebalovo", "Eporner", "HQporner", "PornHub",
                "PornHubPremium", "Porntrex", "Runetki", "Spankbang", "Tizam",
                "Xhamster", "Xnxx", "Xvideos", "XvideosRED",
                "xasiat", "porn4days", "pornobolt"
            }),
    };

    // Root sections that are dead/internal and must never show in the panel.
    public static readonly HashSet<string> IgnoredRootKeys = new(StringComparer.Ordinal)
    {
        "UhdMovies" // nguồn đóng 2026-09-01; chỉ còn section rác trong current.conf
    };

    public static List<GroupDto> Build(JObject currentRoot, IEnumerable<string> nextHubSiteKeys = null)
    {
        if (currentRoot == null)
            currentRoot = new JObject();

        var inFile = new HashSet<string>(
            currentRoot.Properties().Select(p => p.Name).Where(k => !IgnoredRootKeys.Contains(k)),
            StringComparer.Ordinal);

        var assigned = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<GroupDto>();

        foreach (var g in Catalog)
        {
            var keys = g.Keys.Where(inFile.Contains).OrderBy(k => k, StringComparer.Ordinal).ToArray();
            foreach (var k in keys)
                assigned.Add(k);

            if (keys.Length == 0)
                continue;

            result.Add(new GroupDto(g.Id, g.Title, g.Hint, keys));
        }

        var nextHubKeys = (nextHubSiteKeys ?? Array.Empty<string>())
            .Where(inFile.Contains)
            .Where(k => !assigned.Contains(k))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();
        if (nextHubKeys.Length > 0)
        {
            foreach (var k in nextHubKeys)
                assigned.Add(k);

            result.Add(new GroupDto(
                "src-adult-nexthub",
                "Nguồn · NextHUB / 18+",
                "Site YAML NextHUB tự phát hiện. Có thể đổi enable, host (mirror), streamproxy và các quyền truy cập — giá trị lưu vào init.conf sẽ đè YAML.",
                nextHubKeys));
        }

        var orphans = inFile.Where(k => !assigned.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        if (orphans.Length > 0)
            result.Add(new GroupDto("other", "Khác", "Các khóa từ current.conf chưa có trong danh mục (module mới/không nhận diện).", orphans));

        return result;
    }

    public static List<GroupDto> BuildCatalog(IEnumerable<string> nextHubSiteKeys = null)
    {
        var list = new List<GroupDto>(Catalog.Length + 1);
        foreach (var g in Catalog)
            list.Add(new GroupDto(g.Id, g.Title, g.Hint, g.Keys.ToArray()));

        var nextHubKeys = (nextHubSiteKeys ?? Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();
        if (nextHubKeys.Length > 0)
        {
            list.Add(new GroupDto(
                "src-adult-nexthub",
                "Nguồn · NextHUB / 18+",
                "Site YAML NextHUB tự phát hiện. Có thể đổi enable, host (mirror), streamproxy và các quyền truy cập — giá trị lưu vào init.conf sẽ đè YAML.",
                nextHubKeys));
        }

        return list;
    }

    public static HashSet<string> CatalogRootKeys { get; } = new(
        Catalog.SelectMany(g => g.Keys),
        StringComparer.Ordinal);
}

public sealed record GroupDto(string Id, string Title, string Hint, string[] Keys);
