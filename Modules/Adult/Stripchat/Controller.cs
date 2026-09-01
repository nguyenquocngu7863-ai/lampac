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
        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>($"Stripchat:list:{tag}:{pg}", 3, async e =>
        {
            List<PlaylistItem> playlists = null;
            int totalPages = 0;
            string url = StripchatTo.Uri(init.host, tag, pg);

            // .NET HttpClient fails TLS to this host in Android proot while the system curl
            // succeeds from the exact same guest. Use curl as the narrow transport fallback;
            // ArgumentList avoids shell interpolation and all URL values are server-generated.
            var fetched = await CurlGet(url);
            if (fetched.exitCode == 0 && !string.IsNullOrEmpty(fetched.body))
                playlists = StripchatTo.Playlist(init.host, fetched.body.AsSpan(), pg, out totalPages);

            if (playlists == null || playlists.Count == 0)
            {
                string reason = StripchatTo.LastError;
                if (fetched.exitCode != 0)
                    reason = $"curl exit={fetched.exitCode}: {fetched.error}";
                else if (string.IsNullOrEmpty(fetched.body))
                    reason = "curl returned an empty body";
                return e.Fail($"playlists: {reason ?? "unknown parser result"}", refresh_proxy: true);
            }
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

    static async Task<(int exitCode, string body, string error)> CurlGet(string url)
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
                "-fsSL", "--max-time", "20", "--compressed",
                "-A", "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36",
                "-H", "Referer: https://stripchat.com/",
                "-H", "Origin: https://stripchat.com",
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
