using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace Mbbg;

public static class MbbgTo
{
    // Site doi .gay -> .hair (do 2026-10-09: list URL da la .hair, nonce
    // ajax chi hop le dung host -> POST ve .gay an 400 "0").
    public static string SiteHost = "https://mbbg.website";

    // BẮT BUỘC = `Http.UserAgent` (Chrome/146), KHÔNG tự đặt UA riêng.
    // Đo 2026-10-03 (4/4): URL `googlevideo` bị Google ràng buộc với ĐÚNG UA đã
    // dùng lúc gọi `batchexecute`:
    //   URL tạo bằng Chrome/124 -> UA124 206 | UA146 403
    //   URL tạo bằng Chrome/146 -> UA146 206 | UA124 403
    // Proxy của Lampac luôn ghi đè `user-agent` = `Http.UserAgent`
    // (Core/Middlewares/ProxyAPI.Utilities.cs:115 `Http.defaultFullHeaders`) nên
    // muốn phát được thì cả chuỗi phải xài UA đó từ đầu.
    public static string ChromeUA = Http.UserAgent;

    // WordPress, SSR that, 20 phim/trang. Do 2026-10-03:
    //   trang chu : /page/N                 (khong co / cuoi — co / thi timeout)
    //   danh muc   : /<slug>/page/N          (do: trang 1 vs 2 -> 0 phim trung)
    //   tag        : /tag/<slug>             (max 20 phim/tag nen LUON 1 trang)
    //   tim kiem   : /?s=<kw>&paged=N        (DUNG `paged` — `&page=2` bi BO QUA, da do
    //                                        tra ve dung 20 phim cua trang 1)
    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        host = host.TrimEnd('/');

        if (pg < 1)
            pg = 1;

        if (!string.IsNullOrWhiteSpace(search))
        {
            string u = host + "/?s=" + HttpUtility.UrlEncode(search.Trim());
            return pg > 1 ? u + "&paged=" + pg : u;
        }

        if (!string.IsNullOrWhiteSpace(c))
        {
            string p = "/" + c.Trim('/');
            return pg > 1 ? host + p + "/page/" + pg : host + p;
        }

