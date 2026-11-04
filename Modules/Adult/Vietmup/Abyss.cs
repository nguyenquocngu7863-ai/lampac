using Microsoft.AspNetCore.Http;
using Shared.Services.Hybrid;
using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Vietmup;

// Abyss (player.abyssplayer.com)
//  - trang player nhúng `const datas = "<base64>"`; JSON đó có field `media` mã hoá AES-256-CTR,
//    key = ASCII(md5hex("user_id:slug:md5_id")), counter = 16 ký tự đầu của chuỗi hex đó.
//  - file mp4 trên CDN: CHỈ phần đầu (ftyp + moov) bị mã hoá, cùng thuật toán nhưng
//    key = ASCII(md5hex(segment cuối của path)); phần mdat phía sau là plaintext.
//    Server seek counter theo Range nên proxy chỉ cần giải mã đúng đoạn nằm trong header.
public static class AbyssTo
{
    static readonly HttpClient _client = new HttpClient()
    {
        Timeout = TimeSpan.FromSeconds(40)
    };

    public sealed class StreamInfo
    {
        public string url { get; set; }
        public string keyHex { get; set; }
        public long headerEnd { get; set; }
        public long size { get; set; }
    }

    public static async Task<StreamInfo> ResolveAsync(string html, string playerUrl)
    {
        if (string.IsNullOrEmpty(html) || string.IsNullOrEmpty(playerUrl))
            return null;

        var cache = HybridCache.Get();
        string ck = "vietmup:abyss:" + playerUrl;
        if (cache.TryGetValue(ck, out StreamInfo hit) && hit != null)
            return hit;

        var dm = Regex.Match(html, "const\\s+datas\\s*=\\s*\"([^\"]+)\"");
        if (!dm.Success)
            return null;

        string cdnUrl, keyHex;
        long size;

        try
        {
            string raw = Encoding.Latin1.GetString(Convert.FromBase64String(dm.Groups[1].Value));

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            string slug = root.GetProperty("slug").GetString();
            long md5id = root.GetProperty("md5_id").GetInt64();
            long uid = root.GetProperty("user_id").GetInt64();
            string media = root.GetProperty("media").GetString();

            byte[] mkey = Encoding.ASCII.GetBytes(Md5Hex($"{uid}:{slug}:{md5id}"));
            byte[] plain = CtrXor(mkey, 0, Encoding.Latin1.GetBytes(media));

            using var md = JsonDocument.Parse(plain);
            var sources = md.RootElement.GetProperty("mp4").GetProperty("sources");

            JsonElement best = default;
            long bestSize = -1;
            foreach (var s in sources.EnumerateArray())
            {
                if (s.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.False)
                    continue;
                long sz = s.TryGetProperty("size", out var szEl) ? szEl.GetInt64() : 0;
                if (sz > bestSize)
                {
                    bestSize = sz;
                    best = s;
                }
            }

            if (bestSize <= 0)
                return null;

            cdnUrl = best.GetProperty("url").GetString().TrimEnd('/') + "/"
                + best.GetProperty("path").GetString().TrimStart('/');
            keyHex = Md5Hex(cdnUrl.Substring(cdnUrl.LastIndexOf('/') + 1));
            size = bestSize;
        }
        catch
        {
            return null;
        }

        long headerEnd = await FindHeaderEnd(cdnUrl, keyHex);
        if (headerEnd <= 0)
            return null;

        var info = new StreamInfo() { url = cdnUrl, keyHex = keyHex, headerEnd = headerEnd, size = size };
        cache.Set(ck, info, TimeSpan.FromMinutes(30), inmemory: true);
        return info;
    }

    // Giải mã 1KB đầu rồi đi theo box: ftyp -> (free) -> moov, hết moov chính là biên vùng mã hoá.
    static async Task<long> FindHeaderEnd(string url, string keyHex)
    {
        byte[] head = await GetRange(url, 0, 1023);
        if (head == null || head.Length < 16)
            return -1;

        CtrXor(Encoding.ASCII.GetBytes(keyHex), 0, head);

        int o = 0;
        for (int i = 0; i < 8 && o + 8 <= head.Length; i++)
        {
            long sz = ((long)head[o] << 24) | ((long)head[o + 1] << 16) | ((long)head[o + 2] << 8) | head[o + 3];
            string type = Encoding.ASCII.GetString(head, o + 4, 4);
            if (sz < 8)
                return -1;
            if (type == "moov")
                return o + sz;
            o += (int)sz;
            if (o >= head.Length)
                break;
        }

        return -1;
    }

    static async Task<byte[]> GetRange(string url, long from, long to)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", VietmupTo.ChromeUA);
            req.Headers.TryAddWithoutValidation("Referer", "https://player.abyssplayer.com/");
            req.Headers.TryAddWithoutValidation("Accept", "*/*");
            req.Headers.TryAddWithoutValidation("Range", $"bytes={from}-{to}");

