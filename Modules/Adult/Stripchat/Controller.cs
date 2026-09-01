using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Stripchat;

public class StripchatController : BaseSisiController
{
    public StripchatController() : base(ModInit.conf) { }

    [HttpGet, Staticache]
    [Route("stripchat")]
    public async Task<ActionResult> Index(string search, string tag, int pg = 1)
    {
        if (!string.IsNullOrEmpty(search))
            return OnError("no search", false);

        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

    rhubFallback:
        Console.WriteLine($"[Stripchat][v7] list request tag={tag} pg={pg}");
        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>($"Stripchat:list:{tag}:{pg}", 1, async e =>
        {
            List<PlaylistItem> playlists = null;
            int totalPages = 0;
            string lastReason = "unknown parser result";

            // Direct TLS to the apex stripchat.com is killed by some ISPs
            // (OpenSSL "unexpected eof while reading" = SNI reset), which hits
            // both HttpClient and system curl. Stripchat serves the identical
            // API on geo/alt domains, so use the mirrors and take the first
            // host that answers with actual rooms.
            var apiHosts = MirrorHosts();

            foreach (string apiHost in apiHosts)
            {
                try
                {
                    string url = StripchatTo.Uri(apiHost, tag, pg);

                    // .NET HttpClient fails TLS to this host in Android proot while the system curl
                    // succeeds from the exact same guest. Use curl as the narrow transport fallback;
                    // ArgumentList avoids shell interpolation and all URL values are server-generated.
                    var fetched = await CurlGet(url, apiHost);
                    if (fetched.exitCode == 0 && !string.IsNullOrEmpty(fetched.body))
                        playlists = StripchatTo.Playlist(apiHost, fetched.body.AsSpan(), pg, out totalPages);

                    if (playlists != null && playlists.Count > 0)
                        break;

                    if (fetched.exitCode != 0)
                        lastReason = $"curl {apiHost} exit={fetched.exitCode}: {fetched.error}";
                    else if (string.IsNullOrEmpty(fetched.body))
                        lastReason = $"curl {apiHost} returned an empty body";
                    else
                        lastReason = StripchatTo.LastError ?? "no rooms in response";
                }
                catch (Exception ex)
                {
                    lastReason = $"host {apiHost} exception: {ex.GetType().Name}: {ex.Message}";
                    playlists = null;
                }

                Console.WriteLine($"[Stripchat] host {apiHost} failed: {lastReason}");
            }

            if (playlists == null || playlists.Count == 0)
                return e.Fail($"playlists: {lastReason}", refresh_proxy: true);

            return e.Success((playlists, totalPages));
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(
            cache.Value.playlists,
            cache.ISingleCache,
            StripchatTo.Menu(host, tag),
            total_pages: cache.Value.total_pages
        );
    }

    [HttpGet]
    [Route("stripchat/play")]
    public async Task<ActionResult> Play(string u)
    {
        if (!StripchatTo.IsValidUsername(u))
            return OnError("bad username", false);

        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        Console.WriteLine($"[Stripchat][v7] play request u={u}");

        var cache = await InvokeCacheResult($"Stripchat:play:{u}", 5, jsonContext.StreamItem, async e =>
        {
            foreach (string apiHost in MirrorHosts())
            {
                try
                {
                    string url = StripchatTo.CamUri(apiHost, u);
                    var fetched = await CurlGet(url, apiHost);
                    if (fetched.exitCode != 0 || string.IsNullOrEmpty(fetched.body))
                    {
                        Console.WriteLine($"[Stripchat] cam {apiHost} failed: exit={fetched.exitCode} {fetched.error}");
                        continue;
                    }

                    var stream = StripchatTo.LiveStream(fetched.body.AsSpan());
                    if (stream?.qualitys != null && stream.qualitys.Count > 0)
                        return e.Success(stream);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Stripchat] play {apiHost} exception: {ex.Message}");
                }
            }

            return e.Fail("live stream not found (offline/private?)", refresh_proxy: true);
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return OnResult(cache);
    }

    static List<string> MirrorHosts()
    {
        var hosts = new List<string>();
        foreach (string h in new[] { "https://vi.stripchat.com", "https://stripol.com" })
            if (!hosts.Contains(h))
                hosts.Add(h);
        return hosts;
    }

    static async Task<(int exitCode, string body, string error)> CurlGet(string url, string originHost)
    {
        try
        {
            var start = new ProcessStartInfo("curl")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (string arg in new[]
            {
                "-fsSL", "--max-time", "12", "--compressed",
                "-A", "Mozilla/5.0 (iPad; CPU OS 17_4 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Mobile/15E148 Safari/604.1",
                "-H", $"Referer: {originHost}/",
                "-H", $"Origin: {originHost}",
                "-H", "Accept: application/json, text/plain, */*",
                "-H", "Accept-Language: en-US,en;q=0.9",
                url
            })
                start.ArgumentList.Add(arg);

            using var process = Process.Start(start);
            if (process == null)
                return (-1, null, "cannot start curl");

            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return (process.ExitCode, await stdout, (await stderr).Trim());
        }
        catch (System.Exception ex)
        {
            return (-1, null, ex.Message);
        }
    }
}
