using Newtonsoft.Json.Linq;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Stripchat;

public static class StripchatTo
{
    public static string LastError { get; private set; }

    // Real Stripchat usernames are lowercase tokens; banner/promo cards carry
    // ad copy (spaces, capitals, emoji) in the same field and get filtered out.
    static readonly Regex UsernameRx = new("^[a-z0-9_]{2,40}$", RegexOptions.Compiled);

    // Fields an actual cam-room object always carries. Marketing banners and
    // studio/promo objects miss all of them even when they expose username+id.
    static readonly string[] RoomSignals =
    {
        "hlsPlaylist", "stream", "status", "isLive", "isOnline",
        "previewUrlThumbSmall", "previewUrlThumbBig",
        "snapshotTimestamp", "popularSnapshotTimestamp"
    };

    public static string Uri(string host, string tag, int pg)
    {
        tag = NormalizeTag(tag);
        // /api/front/models (v1 listing) with the full sort/group parameter set
        // the maintained Kodi cumination addon uses. Without recInFeatured/iem/
        // decMb/ctryTop the edge node answers with the homepage feed, which is
        // full of promo/banner cards instead of rooms.
        int offset = pg > 1 ? (pg - 1) * 60 : 0;
        return $"{host}/api/front/models?removeShows=false&recInFeatured=false" +
               $"&limit=60&offset={offset}&filterGroupTags=&sortBy=stripRanking" +
               $"&parentTag=&nic=true&byw=false&rcmGrp=A&rbCnGr=true" +
               $"&iem=true&decMb=true&ctryTop=true&primaryTag={tag}";
    }

    static bool LooksLikeRoom(JObject o)
    {
        if (o?["username"]?.Value<string>() is not string uname || !UsernameRx.IsMatch(uname))
            return false;
        long? id = o.Value<long?>("id");
        if (id is null or <= 0)
            return false;
        foreach (string sig in RoomSignals)
            if (o[sig] != null)
                return true;
        return false;
    }

    public static List<PlaylistItem> Playlist(string host, ReadOnlySpan<char> json, int pg, out int totalPages)
    {
        totalPages = 0;

        try
        {
            return ParsePlaylist(host, json, pg, out totalPages);
        }
        catch (Exception ex)
        {
            totalPages = 0;
            LastError = $"parse exception: {ex.GetType().Name}: {ex.Message}";
            Console.WriteLine($"[Stripchat] {LastError}");
            return null;
        }
    }

