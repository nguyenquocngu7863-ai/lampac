using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace SexTb;

public static class SexTbTo
{
    public static string SiteHost = "https://sextb.net";
    public const string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";
    public static string PlayerApi => SiteHost + "/ajax/player";

    static readonly char[] Hidden = { '\uFEFF', '\u200B', '\u200C', '\u200D', '\u200E', '\u200F' };
    static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return new string(s.Where(c => !Hidden.Contains(c)).ToArray()).Trim();
    }

    // ========== Uri / Slug ==========
    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrWhiteSpace(host)) host = SiteHost;
        host = host.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(search))
        {
            string slug = Slug(search);
            if (string.IsNullOrEmpty(slug))
                slug = System.Uri.EscapeDataString(WebUtility.HtmlDecode(search).Trim().ToLowerInvariant());
            string baseUrl = host + "/search/" + slug;
            return pg > 1 ? baseUrl + "/pg-" + pg : baseUrl;
        }
        if (!string.IsNullOrWhiteSpace(c))
        {
            string raw = c.Trim();
            // c co the la "genre/amateur-xxx?genre=all&sort=viewed"
            int q = raw.IndexOf('?');
            string path = (q >= 0 ? raw[..q] : raw).Trim('/').Trim();
            string query = q >= 0 ? raw[(q + 1)..].Trim('&') : "";
            if (string.IsNullOrEmpty(path)) return host;
            string basePath = host + "/" + path;
            if (pg <= 1)
                return string.IsNullOrEmpty(query) ? basePath : basePath + "?" + query;
            // paginated: path/pg-N?query
            return string.IsNullOrEmpty(query) ? basePath + "/pg-" + pg : basePath + "/pg-" + pg + "?" + query;
        }
        return host + (pg > 1 ? "/pg-" + pg : "");
    }

    public static string Slug(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        value = WebUtility.HtmlDecode(value).Trim().ToLowerInvariant();
        value = Regex.Replace(value, @"[^a-z0-9\s-]", "");
        value = Regex.Replace(value, @"[\s_]+", "-");
        return Regex.Replace(value, @"-+", "-").Trim('-');
    }

    public static string NormalizePageUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = WebUtility.HtmlDecode(value.Trim());
        int frag = value.IndexOf('#');
        if (frag >= 0) value = value[..frag];
        if (value.StartsWith("//")) value = "https:" + value;
        else if (value.StartsWith("/")) value = SiteHost + value;
        else if (!value.StartsWith("http", StringComparison.OrdinalIgnoreCase)) value = SiteHost + "/" + value;
        if (!System.Uri.TryCreate(value, UriKind.Absolute, out var parsed)) return null;
        if (!parsed.Host.Equals("sextb.net", StringComparison.OrdinalIgnoreCase) &&
            !parsed.Host.EndsWith(".sextb.net", StringComparison.OrdinalIgnoreCase))
            return null;
        return value;
    }

    public static string NormalizeMediaUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = WebUtility.HtmlDecode(value.Trim()).Replace("\\/", "/");
        if (value.StartsWith("//")) value = "https:" + value;
        else if (value.StartsWith("/")) value = SiteHost + value;
        return value.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? value : null;
    }

    public static string HostOf(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (!System.Uri.TryCreate(url, UriKind.Absolute, out var parsed)) return null;
        return parsed.GetLeftPart(UriPartial.Authority);
    }

    // ========== Playlist ==========
    public static List<PlaylistItem> Playlist(string route, string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html)) return list;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // outer: <div class="tray-item..."><a href="..."><img ...
        var matches = Regex.Matches(html,
            @"<div\b[^>]*\bclass\s*=\s*""[^""]*\btray-item\b[^""]*""[^>]*>\s*<a\b[^>]*\bhref\s*=\s*""([^""]+)""[^>]*>\s*<img[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        foreach (Match m in matches)
        {
            string href = m.Groups[1].Value.Trim();
            // filter non-video? tray-item-list (studio) also matches but img is folder icon not thumbnail; skip via thumbnail check
            string snippet = html.Substring(m.Index, Math.Min(3000, html.Length - m.Index));
            if (!snippet.Contains("tray-item-thumbnail", StringComparison.OrdinalIgnoreCase))
                continue;
            // poster
            string poster = null;
            var pm = Regex.Match(snippet, @"<img\b[^>]*\bdata-src\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (!pm.Success)
                pm = Regex.Match(snippet, @"<img\b[^>]*\bsrc\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (pm.Success) poster = NormalizeMediaUrl(pm.Groups[1].Value);
            if (!string.IsNullOrEmpty(poster) && poster.Contains("default-cover", StringComparison.OrdinalIgnoreCase))
            {
                // genre page has src default-cover but data-src real - we already prefer data-src, so if poster is default and data-src not found -> skip poster but keep item
                // check if data-src exists separately
                var pm2 = Regex.Match(snippet, @"\bdata-src\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
                if (pm2.Success) poster = NormalizeMediaUrl(pm2.Groups[1].Value);
                else poster = null;
            }
            if (!string.IsNullOrEmpty(poster) && poster.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                poster = null;
            string pageUrl = href.StartsWith("//") ? "https:" + href : href.StartsWith("/") ? SiteHost + href : href;
            if (!seen.Add(pageUrl)) continue;
            // title
            string name = "";
            var mt = Regex.Match(snippet, @"class\s*=\s*""[^""]*tray-item-title[^""]*""[^>]*>([^<]+)<", RegexOptions.IgnoreCase);
            if (mt.Success) name = Clean(HttpUtility.HtmlDecode(mt.Groups[1].Value));
            if (string.IsNullOrEmpty(name))
            {
                var alt = Regex.Match(snippet, @"\balt\s*=\s*""([^""]{3,200})""", RegexOptions.IgnoreCase);
                if (alt.Success) name = Clean(HttpUtility.HtmlDecode(alt.Groups[1].Value));
            }
            if (string.IsNullOrEmpty(name)) continue;
            var code = Regex.Match(snippet, @"class\s*=\s*""[^""]*tray-item-code[^""]*""[^>]*>([^<]{2,40})<", RegexOptions.IgnoreCase);
            if (code.Success)
            {
                string c = Clean(HttpUtility.HtmlDecode(code.Groups[1].Value));
                if (c.Length > 0 && name.IndexOf(c, StringComparison.OrdinalIgnoreCase) < 0)
                    name = c + " " + name;
            }
            list.Add(new PlaylistItem()
            {
                video = route + "?uri=" + HttpUtility.UrlEncode(pageUrl),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Bookmark() { site = "sextb", href = pageUrl, image = poster }
            });
        }
        return list;
    }

    // ========== Detail tokens ==========
    public static (string filmId, string pt, string pk) Tokens(string html)
    {
        if (string.IsNullOrEmpty(html)) return (null,null,null);
        var id = Regex.Match(html, @"\bfilmId\s*=\s*(\d+)");
        var pt = Regex.Match(html, @"window\.__pt\s*=\s*""([a-f0-9]+)""");
        var pk = Regex.Match(html, @"window\.__pk\s*=\s*""([a-f0-9]+)""");
        if (!id.Success || !pt.Success || !pk.Success) return (null,null,null);
        return (id.Groups[1].Value, pt.Groups[1].Value, pk.Groups[1].Value);
    }

    public static Dictionary<string,string> Servers(string html, string filmId)
    {
        var res = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(html) || string.IsNullOrEmpty(filmId)) return res;
        // <button ... data-source="filmId" data-id="episode"> <i></i> LABEL</button>
        var pattern = @"data-source\s*=\s*""" + Regex.Escape(filmId) + @"""[^>]*data-id\s*=\s*""(\d+)""[^>]*>\s*(?:<i[^>]*>.*?</i>\s*)?([A-Za-z0-9]{1,4})\s*<";
        foreach (Match m in Regex.Matches(html, pattern, RegexOptions.IgnoreCase))
        {
            string label = m.Groups[2].Value.Trim();
            if (label.Length == 0 || res.ContainsKey(label)) continue;
            res[label] = m.Groups[1].Value;
        }
        // fallback: any button with data-id
        if (res.Count == 0)
        {
            foreach (Match m in Regex.Matches(html, @"<button\b[^>]*\bdata-id\s*=\s*""(\d+)""[^>]*>(.*?)</button>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                string label = Regex.Replace(m.Groups[2].Value, "<[^>]+>", " ").Trim();
                if (label.Length > 8) label = label.Substring(0,8).Trim();
                if (string.IsNullOrEmpty(label)) label = "S" + (res.Count+1);
                string key = label; int dup=2; while(res.ContainsKey(key)) key = label+" "+(dup++);
                res[key]=m.Groups[1].Value;
                if (res.Count>=8) break;
            }
        }
        return res;
    }

    public static (string html, string nextPt, string nextPk) DecryptResponse(string json, string pk)
    {
        if (string.IsNullOrWhiteSpace(json)) return (null,null,null);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return (null,null,null);
            string enc = root.TryGetProperty("player_enc", out var e) && e.ValueKind==System.Text.Json.JsonValueKind.String ? e.GetString() : null;
            string npt = root.TryGetProperty("next_pt", out var np) && np.ValueKind==System.Text.Json.JsonValueKind.String ? np.GetString() : null;
            string npk = root.TryGetProperty("next_pk", out var nk) && nk.ValueKind==System.Text.Json.JsonValueKind.String ? nk.GetString() : null;
            string html2 = XorDecrypt(enc, pk);
            // fallback plain player
            if (string.IsNullOrEmpty(html2) && root.TryGetProperty("player", out var pl) && pl.ValueKind==System.Text.Json.JsonValueKind.String)
                html2 = pl.GetString();
            return (html2, npt, npk);
        }
        catch { return (null,null,null); }
    }

    public static string XorDecrypt(string encoded, string key)
    {
        if (string.IsNullOrEmpty(encoded) || string.IsNullOrEmpty(key)) return null;
        try
        {
            byte[] data = Convert.FromBase64String(encoded);
            var sb = new System.Text.StringBuilder(data.Length);
            for (int i=0;i<data.Length;i++) sb.Append((char)(data[i] ^ key[i % key.Length]));
            return sb.ToString();
        }
        catch { return null; }
    }

    public static string IframeUrl(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        var m = Regex.Match(html, @"<iframe\b[^>]*\bsrc\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
        return NormalizeMediaUrl(m.Success ? m.Groups[1].Value : null);
    }

    // ========== Kind / Priority ==========
    public static int Priority(string label)
    {
        switch((label??"").ToUpperInvariant())
        {
            case "PM": return 0;
            case "F4": return 1;
            case "FL": return 2;
            case "SW": return 3;
            case "DD": return 4;
            case "US": return 5;
            default: return 99;
        }
    }
    public static bool IsSupported(string label) => Priority(label) < 99;
    public static string Kind(string iframe)
    {
        if (string.IsNullOrEmpty(iframe)) return null;
        if (iframe.IndexOf("f4scdn.com", StringComparison.OrdinalIgnoreCase)>=0 || iframe.IndexOf("f4stream.com", StringComparison.OrdinalIgnoreCase)>=0) return "hls";
        if (iframe.IndexOf("ryderjet.com", StringComparison.OrdinalIgnoreCase)>=0 || iframe.IndexOf("hglink.to", StringComparison.OrdinalIgnoreCase)>=0 || iframe.IndexOf("vibuxer.com", StringComparison.OrdinalIgnoreCase)>=0) return "hls";
        if (iframe.IndexOf("playmate.to", StringComparison.OrdinalIgnoreCase)>=0) return "hls";
        if (IsUpn(iframe)) return "hls";
        if (iframe.IndexOf("playmogo", StringComparison.OrdinalIgnoreCase)>=0 || iframe.IndexOf("dood", StringComparison.OrdinalIgnoreCase)>=0) return "mp4";
        return null;
    }
    public static string StreamHgUrl(string iframe)
    {
        if (string.IsNullOrEmpty(iframe)) return null;
        if (iframe.IndexOf("hglink.to", StringComparison.OrdinalIgnoreCase)>=0)
            return Regex.Replace(iframe, @"//hglink\.to/", "//vibuxer.com/", RegexOptions.IgnoreCase);
        return iframe;
    }
    public static bool IsUpn(string iframe) => !string.IsNullOrEmpty(iframe) && (iframe.IndexOf("player.upn.one",StringComparison.OrdinalIgnoreCase)>=0 || iframe.IndexOf("strp2p.com",StringComparison.OrdinalIgnoreCase)>=0 || iframe.IndexOf("upn.one",StringComparison.OrdinalIgnoreCase)>=0);
    public static string UpnId(string iframe)
    {
        if (string.IsNullOrEmpty(iframe)) return null;
        int at = iframe.IndexOf('#'); if(at<0) return null;
        string id = iframe[(at+1)..].Split('&')[0].Trim('/');
        return id.Length>1 ? id : null;
    }
    public static string UpnApi(string iframe, string id) => HostOf(iframe) + "/api/v1/video?id=" + HttpUtility.UrlEncode(id);
    public const string UpnKey = "kiemtienmua911ca";
    public const string UpnIv = "1234567890oiuytr";
    public static bool IsPlaymate(string iframe) => !string.IsNullOrEmpty(iframe) && iframe.IndexOf("playmate.to", StringComparison.OrdinalIgnoreCase)>=0;
    public static string PlaymateId(string iframe)
    {
        if (string.IsNullOrEmpty(iframe)) return null;
        var m = Regex.Match(iframe, @"/embed/([A-Za-z0-9_\-]+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    // ========== Crypto / Unpack ==========
    public static string UpnDecrypt(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        hex = hex.Trim().Trim('"');
        if (hex.Length%2!=0 || Regex.IsMatch(hex, @"[^0-9a-fA-F]")) return null;
        var data = new byte[hex.Length/2];
        for(int i=0;i<data.Length;i++) data[i]=Convert.ToByte(hex.Substring(i*2,2),16);
        try
        {
            using var aes = System.Security.Cryptography.Aes.Create();
            aes.KeySize=128; aes.Mode=System.Security.Cryptography.CipherMode.CBC; aes.Padding=System.Security.Cryptography.PaddingMode.PKCS7;
            aes.Key=System.Text.Encoding.UTF8.GetBytes(UpnKey);
            aes.IV=System.Text.Encoding.UTF8.GetBytes(UpnIv);
            using var tr=aes.CreateDecryptor();
            var plain=tr.TransformFinalBlock(data,0,data.Length);
            return System.Text.Encoding.UTF8.GetString(plain);
        }
        catch { return null; }
    }

    public static List<string> UpnSources(string json)
    {
        var res=new List<string>();
        var get=(string prop)=>{
            var m=Regex.Match(json, "\""+prop+"\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            return m.Success?m.Groups[1].Value.Replace("\\/","/"):null;
        };
        foreach(var prop in new[]{"cfNative","source","cf"})
        {
            string v=get(prop);
            if(!string.IsNullOrEmpty(v) && v.StartsWith("http",StringComparison.OrdinalIgnoreCase) && !res.Contains(v)) res.Add(v);
        }
        return res;
    }

    public static string Unpack(string html)
    {
        if(string.IsNullOrEmpty(html)) return null;
        var m=Regex.Match(html, @"eval\(function\(p,a,c,k,e,d\)\{[^}]*\}\('([\s\S]*?)',(\d+),(\d+),'([\s\S]*?)'\.split\('\|'\)\)", RegexOptions.IgnoreCase);
        if(!m.Success) return null;
        string p=m.Groups[1].Value;
        if(!int.TryParse(m.Groups[2].Value,out int a) || !int.TryParse(m.Groups[3].Value,out int c)) return null;
        var k=m.Groups[4].Value.Split('|');
        for(int i=c-1;i>=0;i--)
        {
            if(i>=k.Length||string.IsNullOrEmpty(k[i])) continue;
            p=Regex.Replace(p, @"\b"+ToBase(i,a)+@"\b", _=>k[i]);
        }
        return p;
    }
    static string ToBase(int value,int b)
    {
        if(value==0) return "0";
        var sb=new System.Text.StringBuilder();
        while(value>0){int d=value%b; sb.Insert(0,(char)(d<10?'0'+d:'a'+d-10)); value/=b;}
        return sb.ToString();
    }
    public static List<string> StreamHgMasters(string html,string playerUrl)
    {
        var res=new List<string>();
        if(string.IsNullOrEmpty(html)) return res;
        string src=Unpack(html)??html;
        var m=Regex.Match(src, @"var\s+links\s*=\s*\{([^}]{0,4000})\}", RegexOptions.IgnoreCase);
        if(!m.Success) return res;
        var kv=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(Match x in Regex.Matches(m.Groups[1].Value, @"""(hls\d)""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase))
            kv[x.Groups[1].Value]=HttpUtility.HtmlDecode(x.Groups[2].Value.Trim());
        string baseHost=HostOf(playerUrl);
        // uu tien hls3 (xxx.space/master.txt) > hls2 (cdn token) > hls4 (/stream/ can cookie file_id).
        // hls4 khong token, phu thuoc cookie -> Http.Get khong giu cookie de 403 lech IP.
        foreach(string key in new[]{"hls3","hls2","hls4"})
        {
            if(!kv.TryGetValue(key,out string v)||string.IsNullOrEmpty(v)) continue;
            if(v.StartsWith("/") && !string.IsNullOrEmpty(baseHost)) v=baseHost.TrimEnd('/')+v;
            if(v.StartsWith("http",StringComparison.OrdinalIgnoreCase)&&!res.Contains(v)) res.Add(v);
        }
        return res;
    }
    public static string StreamHgMaster(string embedHtml,string referer){ var lst=StreamHgMasters(embedHtml,referer); return lst.Count>0?lst[0]:null; }

    // ========== F4 ==========
    // F4 sinh token theo IP - phai dung cung IP voi stream (direct). Truoc dung proxy o day nhung stream direct -> token lech.
    public static async Task<string> F4SourceAsync(string embedUrl,int timeoutSeconds=10, WebProxy proxyDirect=null, int httpversion=1)
    {
        if(string.IsNullOrEmpty(embedUrl)) return null;
        string html;
        // thu direct truoc (token IP VN), fallback proxy neu direct chet
        try{ html=await Http.Get(embedUrl, timeoutSeconds: timeoutSeconds, headers: HeadersModel.Init(("User-Agent",ChromeUA),("Referer",SiteHost+"/")), httpversion: httpversion); }catch{ html=null; }
        if(string.IsNullOrEmpty(html))
        {
            try{ html=await Http.Get(embedUrl, timeoutSeconds: timeoutSeconds, headers: HeadersModel.Init(("User-Agent",ChromeUA),("Referer",SiteHost+"/")), proxy: proxyDirect, httpversion: httpversion); }catch{return null;}
        }
        if(string.IsNullOrEmpty(html)) return null;
        var m=Regex.Match(html, @"data-api\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
        if(!m.Success) return null;
        string api=m.Groups[1].Value;
        if(api.StartsWith("/")) api="https://f4stream.com"+api;
        string json;
        try{ json=await Http.Get(api, timeoutSeconds: timeoutSeconds, headers: HeadersModel.Init(("User-Agent",ChromeUA),("Referer",embedUrl)), httpversion: httpversion); }catch{ json=null; }
        if(string.IsNullOrWhiteSpace(json))
        {
            try{ json=await Http.Get(api, timeoutSeconds: timeoutSeconds, headers: HeadersModel.Init(("User-Agent",ChromeUA),("Referer",embedUrl)), proxy: proxyDirect, httpversion: httpversion); }catch{return null;}
        }
        if(string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc=System.Text.Json.JsonDocument.Parse(json);
            if(doc.RootElement.TryGetProperty("url",out var u) && u.ValueKind==System.Text.Json.JsonValueKind.String)
            {
                string v=u.GetString().Replace("\\/","/");
                if(v.StartsWith("/")) v="https://f4stream.com"+v;
                return v.StartsWith("http")?v:null;
            }
        }catch{}
        return null;
    }

    // ========== Dood / Upn / Playmate ==========
    // Dood: direct moi co pass_md5 (proxy tra captcha 5k). Sinh token IP phai cung IP voi stream.
    public static async Task<(string url,string referer)> DoodSourceAsync(string embedUrl,int timeoutSeconds=10, WebProxy proxyDirect=null, int httpversion=1)
    {
        if(string.IsNullOrEmpty(embedUrl)) return (null,null);
        string body=null;
        try{ body=await Http.Get(embedUrl, timeoutSeconds: timeoutSeconds, headers: HeadersModel.Init(("User-Agent",ChromeUA),("Referer",SiteHost+"/")), httpversion: httpversion); }catch{}
        if(string.IsNullOrEmpty(body) || !body.Contains("pass_md5"))
        {
            try{ body=await Http.Get(embedUrl, timeoutSeconds: timeoutSeconds, headers: HeadersModel.Init(("User-Agent",ChromeUA),("Referer",SiteHost+"/")), proxy: proxyDirect, httpversion: httpversion); }catch{return (null,null);}
        }
        var m=Regex.Match(body??"", @"(pass_md5/[A-Za-z0-9_\-]+/[A-Za-z0-9]+)");
        if(!m.Success) return (null,null);
        string apiHost=SiteHost;
        try{ var u=new System.Uri(embedUrl); int at=u.AbsoluteUri.IndexOf("/e/",StringComparison.OrdinalIgnoreCase); if(at>8) apiHost=u.AbsoluteUri.Substring(0,at); }catch{}
        string src=null;
        try{ src=await Http.Get(apiHost+"/"+m.Groups[1].Value+"?referer=sextb.net", timeoutSeconds: timeoutSeconds, headers: HeadersModel.Init(("User-Agent",ChromeUA),("Referer",apiHost+"/")), httpversion: httpversion); }catch{}
        if(string.IsNullOrEmpty(src) || !src.TrimStart().StartsWith("http",StringComparison.OrdinalIgnoreCase))
        {
            try{ src=await Http.Get(apiHost+"/"+m.Groups[1].Value+"?referer=sextb.net", timeoutSeconds: timeoutSeconds, headers: HeadersModel.Init(("User-Agent",ChromeUA),("Referer",apiHost+"/")), proxy: proxyDirect, httpversion: httpversion); }catch{return (null,null);}
        }
        src=(src??"").Trim();
        if(!src.StartsWith("http",StringComparison.OrdinalIgnoreCase)) return (null,null);
        string token=m.Groups[1].Value.Split('/').Last();
        long expiry=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return (src+(src.Contains('?')?"&":"?")+$"token={token}&expiry={expiry}", apiHost+"/");
    }

    public static async Task<(string url,string referer)> UpnSourceAsync(string embedUrl,int timeoutSeconds=10, WebProxy proxyDirect=null, int httpversion=1)
    {
        if(string.IsNullOrEmpty(embedUrl)) return (null,null);
        int hash=embedUrl.IndexOf('#'); if(hash<0) return (null,null);
        string id=embedUrl.Substring(hash+1).Trim().Trim('/'); int amp=id.IndexOf('&'); if(amp>=0) id=id.Substring(0,amp); if(id.Length<2) return (null,null);
        string host; try{host=new System.Uri(embedUrl).GetLeftPart(System.UriPartial.Authority);}catch{return (null,null);}
        string hex=null;
        try{hex=await Http.Get(host+"/api/v1/video?id="+System.Uri.EscapeDataString(id), timeoutSeconds: timeoutSeconds, headers: HeadersModel.Init(("User-Agent",ChromeUA),("Referer",host+"/")), httpversion: httpversion);}catch{}
        if(string.IsNullOrEmpty(hex))
        {
            try{hex=await Http.Get(host+"/api/v1/video?id="+System.Uri.EscapeDataString(id), timeoutSeconds: timeoutSeconds, headers: HeadersModel.Init(("User-Agent",ChromeUA),("Referer",host+"/")), proxy: proxyDirect, httpversion: httpversion);}catch{return (null,null);}
        }
        string json=UpnDecrypt(hex); if(string.IsNullOrEmpty(json)) return (null,null);
        try{ var doc=System.Text.Json.JsonDocument.Parse(json); var root=doc.RootElement; if(root.TryGetProperty("cfNative",out var cf)&&cf.ValueKind==System.Text.Json.JsonValueKind.String&&cf.GetString().StartsWith("http")) return (cf.GetString(), host+"/"); }catch{}
        return (null,null);
    }

    public static async Task<string> PlaymateSourceAsync(string embedUrl,int timeoutSeconds=10, WebProxy proxyDirect=null, int httpversion=1)
    {
        if(string.IsNullOrEmpty(embedUrl)) return null;
        string id=PlaymateId(embedUrl); if(string.IsNullOrEmpty(id)) return null;
        string host; try{host=new System.Uri(embedUrl).GetLeftPart(System.UriPartial.Authority);}catch{return null;}
        // PM: thu direct truoc, fallback proxy (ca 2 dang 403 hien tai nhung giu co che)
        try
        {
            string json=await Http.Post(host+"/api/s", new System.Net.Http.StringContent("{\"c\":\""+id+"\",\"d\":\"desktop\"}", System.Text.Encoding.UTF8, "application/json"), timeoutSeconds: timeoutSeconds, headers: HeadersModel.Init(("User-Agent",ChromeUA),("Referer",host+"/")), httpversion: httpversion);
            var m=Regex.Match(json??"", "\"sx\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if(m.Success)
            {
                string src=m.Groups[1].Value.Replace("\\/","/");
                if(src.StartsWith("http")) return src;
            }
        }
        catch{}
        try
        {
            string json=await Http.Post(host+"/api/s", new System.Net.Http.StringContent("{\"c\":\""+id+"\",\"d\":\"desktop\"}", System.Text.Encoding.UTF8, "application/json"), timeoutSeconds: timeoutSeconds, headers: HeadersModel.Init(("User-Agent",ChromeUA),("Referer",host+"/")), proxy: proxyDirect, httpversion: httpversion);
            var m=Regex.Match(json??"", "\"sx\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if(!m.Success) return null;
            string src=m.Groups[1].Value.Replace("\\/","/");
            return src.StartsWith("http")?src:null;
        }
        catch{return null;}
    }

    // ========== Taxonomies / Menu ==========
    public static List<(string slug,string name)> Taxonomies(string html,string prefix)
    {
        var res=new List<(string slug,string name)>();
        if(string.IsNullOrEmpty(html)||string.IsNullOrEmpty(prefix)) return res;
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // prefix like "genre/" compare lower
        prefix=prefix.Trim('/').ToLowerInvariant()+"/";
        // chi boc trong <section class="tray"> (grid chinh). Nav navbar-submenu chua caret-sub
        // toan link sort rac (Latest Updates / New Releases / ...) - phai loai.
        string scope=html;
        int sec=html.IndexOf("<section", StringComparison.OrdinalIgnoreCase);
        while(sec>=0)
        {
            int cls=html.IndexOf("tray", sec, StringComparison.OrdinalIgnoreCase);
            int end=html.IndexOf("</section>", sec, StringComparison.OrdinalIgnoreCase);
            if(cls>=0 && (end<0 || cls<end))
            {
                scope=end>=0 ? html.Substring(sec, end-sec) : html.Substring(sec);
                break;
            }
            sec=html.IndexOf("<section", sec+8, StringComparison.OrdinalIgnoreCase);
        }
        foreach(Match m in Regex.Matches(scope, @"<a[^>]*href\s*=\s*""[^""]*?/(genre|studio|label|director)/([^""#?]+)[^""]*""[^>]*>(.*?)</a>", RegexOptions.IgnoreCase|RegexOptions.Singleline))
        {
            // bo link nav (navbar / caret-sub)
            if(m.Value.IndexOf("caret-sub", StringComparison.OrdinalIgnoreCase)>=0
                || m.Value.IndexOf("navbar-", StringComparison.OrdinalIgnoreCase)>=0) continue;
            string pf=m.Groups[1].Value.ToLowerInvariant()+"/";
            if(pf!=prefix) continue;
            string slugPart=m.Groups[2].Value.Trim();
            string slug=pf+slugPart;
            string raw=m.Groups[3].Value;
            raw=Regex.Replace(raw, "<[^>]+>", " ");
            string name=Clean(HttpUtility.HtmlDecode(raw));
            if(string.IsNullOrEmpty(name))
            {
                var tit=Regex.Match(m.Value, @"title\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
                if(tit.Success) name=Clean(HttpUtility.HtmlDecode(tit.Groups[1].Value).Replace("Genre ","").Replace("Label ",""));
            }
            name=name.Replace(':', '-').Replace('|','-').Trim();
            if(string.IsNullOrEmpty(slug)||string.IsNullOrEmpty(name)||!seen.Add(slug)) continue;
            res.Add((slug, name));
        }
        return res;
    }

    static List<MenuItem> TaxonomyMenu(string host,string prefix, List<(string slug,string name)> items)
    {
        var res=new List<MenuItem>(items?.Count??0);
        foreach(var (slug,name) in items??new List<(string,string)>())
        {
            if(string.IsNullOrEmpty(slug)) continue;
            res.Add(new MenuItem(string.IsNullOrEmpty(name)?slug:name, host + "/sextb?c=" + HttpUtility.UrlEncode(slug)));
        }
        return res;
    }

    public static readonly string[] Years = { "2026","2025","2024","2023","2022","2021","2020","2019","2018","2017","2016" };
    public const string FilterQuery = "genre=all&studio=all&quality=all&year=all&sort=";

    public static List<MenuItem> Menu(string host, List<(string slug,string name)> genres, List<(string slug,string name)> studios)
    {
        host=host.TrimEnd('/');
        string url(string c)=> host + "/sextb?c=" + HttpUtility.UrlEncode(c);
        var root=new List<MenuItem>(6)
        {
            new MenuItem(){ title="Tìm kiếm", search_on="search_on", playlist_url=host+"/sextb" }
        };
        root.Add(new MenuItem(){ title="Sắp xếp", playlist_url="submenu", submenu=new List<MenuItem>(){
            new("Mới nhất", url("?sort=desc")),
            new("Ngày phát hành", url("?sort=release")),
            new("Yêu thích", url("?sort=liked")),
            new("Xem nhiều", url("?sort=viewed")),
            new("Xem nhiều (ngày)", url("?sort=viewed-day")),
            new("Xem nhiều (tuần)", url("?sort=viewed-week")),
            new("Xem nhiều (tháng)", url("?sort=viewed-month")),
        }});
        if(genres!=null && genres.Count>0)
        {
            var gm=TaxonomyMenu(host,"genre/", genres);
            // limit? keep all but bucket if >300
            if(gm.Count>300) gm=DirBuckets(host,"Thể loại", genres, 300).SelectMany(b=>b.submenu).ToList();
            root.Add(new MenuItem(){ title="Thể loại", playlist_url="submenu", submenu=gm });
        }
        if(studios!=null && studios.Count>0)
        {
            // studios 2055 -> bucket
            var buckets=DirBuckets(host,"Hãng phim", studios);
            root.AddRange(buckets);
        }
        root.Add(new MenuItem(){ title="Chất lượng", playlist_url="submenu", submenu=new List<MenuItem>(){
            new("HD", url("?quality=hd")),
            new("SD", url("?quality=sd")),
        }});
        var yearMenu=new List<MenuItem>(Years.Length);
        foreach(string y in Years) yearMenu.Add(new MenuItem(y, url($"?year={y}")));
        root.Add(new MenuItem(){ title="Năm", playlist_url="submenu", submenu=yearMenu });
        return root;
    }

    public const int MaxPerBucket=300;
    public static List<MenuItem> DirBuckets(string host,string title, IReadOnlyList<(string slug,string name)> all,int maxPer=MaxPerBucket)
    {
        var res=new List<MenuItem>();
        if(all==null||all.Count==0) return res;
        var groups=new Dictionary<char, List<(string slug,string name)>>();
        foreach(var it in all)
        {
            if(string.IsNullOrWhiteSpace(it.name)) continue;
            char c=char.ToUpperInvariant(it.name.Trim()[0]);
            if(c<'A'||c>'Z') c='#';
            if(!groups.TryGetValue(c,out var lst)){ lst=new List<(string,string)>(); groups[c]=lst; }
            lst.Add(it);
        }
        var keys=groups.Keys.OrderBy(CharRank).ToList();
        var flat=new List<(char key,string name,string path)>(all.Count);
        foreach(var k in keys) foreach(var it in groups[k]) flat.Add((k,it.name,it.slug));
        var from=new List<char>(); var to=new List<char>(); var subs=new List<List<MenuItem>>();
        for(int i=0;i<flat.Count;i+=maxPer)
        {
            int n=Math.Min(maxPer, flat.Count-i);
            from.Add(flat[i].key); to.Add(flat[i+n-1].key);
            subs.Add(flat.Skip(i).Take(n).Select(x=> new MenuItem(x.name, host + "/sextb?c=" + HttpUtility.UrlEncode(x.path))).ToList());
        }
        for(int k=0;k<from.Count;k++)
        {
            string name=title+" "+from[k]+(to[k]==from[k]?"":"–"+to[k]);
            if(to[k]==from[k])
            {
                int parts=from.Count(c=>c==from[k]);
                if(parts>1){ int nth=1; for(int q=0;q<k;q++) if(from[q]==from[k]) nth++; name+=$" ({nth}/{parts})"; }
            }
            res.Add(new MenuItem(name,"submenu"){ submenu=subs[k] });
        }
        return res;
    }
    static int CharRank(char c)
    {
        if(c=='#') return 0;
        if(c>='0'&&c<='9') return 1+(c-'0');
        if(c>='A'&&c<='Z') return 20+(c-'A');
        return 100;
    }
}
