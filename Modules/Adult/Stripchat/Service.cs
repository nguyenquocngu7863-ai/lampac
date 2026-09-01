using Newtonsoft.Json.Linq;
using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Stripchat;

public static class StripchatTo
{
    public static string LastError { get; private set; }

    public static string Uri(string host, string tag, int pg)
    {
        tag = NormalizeTag(tag);
        // The /api/front/models (v1) listing returns a flat `models` array plus
        // `filteredCount` for real pagination. The v2 variant is also accepted
        // by the parser (blocks[*].models), but without the sort/group parameters
        // below Stripchat answers with an empty payload on many edge nodes.
        int offset = pg > 1 ? (pg - 1) * 60 : 0;
        return $"{host}/api/front/models?improveTs=false&removeShows=false" +
               $"&limit=60&offset={offset}&primaryTag={tag}" +
               "&sortBy=stripRanking&rcmGrp=A&rbCnGr=true&prxCnGr=false&nic=false";
    }

    public static List<PlaylistItem> Playlist(string host, ReadOnlySpan<char> json, int pg, out int totalPages)
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

        // Collect model objects from every response shape Stripchat currently
        // ships: v1 flat `models` array, v2 `blocks[*].models`, and the
        // featured `items[*].model` wrapper.
        var tokens = root["models"] as JArray;
        if (tokens is not { Count: > 0 })
        {
            tokens = new JArray();
            if (root["blocks"] is JArray blocks)
            {
                foreach (var block in blocks.OfType<JObject>())
                    if (block["models"] is JArray blockModels)
                        foreach (var m in blockModels)
                            tokens.Add(m);
            }
        }
        if (tokens.Count == 0)
        {
            tokens = new JArray();
            if (root["items"] is JArray items)
            {
                foreach (var item in items.OfType<JObject>())
                    if (item["model"] is JObject wrapped)
                        tokens.Add(wrapped);
            }
        }

        if (tokens.Count == 0)
        {
            // Tolerate experiments that nest/wrap the payload: a model object is
            // uniquely recognizable by username + numeric id regardless of nesting.
            var fallback = root.Descendants()
                .OfType<JObject>()
                .Where(i => i["username"] != null && i["id"] != null)
                .ToList();
            foreach (var m in fallback) tokens.Add(m);
        }

        if (tokens.Count == 0)
        {
            string preview = raw.Length > 800 ? raw[..800] : raw;
            LastError = $"JSON has no models; keys={string.Join(',', root.Properties().Select(i => i.Name))}";
            Console.WriteLine($"[Stripchat] no model objects found; raw response:\n{preview}");
            return null;
        }

        var result = new List<PlaylistItem>(60);
        var seen = new HashSet<long>();
        int skippedNotLive = 0;

        foreach (JToken model in tokens)
        {
            long id = model.Value<long?>("id") ?? 0;
            string username = model.Value<string>("username");
            if (id <= 0 || string.IsNullOrWhiteSpace(username) || !seen.Add(id))
                continue;

            // Depending on geo/experiment Stripchat sends isLive, isOnline or
            // only a status string. The listing already consists of online rooms;
            // accept anything that is not explicitly offline/private.
            bool isLive = model.Value<bool?>("isLive") == true || model.Value<bool?>("isOnline") == true;
            string status = model.Value<string>("status");
            if (!IsPublicStatus(status, isLive))
            {
                skippedNotLive++;
                continue;
            }

            string image = model.Value<string>("previewUrlThumbSmall")
                ?? model.Value<string>("avatarUrl");
            long? snap = model.Value<long?>("snapshotTimestamp") ?? model.Value<long?>("popularSnapshotTimestamp");
            if (string.IsNullOrEmpty(image) && snap != null)
                image = $"https://img.doppiocdn.net/thumbs/{snap}/{id}";
            if (!string.IsNullOrEmpty(image) && image.StartsWith("//"))
                image = "https:" + image;

            // Stream: the API hands a ready-to-play playlist URL in hlsPlaylist
            // (some variants nest it under stream.url). Fall back to the canonical
            // edge-hls master URL with the lowLatency playlist type.
            string video = model.Value<string>("hlsPlaylist");
            if (string.IsNullOrEmpty(video))
                video = model["stream"]?.Value<string>("url");
            if (string.IsNullOrEmpty(video))
                video = $"https://edge-hls.doppiocdn.net/hls/{id}/master/{id}_auto.m3u8?playlistType=lowLatency";
            if (video.StartsWith("//"))
                video = "https:" + video;
            if (video.StartsWith("http://", StringComparison.Ordinal))
                video = "https://" + video[7..];

            var presets = model["presets"]?.Values<string>().ToArray() ?? Array.Empty<string>();
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

        if (result.Count == 0)
        {
            // JSON parsed and we found model entries, but every single one got
            // filtered out. Log a sample so field renames are easy to diagnose.
            string sample = tokens[0].ToString(Newtonsoft.Json.Formatting.None);
            if (sample.Length > 500) sample = sample[..500];
            LastError = $"parsed={tokens.Count}, accepted=0, sample={sample}";
            Console.WriteLine($"[Stripchat] {tokens.Count} models parsed, 0 passed live/public filter (skipped={skippedNotLive}).\n[Stripchat] sample model:\n{sample}");
            return null;
        }

        // Prefer the server-side total for an accurate Next page; otherwise keep
        // paging while a full page came back.
        long filtered = root.Value<long?>("filteredCount") ?? 0;
        if (filtered > 0)
            totalPages = (int)Math.Min(Math.Max(1, (filtered + 59) / 60), 100);
        else
            totalPages = result.Count >= 55 ? pg + 1 : Math.Max(1, pg);
        return result;
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

    static readonly HashSet<string> privateStatuses = new()
    {
        "private", "groupShow", "p2p", "virtualPrivate", "p2pVoice", "off", "idle"
    };

    static bool IsPublicStatus(string status, bool isLive)
    {
        // Explicit private/offline statuses always exclude the room, even when
        // isLive lingers true (group shows are still "live" but not openly
        // playable). With no explicit status, trust the live flag; an online
        // listing with public status is accepted.
        if (!string.IsNullOrEmpty(status))
        {
            if (privateStatuses.Contains(status))
                return false;
            if (status != "public")
                return false;
            return true;
        }
        return isLive;
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