    static List<PlaylistItem> ParsePlaylist(string host, ReadOnlySpan<char> json, int pg, out int totalPages)
    {
        totalPages = 0;

        LastError = null;
        if (json.IsEmpty)
        {
            LastError = "empty upstream response";
            Console.WriteLine("[Stripchat] empty response body from listing request");
            return null;
        }

        string raw = json.ToString();

        JObject root;
        try
        {
            root = JObject.Parse(raw);
        }
        catch (Exception ex)
        {
            // Most likely cause: Stripchat returned an HTML challenge/error page
            // instead of JSON (bot detection), so JSON.Parse blows up here.
            string preview = raw.Length > 800 ? raw[..800] : raw;
            LastError = $"not JSON: {ex.Message}";
            Console.WriteLine($"[Stripchat] JSON parse failed: {ex.Message}\n[Stripchat] raw response (first 800 chars):\n{preview}");
            return null;
        }

        // Collect model objects from every response shape Stripchat ships:
        // v1 flat `models` array, v2 `blocks[*].models`, featured
        // `items[*].model`, and a targeted fallback that only pulls objects
        // living inside arrays named "models"/"items" — never a whole-tree
        // scan, which used to pick up banner/promo cards.
        string source = null;
        var tokens = new List<JObject>();

        if (root["models"] is JArray flat && flat.Count > 0)
        {
            source = "models";
            tokens = flat.OfType<JObject>().Where(LooksLikeRoom).ToList();
        }

        if (tokens.Count == 0 && root["blocks"] is JArray blocks)
        {
            var picked = new List<JObject>();
            foreach (var block in blocks.OfType<JObject>())
                if (block["models"] is JArray blockModels)
                    picked.AddRange(blockModels.OfType<JObject>().Where(LooksLikeRoom));
            if (picked.Count > 0)
            {
                source = "blocks";
                tokens = picked;
            }
        }

        if (tokens.Count == 0 && root["items"] is JArray items)
        {
            var picked = new List<JObject>();
            foreach (var item in items.OfType<JObject>())
            {
                if (item["model"] is JObject wrapped && LooksLikeRoom(wrapped))
                    picked.Add(wrapped);
                else if (LooksLikeRoom(item))
                    picked.Add(item);
            }
            if (picked.Count > 0)
            {
                source = "items";
                tokens = picked;
            }
        }

        if (tokens.Count == 0)
        {
            var picked = new List<JObject>();
            foreach (var prop in root.Properties())
            {
                if (prop.Value is not JArray arr || prop.Name is not ("models" or "items"))
                    continue;
                picked.AddRange(arr.OfType<JObject>().Where(LooksLikeRoom));
            }
            if (picked.Count > 0)
            {
                source = "fallback";
                tokens = picked;
            }
        }

        if (tokens.Count == 0)
        {
            string preview = raw.Length > 800 ? raw[..800] : raw;
            LastError = $"JSON has no rooms; keys={string.Join(',', root.Properties().Select(i => i.Name))}";
            Console.WriteLine($"[Stripchat] no room objects found (source=none); root keys: {string.Join(',', root.Properties().Select(i => i.Name))}\n[Stripchat] raw response:\n{preview}");
            return null;
        }

        var result = new List<PlaylistItem>(60);
        var seen = new HashSet<long>();
        int skippedNotLive = 0;

        foreach (JObject model in tokens)
        {
            try
            {
                long id = model.Value<long?>("id") ?? 0;
                string username = model.Value<string>("username");
                if (id <= 0 || string.IsNullOrWhiteSpace(username) || !seen.Add(id))
                    continue;

                // The listing returns live rooms, but groupShow/private rooms are
                // not openly playable. Require an explicit live flag + public
                // status so promo/teaser/locked entries never make it to the UI.
                bool isLive = model.Value<bool?>("isLive") == true || model.Value<bool?>("isOnline") == true;
                string status = model.Value<string>("status");
                if (!isLive || status != "public")
                {
                    skippedNotLive++;
                    continue;
                }

                // Live snapshot (refreshes constantly) — prefer the timestamped
                // snapshot URL over any possibly-stale preview field.
                long? snap = model.Value<long?>("snapshotTimestamp") ?? model.Value<long?>("popularSnapshotTimestamp");
                string image = snap != null
                    ? $"https://img.doppiocdn.net/thumbs/{snap}/{id}"
                    : model.Value<string>("previewUrlThumbSmall") ?? model.Value<string>("avatarUrl");
                if (!string.IsNullOrEmpty(image) && image.StartsWith("//"))
                    image = "https:" + image;

                // The list endpoint's hlsPlaylist is only the short hover
                // PREVIEW clip (not the live room). Resolve the real live stream
                // from the per-model /cam endpoint at play time, and point the
                // item at our resolver route for that.
                string video = $"stripchat/play?u={System.Uri.EscapeDataString(username)}";

                var presets = model["presets"] is JArray presetArr
                    ? presetArr.Values<string>().Where(i => !string.IsNullOrEmpty(i)).ToArray()
                    : Array.Empty<string>();
                string quality = presets.FirstOrDefault(i => i.StartsWith("1080"))
                    ?? presets.FirstOrDefault(i => i.StartsWith("960"))
                    ?? presets.FirstOrDefault(i => i.StartsWith("720"))
                    ?? presets.FirstOrDefault(i => i.StartsWith("480"));

                result.Add(new PlaylistItem
                {
                    name = username,
                    quality = quality,
                    picture = image,
                    video = video
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Stripchat] skip model due to parse exception: {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (result.Count == 0)
        {
            // JSON parsed and we found room objects, but every single one got
            // filtered out. Log a sample so field renames are easy to diagnose.
            string sample = tokens[0].ToString(Newtonsoft.Json.Formatting.None);
            if (sample.Length > 500) sample = sample[..500];
            LastError = $"source={source}, rooms={tokens.Count}, accepted=0, sample={sample}";
            Console.WriteLine($"[Stripchat] source={source}: {tokens.Count} room-like objects, 0 passed live/public filter (skipped={skippedNotLive}).\n[Stripchat] sample object:\n{sample}");
            return null;
        }

        Console.WriteLine($"[Stripchat] source={source}: {tokens.Count} room-like objects -> {result.Count} live rooms (skipped={skippedNotLive})");

        // Prefer the server-side total for an accurate Next page; otherwise keep
        // paging while a full page came back.
        long filtered = root.Value<long?>("filteredCount") ?? 0;
        if (filtered > 0)
            totalPages = (int)Math.Min(Math.Max(1, (filtered + 59) / 60), 100);
        else
            totalPages = result.Count >= 55 ? pg + 1 : Math.Max(1, pg);
        return result;
    }

    public static bool IsValidUsername(string u)
        => !string.IsNullOrEmpty(u) && UsernameRx.IsMatch(u);

    public static string CamUri(string host, string username)
        => $"{host}/api/front/v2/models/username/{System.Uri.EscapeDataString(username)}/cam?uniq={Uniq(16)}";

    static string Uniq(int n)
    {
        var rnd = new Random();
        const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        var sb = new StringBuilder(n);
        for (int i = 0; i < n; i++)
            sb.Append(chars[rnd.Next(chars.Length)]);
        return sb.ToString();
    }

    // Pull the real live master playlist out of the per-model /cam response.
    // The list endpoint's hlsPlaylist is just a short hover preview clip;
    // this returns a {"auto": liveMasterM3u8} qualitys map, or null if the room
    // is offline/private.
    public static StreamItem LiveStream(ReadOnlySpan<char> json)
    {
        if (json.IsEmpty)
            return null;

        JObject root;
        try { root = JObject.Parse(json.ToString()); }
        catch (Exception ex)
        {
            Console.WriteLine($"[Stripchat] cam JSON parse failed: {ex.Message}");
            return null;
        }

        // cam.isCamStatus tells whether a room is actually live.
        bool? isCam = root["cam"]?.Value<bool?>("isCam");
        string camStatus = root["cam"]?.Value<string>("camStatus");
        if (isCam == false || camStatus is not (null or "public"))
            Console.WriteLine($"[Stripchat] room not live: isCam={isCam} camStatus={camStatus}");

        // Collect every absolute m3u8 URL in the payload, best first.
        var urls = new List<string>();
        void Add(string u)
        {
            if (string.IsNullOrEmpty(u) || !u.Contains("m3u8", StringComparison.OrdinalIgnoreCase))
                return;
            if (urls.Contains(u))
                return;
            urls.Add(u);
        }

        // Explicit preferred fields.
        Add(root["cam"]?["stream"]?.Value<string>("url"));
        Add(root["cam"]?.Value<string>("hlsPlaylist"));
        Add(root["cam"]?.Value<string>("hlsUrl"));
        Add(root["stream"]?.Value<string>("url"));

        // Any other m3u8 anywhere in the response.
        foreach (JToken t in root.Descendants())
        {
            if (t is JValue v && v.Type == JTokenType.String && v.Value<string>() is string s)
            {
                foreach (Match m in Regex.Matches(s, "https?://[^\\s\"'\\\\]+\\.m3u8[^\\s\"'\\\\]*"))
                    Add(m.Value);
            }
        }

        if (urls.Count == 0)
        {
            Console.WriteLine($"[Stripchat] no live m3u8 found in cam response; keys={string.Join(',', root.Descendants().OfType<JProperty>().Select(p => p.Name).Distinct().Take(40))}");
            return null;
        }

        // Prefer the edge-hls master (the actual live multi-bitrate playlist).
        string best = urls.FirstOrDefault(u => u.Contains("edge-hls", StringComparison.OrdinalIgnoreCase))
                   ?? urls.FirstOrDefault(u => u.Contains("media-hls", StringComparison.OrdinalIgnoreCase))
                   ?? urls[0];

        Console.WriteLine($"[Stripchat] live m3u8 candidates={urls.Count}, chosen={best[..Math.Min(120, best.Length)]}");
        return new StreamItem
        {
            qualitys = new Dictionary<string, string> { ["auto"] = best }
        };
    }

    public static List<MenuItem> Menu(string host, string tag)
    {
        string url(string value) => value == "girls" ? $"{host}/stripchat" : $"{host}/stripchat?tag={value}";
        return new List<MenuItem>
        {
            new MenuItem
            {
                title = $"Danh mục: {Title(tag)}",
                playlist_url = "submenu",
                submenu = new List<MenuItem>
                {
                    new("Nữ", url("girls")),
                    new("Cặp đôi", url("couples")),
                    new("Nam", url("men")),
                    new("Chuyển giới", url("trans"))
                }
            }
        };
    }

    static string NormalizeTag(string tag) => tag switch
    {
        "men" => "men",
        "trans" => "trans",
        "couples" => "couples",
        _ => "girls"
    };

    static string Title(string tag) => tag switch
    {
        "couples" => "Cặp đôi",
        "men" => "Nam",
        "trans" => "Chuyển giới",
        _ => "Nữ"
    };
}
