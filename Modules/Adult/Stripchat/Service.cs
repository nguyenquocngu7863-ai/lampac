using Newtonsoft.Json.Linq;
using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Stripchat;

public static class StripchatTo
{
    public static string Uri(string host, string tag, int pg)
    {
        tag = tag switch
        {
            "men" => "men",
            "trans" => "trans",
            "couples" => "couples",
            _ => "girls"
        };
        // Stripchat rejects limit=90 with `invalid 'limit' value`; the current public
        // endpoint accepts at most 60 records per request.
        int offset = pg > 1 ? (pg - 1) * 60 : 0;
        return $"{host}/api/front/v2/models?limit=60&offset={offset}&primaryTag={tag}";
    }

    public static List<PlaylistItem> Playlist(string host, ReadOnlySpan<char> json, int pg, out int totalPages)
    {
        totalPages = 0;
        if (json.IsEmpty)
            return null;

        JObject root;
        try { root = JObject.Parse(json.ToString()); }
        catch { return null; }

        var result = new List<PlaylistItem>(60);
        var seen = new HashSet<long>();
        foreach (JToken model in root.SelectTokens("$.blocks[*].models[*]"))
        {
            long id = model.Value<long?>("id") ?? 0;
            string username = model.Value<string>("username");
            if (id <= 0 || string.IsNullOrWhiteSpace(username) || !seen.Add(id) ||
                model.Value<bool?>("isLive") != true || model.Value<string>("status") != "public")
                continue;

            string image = model.Value<string>("previewUrlThumbSmall") ?? model.Value<string>("avatarUrl");
            if (!string.IsNullOrEmpty(image) && image.StartsWith('/'))
                image = host + image;

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
                video = $"https://edge-hls.doppiocdn.net/hls/{id}/master/{id}_auto.m3u8"
            });
        }

        // The public endpoint does not consistently return a total count. Keep Next available
        // while a full page is returned; an empty following page naturally ends pagination.
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

    static string Title(string tag) => tag switch
    {
        "couples" => "Cặp đôi",
        "men" => "Nam",
        "trans" => "Chuyển giới",
        _ => "Nữ"
    };
}
