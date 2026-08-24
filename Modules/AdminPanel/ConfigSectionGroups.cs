using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AdminPanel;

public static class ConfigSectionGroups
{
    public sealed record GroupSpec(string Id, string Title, string Hint, string[] Keys);

    public static readonly GroupSpec[] Catalog =
    {
        new("runtime", "Hệ thống", "Các trường hệ thống; thường lấy từ current, không bắt buộc trong init.",
            new[] { "guid", "freeDiskSpace" }),
        new("listen", "Máy chủ HTTP (listen)", "Địa chỉ, cổng, giao thức, timeout, compression.",
            new[] { "listen" }),
        new("security", "Bảo mật và quyền truy cập", "WAF, accsdb, danh sách module và middleware lõi.",
            new[] { "WAF", "accsdb", "BaseModule" }),
        new("network", "Mạng và proxy", "Proxy cho request đi ra, CORS, mạng tin cậy.",
            new[] { "serverproxy", "proxy", "globalproxy", "corsehost", "KnownProxies" }),

        new("pools", "Pool và hệ thống", "Buffer, APN, kit.",
            new[] { "pool", "apn", "kit" }),
        new("cache-gc", "Cache và bộ nhớ", "Hybrid cache, Staticache, cấu hình GC.",
            new[] { "cache", "Staticache", "GC" }),
        new("media", "Ảnh và poster", "Engine hình ảnh, Poster API.",
            new[] { "imagelibrary", "posterApi" }),

        new("realtime", "WebSocket và RCH", "Socket native và hub từ xa.",
            new[] { "WebSocket", "rch" }),
        new("browser", "Trình duyệt (Playwright)", "Chromium / Firefox dùng cho tự động hóa.",
            new[] { "chromium", "firefox" }),
        new("diagnostics", "Log và chẩn đoán", "Serilog, xử lý exception, openstat.",
            new[] { "serilog", "useDeveloperExceptionPage", "exceptionHandlerLogTarget", "exceptionHandlerLogFile", "watcherInit", "openstat" }),

        new("app", "Ứng dụng và giao diện", "online, cub, sisi, quảng cáo, mặc định, omdb.",
            new[] { "online", "cub", "sisi", "vast", "disableEng", "defaultOn", "omdbapi_key", "overrideResponse" }),
        new("client", "Client Lampa và API", "Giao diện Lampa, cookie, PidTor, TMDB, phụ đề tự động.", new[] { "tmdb", "LampaWeb", "SubFinder", "Cookie", "PidTor", "gst" }),
        new("modules", "Module mở rộng", "Các section module ở cấp cao nhất của config.",
            new[] { "Catalog", "DLNA", "JacRed", "Sync", "TimeCode", "TorrServer", "Tracks", "transcoding", "TmdbProxy", "CubProxy", "WebLog" }),

        new("src-anime", "Nguồn · anime", "Nguồn anime online và liên quan (gồm Kodik).",
            new[] { "AniLiberty", "AniLibria", "Animebesst", "AnimeGo", "AnimeLib", "AnimeON", "Animevost", "AniMedia", "Dreamerscast", "Kodik", "Mikai", "MoonAnime" }),
        new("src-embed", "Nguồn · player nhúng", "Embed và aggregator player bên thứ ba.",
            new[] { "Autoembed", "Hydraflix", "MovPI", "Playembed", "Rgshows", "Smashystream", "Twoembed", "VidLink", "Videasy", "Vidsrc" }),
        new("src-vod", "Nguồn · VOD và CDN", "Phim, series, nguồn khu vực và CDN.",
            new[]
            {
                "Alloha", "Ashdi", "AsiaGe", "BamBoo", "CDNvideohub", "Collaps", "Eneyida", "FanCDN", "Filmix", "FilmixPartner", "FilmixTV", "FlixCDN",
                "Geosaitebi", "GetsTV", "HDVB", "HdvbUA", "IptvOnline", "iRemux", "Kinobase", "Kinoflix", "Kinogo", "Kinotochka", "Kinoukr", "KinoPub",
                "LeProduction", "Mirage", "Rezka", "RezkaPrem", "RutubeMovie", "Tortuga", "UaKino", "VideoDB", "Videoseed", "VeoVeo", "Vibix", "VkMovie", "VoKino",
                "WebStreamr", "K20", "OpenDirectory", "Sootio", "AIOStreams"
            }),
        new("src-adult", "Nguồn · 18+", "SISI / trang người lớn.",
            new[]
            {
                "BongaCams", "Chaturbate", "Ebalovo", "Eporner", "HQporner", "PornHub", "PornHubPremium", "Porntrex", "Runetki", "Spankbang", "Tizam",
                "Xhamster", "Xnxx", "Xvideos", "XvideosRED"
            }),
    };

    public static List<GroupDto> Build(JObject currentRoot)
    {
        if (currentRoot == null)
            currentRoot = new JObject();

        var inFile = new HashSet<string>(
            currentRoot.Properties().Select(p => p.Name),
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

        var orphans = inFile.Where(k => !assigned.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        if (orphans.Length > 0)
            result.Add(new GroupDto("other", "Khác", "Các khóa từ current.conf chưa có trong danh mục (module mới).", orphans));

        return result;
    }

    public static List<GroupDto> BuildCatalog()
    {
        var list = new List<GroupDto>(Catalog.Length);
        foreach (var g in Catalog)
            list.Add(new GroupDto(g.Id, g.Title, g.Hint, g.Keys.ToArray()));
        return list;
    }

    public static HashSet<string> CatalogRootKeys { get; } = new(
        Catalog.SelectMany(g => g.Keys),
        StringComparer.Ordinal);
}

public sealed record GroupDto(string Id, string Title, string Hint, string[] Keys);
