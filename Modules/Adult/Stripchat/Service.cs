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

        var tokens = root.SelectTokens("$.blocks[*].models[*]").ToList();
        if (tokens.Count == 0)
        {
            // Tolerate experiments that wrap/reorder blocks: a model object is uniquely
            // recognizable by username + numeric id, regardless of its JSON nesting.
            tokens = root.Descendants()
                .OfType<JObject>()
                .Where(i => i["username"] != null && i["id"] != null)
                .Cast<JToken>()
                .ToList();
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

            // Depending on the geo/experiment block Stripchat sends either isLive or
            // isOnline. The listing itself contains online rooms, so accept either flag.
            bool isLive = model.Value<bool?>("isLive") == true || model.Value<bool?>("isOnline") == true;
            string status = model.Value<string>("status");
            if (!isLive || (!string.IsNullOrEmpty(status) && status != "public"))
            {
                skippedNotLive++;
                continue;
            }

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

        if (result.Count == 0)
        {
            // JSON parsed and we found model entries, but every single one got
            // filtered out by the isLive/public check. Log a sample so we can see
            // whether Stripchat renamed these fields or changed their values.
            string sample = tokens[0].ToString(Newtonsoft.Json.Formatting.None);
            if (sample.Length > 500) sample = sample[..500];
            LastError = $"parsed={tokens.Count}, accepted=0, sample={sample}";
            Console.WriteLine($"[Stripchat] {tokens.Count} models parsed, 0 passed live/public filter (skipped={skippedNotLive}).\n[Stripchat] sample model:\n{sample}");
            return null;
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
