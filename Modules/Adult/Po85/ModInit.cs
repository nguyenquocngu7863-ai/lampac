using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Po85;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;

    public static string modpath;

    static bool uhdShutdown;
    public static Process uhdprocess;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("85po.com", conf, "po85")
        };
    }

    public void Loaded(InitspaceModel baseconf)
    {
        modpath = baseconf.path;
        updateConf();
        EventListener.UpdateInitFile += updateConf;
        EventListener.ProxyApiOverride += ProxyOverride;
        StartUhd();
    }

    public void Dispose()
    {
        try
        {
            uhdShutdown = true;
            uhdprocess?.Kill(true);
        }
        catch { }

        EventListener.UpdateInitFile -= updateConf;
        EventListener.ProxyApiOverride -= ProxyOverride;
    }

    // 85po.com chan HttpClient .NET (Cloudflare 403). Tai bang curl shim
    // (curl_cffi impersonate) roi tra bytes thang, giong MissAV lam voi surrit.
    static async Task<bool> ProxyOverride(EventProxyApiOverride e)
    {
        try
        {
            string uri = e?.decryptLink?.uri;
            if (string.IsNullOrEmpty(uri) || !uri.Contains("85po.com"))
                return true;

            byte[] body = await Po85To.CurlGetBytes(uri, "https://www.85po.com/");
            if (body == null || body.Length == 0)
                return true;

            var resp = e.httpContext.Response;
            resp.StatusCode = 200;
            string lower = uri.ToLowerInvariant();
            resp.ContentType = lower.Contains(".m3u8") ? "application/vnd.apple.mpegurl"
                : lower.Contains(".mp4") ? "video/mp4"
                : "application/octet-stream";
            await resp.Body.WriteAsync(body, 0, body.Length);
            return false;
        }
        catch
        {
            return true;
        }
    }

    #region uhd-resolver (node + Chrome that, giai ma link 4K 85po)
    void StartUhd()
    {
        Task.Run(async () =>
        {
            try
            {
                string uhdDir = Path.Combine(modpath ?? "", "uhd");
                string resolver = Path.Combine(uhdDir, "resolver.js");

                string nodeBin = System.Environment.GetEnvironmentVariable("LAMPAC_NODE");
                if (string.IsNullOrEmpty(nodeBin))
                    nodeBin = "/usr/bin/node";

                if (!File.Exists(resolver) || !File.Exists(nodeBin))
                    return;

                // tu cai playwright-core neu thieu (lan dau)
                if (!Directory.Exists(Path.Combine(uhdDir, "node_modules")))
                {
                    try
                    {
                        var npm = new Process();
                        npm.StartInfo.UseShellExecute = false;
                        npm.StartInfo.FileName = "/usr/bin/npm";
                        npm.StartInfo.Arguments = "i playwright-core --no-audit --no-fund";
                        npm.StartInfo.WorkingDirectory = uhdDir;
                        npm.Start();
                        await npm.WaitForExitAsync();
                    }
                    catch { }
                }

                while (!uhdShutdown && File.Exists(resolver))
                {
                    try { await Bash.ComandAsync("pkill -f 'Adult/Po85/uhd/resolver.js'"); } catch { }

                    try
                    {
                        uhdprocess = new Process();
                        uhdprocess.StartInfo.UseShellExecute = false;
                        uhdprocess.StartInfo.RedirectStandardOutput = true;
                        uhdprocess.StartInfo.RedirectStandardError = true;
                        uhdprocess.StartInfo.FileName = nodeBin;
                        uhdprocess.StartInfo.Arguments = $"\"{resolver}\" --port 9196";
                        uhdprocess.StartInfo.WorkingDirectory = uhdDir;
                        uhdprocess.Start();
                        uhdprocess.BeginOutputReadLine();
                        uhdprocess.BeginErrorReadLine();
                        await uhdprocess.WaitForExitAsync();
                    }
                    catch { }

                    await Task.Delay(10_000);
                }
            }
            catch { }
        });
    }
    #endregion

    void updateConf()
    {
        conf = ModuleInvoke.Init("Po85", new SisiSettings("Po85", "https://www.85po.com")
        {
            displayindex = 11,
            streamproxy = true,
            rch_access = "apk",
            stream_access = "apk",
            headers_stream = HeadersModel.Init(
                ("referer", "https://www.85po.com/")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("referer", "https://www.85po.com/")
            ).ToDictionary()
        });
    }
}