        return pg > 1 ? host + "/page/" + pg : host + "/";
    }

    // <article class="loop-video thumb-block post-13712 ...">
    //   <a href="https://mbbg.gay/<slug>.html" title="TIEU DE">
    //     <div class="post-thumbnail">
    //       <div class="post-thumbnail-container">
    //         <img width="300" data-src="https://mbbg.gay/wp-content/uploads/...jpg" alt="...">
    static readonly Regex _itemRe = new Regex(
        "<a[^>]*href=\"(?<url>[^\"]+\\.html)\"[^>]*title=\"(?<title>[^\"]*)\"[^>]*>",
        RegexOptions.IgnoreCase);

    public static List<PlaylistItem> Playlist(string uri, string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in _itemRe.Matches(html))
        {
            string href = m.Groups["url"].Value.Trim();
            if (!seen.Add(href))
                continue;

            int at = m.Index;
            string tail = html.Substring(at, Math.Min(1800, html.Length - at));

            // Khong co `post-thumbnail` = khong phai card (bai lien quan, sidebar)
            if (tail.IndexOf("post-thumbnail", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            string name = HttpUtility.HtmlDecode(m.Groups["title"].Value).Trim();
            if (name.Length == 0)
                continue;

            // Poster lazyload: data-src truoc, moi den src binh thuong sau
            string poster = "";
            var im = Regex.Match(tail, "data-src=\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (im.Success)
            {
                poster = im.Groups[1].Value;
            }
            else
            {
                var ms = Regex.Match(tail, "(?<![\\w-])src=\"([^\"]+)\"",
                    RegexOptions.IgnoreCase);
                if (ms.Success)
                    poster = ms.Groups[1].Value;
            }

            list.Add(new PlaylistItem()
            {
                video = uri + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Bookmark()
                {
                    site = "mbbg",
                    href = href,
                    image = poster
                }
            });
        }

        return list;
    }

    // Nav doc dung truoc the <article dau tien (doan nay chi co menu),
    // nen khong bat phai lien ket `.html` / `tag/` / `page/` cua sidebar.
    public static List<(string name, string path)> Genres(string html)
    {
        var res = new List<(string name, string path)>();
        if (string.IsNullOrEmpty(html))
            return res;

        int cut = html.IndexOf("class=\"loop-video\"", StringComparison.OrdinalIgnoreCase);
        string nav = cut > 0 ? html.Substring(0, cut) : html;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(nav,
            "href=\"https?://[^\"/]+/(?<slug>[a-z0-9\\-]+)\"[^>]*>\\s*"
            + "(?<name>[^<]{1,40}?)\\s*</a>", RegexOptions.IgnoreCase))
        {
            string slug = m.Groups["slug"].Value;
            if (!seen.Add(slug))
                continue;

            string name = HttpUtility.HtmlDecode(m.Groups["name"].Value).Trim();
            if (name.Length == 0)
                continue;

            res.Add((name, slug));
        }

        return res;
    }

    // Footer/tag cloud: <a href="https://mbbg.gay/tag/<slug>">Tên</a> — 50 the.
    public static List<(string name, string path)> Tags(string html)
        => Slugs(html, "tag");

    static List<(string name, string path)> Slugs(string html, string kind)
    {
        var res = new List<(string name, string path)>();
        if (string.IsNullOrEmpty(html))
            return res;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var re = new Regex("href=\"https?://[^\"/]*/" + kind
            + "/(?<slug>[a-z0-9\\-]+)\"[^>]*>\\s*(?<name>[^<]{1,60}?)\\s*</a>",
            RegexOptions.IgnoreCase);

        foreach (Match m in re.Matches(html))
        {
            string slug = m.Groups["slug"].Value;
            if (!seen.Add(slug))
                continue;

            string name = HttpUtility.HtmlDecode(m.Groups["name"].Value).Trim();
            if (name.Length == 0)
                continue;

            res.Add((name, kind + "/" + slug));
        }

        return res;
    }

    // Fallback tinh lay dung theo nav/footer hien tai (do 2026-10-03).
    public static readonly IReadOnlyList<(string name, string path)> FallbackGenres =
        new (string, string)[]
        {
            ("Việt Nam",        "sex-viet-nam"),
            ("Trung Quốc",      "sex-chinas"),
            ("Rau non",         "rau-non"),
            ("TikTok 18+",      "tiktok-18"),
            ("Máy bay bà già",  "may-bay-ba-gia"),
            ("Ngoại Tình",      "ngoai-tinh"),
            ("Tự Quay",         "tu-quay"),
        };

    public static readonly IReadOnlyList<(string name, string path)> FallbackTags =
        new (string, string)[]
        {
            ("viet69", "tag/viet69"), ("xuất tinh", "tag/xuat-tinh"),
            ("mông to", "tag/mong-to"), ("lồn nhiều nước", "tag/lon-nhieu-nuoc"),
            ("Đá trứng cút", "tag/da-trung-cut"), ("rên la", "tag/ren-la"),
            ("đồ ngủ ren", "tag/do-ngu-ren"), ("VLXX", "tag/vlxx"),
            ("lọt khe tím", "tag/lot-khe-tim"), ("địt cả 2 lỗ", "tag/dit-ca-2-lo"),
            ("đụ 2 lỗ", "tag/du-2-lo"), ("travelvids", "tag/travelvids"),
            ("dập như máy khâu", "tag/dap-nhu-may-khau"), ("loli việt nam", "tag/loli-viet-nam"),
            ("địt xã đồ", "tag/dit-xa-do"), ("địt gái xăm mình", "tag/dit-gai-xam-minh"),
            ("Móc bím", "tag/moc-bim"), ("tét đỏ mông", "tag/tet-do-mong"),
            ("chịch em", "tag/chich-em"), ("Chơi lỗ nhị", "tag/choi-lo-nhi"),
            ("đụ nát lồn", "tag/du-nat-lon"), ("không bao", "tag/khong-bao"),
            ("chongtoico", "tag/chongtoico"), ("mbbg", "tag/mbbg"),
            ("HoaNgocLan4444", "tag/hoangoclan4444"), ("Địt khẩu dâm", "tag/dit-khau-dam"),
            ("bóp vú", "tag/bop-vu"), ("doggy", "tag/doggy"),
            ("máy bay 90", "tag/may-bay-90"), ("Đụ lỗ đít", "tag/du-lo-dit"),
            ("tây trọc travelvids", "tag/tay-troc-travelvids"), ("tư thế truyền thống", "tag/tu-the-truyen-thong"),
            ("vợ chồng vĩnh long", "tag/vo-chong-vinh-long"), ("xuất tinh ngập bím", "tag/xuat-tinh-ngap-bim"),
            ("bé thỏ vĩnh long", "tag/be-tho-vinh-long"), ("Địt em fwb", "tag/dit-em-fwb"),
            ("mình dây", "tag/minh-day"), ("Chơi lỗ đít", "tag/choi-lo-dit"),
            ("Gạ chịch", "tag/ga-chich"), ("vỗ đỏ mông", "tag/vo-do-mong"),
            ("váy ren", "tag/vay-ren"), ("đụ không bao", "tag/du-khong-bao"),
            ("vú bự", "tag/vu-bu"), ("Hoa Ngọc Lan", "tag/hoa-ngoc-lan"),
            ("cosplay", "tag/cosplay"), ("doggy banh lồn", "tag/doggy-banh-lon"),
            ("vú to", "tag/vu-to"), ("chịch loli", "tag/chich-loli"),
            ("SieuKhung", "tag/sieukhung"), ("xuất trong", "tag/xuat-trong"),
        };

    // Dong goc khong co submenu = chet (muc 9c) nen "Mới nhất" co 1 con.
    // Site khong co kieu xep hang -> khong dong "Sắp xếp".
    public static List<MenuItem> Menu(
        string host,
        IReadOnlyList<(string name, string path)> genres = null,
        IReadOnlyList<(string name, string path)> tags = null)
    {
        var root = new List<MenuItem>()
        {
            new MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = host + "/mbbg"
            }
        };

        if (genres != null && genres.Count > 0)
        {
            var sub = new List<MenuItem>();
            foreach (var g in genres)
                sub.Add(new MenuItem(g.name, host + "/mbbg?c=" + HttpUtility.UrlEncode(g.path)));
            root.Add(new MenuItem() { title = "Thể loại", playlist_url = "submenu", submenu = sub });
        }

        if (tags != null && tags.Count > 0)
        {
            var sub = new List<MenuItem>();
            foreach (var t in tags)
                sub.Add(new MenuItem(t.name, host + "/mbbg?c=" + HttpUtility.UrlEncode(t.path)));
            root.Add(new MenuItem() { title = "Từ khoá", playlist_url = "submenu", submenu = sub });
        }

        root.Add(new MenuItem()
        {
            title = "Mới nhất",
            playlist_url = "submenu",
            submenu = new List<MenuItem>() { new MenuItem("Mới Nhất", host + "/mbbg") }
        });

        return root;
    }

    #region resolve — cong thuc F14 (skill lampac-deobfuscate)

    //   var wpst_ajax_var = {"url":"…/admin-ajax.php","nonce":"e2bac8bff9",…}
    public static string Nonce(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var m = Regex.Match(html, "\"nonce\"\\s*:\\s*\"([0-9a-f]{6,})\"",
            RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    //   var sources=["https:\/\/s63.xvideo.lat\/embed\/<b64>.html"];
    // Tra ve NGUYEN mang JSON `["…"]` (da gom dau ngoac) de dua vao getplayer.
    public static string Sources(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var m = Regex.Match(html, "var\\s+sources=\\[(?<inner>[\\s\\S]*?)\\];",
            RegexOptions.IgnoreCase);
        if (m.Success && m.Groups["inner"].Value.Trim().Length > 0)
            return "[" + m.Groups["inner"].Value + "]";

        // Theme 2026-11: khong con `var sources` — player nam o
        // <meta itemprop="contentURL" content="https://.../embed/<b64>.html">
        var cu = Regex.Match(html, "\"contentURL\"\\s+content=\"([^\"]+)\"",
            RegexOptions.IgnoreCase);
        if (cu.Success && cu.Groups[1].Value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return "[" + JsonSerializer.Serialize(cu.Groups[1].Value) + "]";

        return null;
    }

    //   {"success":true,"data":{"sources":{"file":"https://www.blogger.com/video.g?token=…"}}}
    public static string FileFrom(string json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        var m = Regex.Match(json, "\"file\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Replace("\\/", "/") : null;
    }

    // https://www.blogger.com/video.g?token=<TOKEN>&origin=  -> <TOKEN>
    public static string Token(string url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        var m = Regex.Match(url, @"[?&]token=([^&]+)", RegexOptions.IgnoreCase);
        return m.Success ? HttpUtility.UrlDecode(m.Groups[1].Value) : null;
    }

    public static string BatchUrl =
        "https://www.blogger.com/_/BloggerVideoPlayerUi/data/batchexecute"
        + "?rpcids=WcwnYd&source-path=%2Fvideo.g&hl=en-US&rt=c";

    // Body cua batchexecute: f.req=[[[["WcwnYd","[\"<TOKEN>\",null,0]",null,"generic"]]]]&
    public static string BatchBody(string token)
    {
        string inner = "[\"" + token + "\",null,0]";
        string fReq = "[[[\"WcwnYd\"," + JsonSerializer.Serialize(inner)
            + ",null,\"generic\"]]]";
        return "f.req=" + HttpUtility.UrlEncode(fReq) + "&";
    }

    // Tra ve XSSI:
    //   )]}'
    //
    //   <len>
    //   [["wrb.fr","WcwnYd","<json dang string>"], … ]  <— con chu JSON sau nua
    // => lay tu ky tu `[` dau tien (khong bi dai du lieu thua lam loi parse),
    // sau do parse lan 2 vi phan noi dung la mot chuoi JSON.
    //
    // inner = [1, null, [[url,[itag]], [url,[itag]]], thumb, "BLOGGER-video-…", id]
    //   itag 18 = 360p, itag 22 = 720p — ca hai la MP4 goc.
    public static Dictionary<string, string> Formats(string resp)
    {
        var res = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(resp))
            return res;

        try
        {
            int p = resp.IndexOf(")]}'", StringComparison.Ordinal);
            string body = p >= 0 ? resp.Substring(p + 4) : resp;

            int s = body.IndexOf('[');
            if (s < 0)
                return res;
            body = body.Substring(s);

            var rd = new Utf8JsonReader(Encoding.UTF8.GetBytes(body));
            using var root = JsonDocument.ParseValue(ref rd);

            if (root.RootElement.ValueKind != JsonValueKind.Array
                || root.RootElement.GetArrayLength() == 0)
                return res;

            string inner = root.RootElement[0][2].GetString();
            if (string.IsNullOrEmpty(inner))
                return res;

            var rd2 = new Utf8JsonReader(Encoding.UTF8.GetBytes(inner));
            using var doc = JsonDocument.ParseValue(ref rd2);

            var fmts = doc.RootElement[2];
            foreach (var it in fmts.EnumerateArray())
            {
                if (it.ValueKind != JsonValueKind.Array || it.GetArrayLength() < 2)
                    continue;

                string url = it[0].GetString();
                int itag = it[1][0].GetInt32();

                if (string.IsNullOrEmpty(url))
                    continue;

                res[Label(itag)] = url;
            }
        }
        catch { }

        return res;
    }

    // Mirror qooglevideo (khi video Blogger goc chet, site doi file sang iframe
    // mirror — do 2026-10-09): trang embed co san sources:
    // [{"file":"...hls.m3u8","type":"hls",...},{"file":"...mp4",...}].
    public static Dictionary<string, string> QoogleSources(string html)
    {
        var res = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(html))
            return res;
        foreach (Match m in Regex.Matches(html,
            @"\{[^{}]*?""file""\s*:\s*""([^""]+)""[^{}]*?\}",
            RegexOptions.IgnoreCase))
        {
            string block = m.Value;
            string url = Unenc(m.Groups[1].Value).Replace("\\/", "/").Trim();
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                continue;
            var tm = Regex.Match(block, @"""type""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);
            var lm = Regex.Match(block, @"""label""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);
            string type = tm.Success ? tm.Groups[1].Value : "";
            bool hls = type.IndexOf("mpegurl", StringComparison.OrdinalIgnoreCase) >= 0
                || type.IndexOf("hls", StringComparison.OrdinalIgnoreCase) >= 0
                || url.IndexOf(".m3u8", StringComparison.OrdinalIgnoreCase) >= 0;
            string label = lm.Success ? HttpUtility.HtmlDecode(lm.Groups[1].Value).Trim() : "";
            string key = hls ? "HLS" : (string.IsNullOrEmpty(label) ? "MP4" : label);
            if (!res.ContainsKey(key))
                res.Add(key, url);
        }
        return res;
    }

    // Mot so embed (uproxy) boc link bang `window._0x_dec`: "enc.<hex>" voi hex
    // bi DAO NGUOC, giai hex ra byte roi XOR tung byte voi key "o_X1_2Gj".
    static string Unenc(string url)
    {
        if (string.IsNullOrEmpty(url)
            || !url.StartsWith("enc.", StringComparison.OrdinalIgnoreCase))
            return url;

        var ch = url.Substring(4).Replace(".", "").ToCharArray();
        if (ch.Length < 2 || ch.Length % 2 != 0)
            return url;

        Array.Reverse(ch);

        const string key = "o_X1_2Gj";
        var sb = new StringBuilder(ch.Length / 2);

        for (int i = 0; i + 1 < ch.Length; i += 2)
        {
            if (!byte.TryParse(new string(new[] { ch[i], ch[i + 1] }),
                NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
                return url;

            sb.Append((char)(b ^ key[(i / 2) % key.Length]));
        }

        return sb.ToString();
    }

    static string Label(int itag)
    {
        if (itag == 22) return "720p";
        if (itag == 18) return "360p";
        return "itag " + itag;
    }

    public static string HostOf(string url)
    {
        if (string.IsNullOrEmpty(url)) return SiteHost;
        if (!System.Uri.TryCreate(url, UriKind.Absolute, out var parsed)) return SiteHost;
        return parsed.GetLeftPart(System.UriPartial.Authority);
    }

    #endregion

    #region popup long-press (skill lampac-popup)

    // Trang chi tiet:
    //   <article id="post-15474" class="post-15474 … category-phim-sex-trung-quoc">
    //   <div class="tags"><div class="tags-list">
    //     <a href="https://mbbg.blog/phim-sex-trung-quoc" title="Phim Sex Trung Quốc">
    // Ten that lay tu `title` (khong doan tu slug).
    public static List<(string kind, string slug, string name)> Chips(string html)
    {
        var res = new List<(string kind, string slug, string name)>();
        if (string.IsNullOrEmpty(html))
            return res;

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var block = Regex.Match(html, "<div class=\"tags\">([\\s\\S]*?)</div>\\s*</div>",
            RegexOptions.IgnoreCase);

        if (block.Success)
            foreach (Match a in Regex.Matches(block.Groups[1].Value,
                "href=\"(?<href>[^\"]+)\"[^>]*title=\"(?<title>[^\"]*)\"",
                RegexOptions.IgnoreCase))
            {
                string path = PathOf(a.Groups["href"].Value);
                if (path.Length == 0)
                    continue;

                names[path] = HttpUtility.HtmlDecode(a.Groups["title"].Value).Trim();
            }

        // Lop `category-`/`tag-` cua chinh bai (article dau tien, khong loop-video).
        var post = Regex.Match(html,
            "<article\\s+id=\"post-\\d+\"[^>]*class=\"(?<cls>[^\"]*)\"", RegexOptions.IgnoreCase);

        if (!post.Success)
            post = Regex.Match(html, "<article[^>]*class=\"(?<cls>loop-video|post-)[^\"]*\"",
                RegexOptions.IgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string kind, string slug, string path)
        {
            if (string.IsNullOrEmpty(slug) || !seen.Add(kind + "/" + slug))
                return;

            string name = names.TryGetValue(path, out string n) && n.Length > 0 ? n : slug;
            res.Add((kind, slug, name));
        }

        foreach (string cls in post.Groups["cls"].Value.Split(
            new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (cls.StartsWith("category-", StringComparison.OrdinalIgnoreCase))
                Add("cat", cls.Substring(9), "/" + cls.Substring(9));
            else if (cls.StartsWith("tag-", StringComparison.OrdinalIgnoreCase))
                Add("tag", cls.Substring(4), "/tag/" + cls.Substring(4));
        }

        // Link trong .tags-list ma lop article khong ke (vd the chi co trong block).
        foreach (var kv in names)
            Add(kv.Key.StartsWith("/tag/", StringComparison.OrdinalIgnoreCase)
                ? "tag" : "cat",
                kv.Key.StartsWith("/tag/", StringComparison.OrdinalIgnoreCase)
                    ? kv.Key.Substring(5) : kv.Key.TrimStart('/'),
                kv.Key);

        return res;
    }

    // "Phim Sex Diary Hay Nhất" — chi lay card trong khoi duoi video, khong lay
    // sidebar/bai lien quan khac ( chung cung la .loop-video).
    public static List<PlaylistItem> Related(string uri, string html)
    {
        if (string.IsNullOrEmpty(html))
            return new List<PlaylistItem>();

        int cut = html.IndexOf("under-video-block", StringComparison.OrdinalIgnoreCase);
        return Playlist(uri, cut > 0 ? html.Substring(cut) : "");
    }

    // https://mbbg.blog/phim-sex-trung-quoc  -> /phim-sex-trung-quoc
    // https://mbbg.blog/tag/viet69           -> /tag/viet69
    static string PathOf(string href)
    {
        if (string.IsNullOrEmpty(href))
            return "";

        string s = href.Trim();

        int p = s.IndexOf("://", StringComparison.Ordinal);
        if (p > 0)
        {
            int slash = s.IndexOf('/', p + 3);
            s = slash > 0 ? s.Substring(slash) : "/";
        }

        if (!s.StartsWith("/") || s.Length < 2)
            return "";

        return s.TrimEnd('/');
    }

    #endregion
}