            using var resp = await _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode)
                return null;

            return await resp.Content.ReadAsByteArrayAsync();
        }
        catch
        {
            return null;
        }
    }

    public static async Task<bool> StreamAsync(HttpContext ctx, StreamInfo info, CancellationToken abort)
    {
        long start = 0;
        string range = ctx.Request.Headers["Range"].ToString();
        bool hasRange = false;

        if (!string.IsNullOrEmpty(range))
        {
            var m = Regex.Match(range, @"bytes\s*=\s*(\d+)\s*-\s*(\d*)");
            if (m.Success)
            {
                hasRange = true;
                start = long.Parse(m.Groups[1].Value);
            }
        }

        using var req = new HttpRequestMessage(HttpMethod.Get, info.url);
        req.Headers.TryAddWithoutValidation("User-Agent", VietmupTo.ChromeUA);
        req.Headers.TryAddWithoutValidation("Referer", "https://player.abyssplayer.com/");
        req.Headers.TryAddWithoutValidation("Accept", "*/*");
        if (hasRange)
            req.Headers.TryAddWithoutValidation("Range", range);

        using var resp = await _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, abort);
        if (!resp.IsSuccessStatusCode)
            return false;

        ctx.Response.StatusCode = (int)resp.StatusCode;
        ctx.Response.ContentType = "video/mp4";
        ctx.Response.Headers["Accept-Ranges"] = "bytes";

        if (resp.Content.Headers.ContentRange != null)
            ctx.Response.Headers["Content-Range"] = resp.Content.Headers.ContentRange.ToString();
        if (resp.Content.Headers.ContentLength.HasValue)
            ctx.Response.ContentLength = resp.Content.Headers.ContentLength.Value;

        byte[] key = Encoding.ASCII.GetBytes(info.keyHex);
        long remainHead = info.headerEnd - start;
        if (remainHead < 0)
            remainHead = 0;

        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using var ecb = aes.CreateEncryptor();

        byte[] ctr = new byte[16];
        Buffer.BlockCopy(key, 0, ctr, 0, 16);
        AddCounter(ctr, start / 16);

        byte[] ks = new byte[16];
        ecb.TransformBlock(ctr, 0, 16, ks, 0);
        AddCounter(ctr, 1);
        int ksPos = (int)(start % 16);

        await using var input = await resp.Content.ReadAsStreamAsync(abort);
        byte[] buf = new byte[65536];

        while (true)
        {
            int n = await input.ReadAsync(buf.AsMemory(0, buf.Length), abort);
            if (n <= 0)
                break;

            int dec = (int)Math.Min(n, remainHead);
            for (int i = 0; i < dec; i++)
            {
                if (ksPos == 16)
                {
                    ecb.TransformBlock(ctr, 0, 16, ks, 0);
                    AddCounter(ctr, 1);
                    ksPos = 0;
                }
                buf[i] ^= ks[ksPos++];
            }
            remainHead -= dec;

            await ctx.Response.Body.WriteAsync(buf.AsMemory(0, n), abort);
        }

        return true;
    }

    public static byte[] CtrXor(byte[] key, long offset, byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using var ecb = aes.CreateEncryptor();

        byte[] ctr = new byte[16];
        Buffer.BlockCopy(key, 0, ctr, 0, 16);
        AddCounter(ctr, offset / 16);

        byte[] ks = new byte[16];
        ecb.TransformBlock(ctr, 0, 16, ks, 0);
        AddCounter(ctr, 1);
        int p = (int)(offset % 16);

        for (int i = 0; i < data.Length; i++)
        {
            if (p == 16)
            {
                ecb.TransformBlock(ctr, 0, 16, ks, 0);
                AddCounter(ctr, 1);
                p = 0;
            }
            data[i] ^= ks[p++];
        }

        return data;
    }

    // counter 128-bit big-endian (giống OpenSSL/WebCrypto AES-CTR)
    static void AddCounter(byte[] c, long add)
    {
        ulong hi = 0, lo = 0;
        for (int i = 0; i < 8; i++) hi = (hi << 8) | c[i];
        for (int i = 8; i < 16; i++) lo = (lo << 8) | c[i];

        ulong nlo = lo + (ulong)add;
        if (nlo < lo)
            hi++;

        for (int i = 15; i >= 8; i--) { c[i] = (byte)nlo; nlo >>= 8; }
        for (int i = 7; i >= 0; i--) { c[i] = (byte)hi; hi >>= 8; }
    }

    public static string Md5Hex(string s)
    {
        byte[] h = MD5.HashData(Encoding.UTF8.GetBytes(s));
        var sb = new StringBuilder(32);
        foreach (byte b in h)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
