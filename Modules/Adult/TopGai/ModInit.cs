using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TopGai;

public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("TopGai", conf, "topgai")
        };
    }

    public void Loaded(InitspaceModel baseconf)
    {
        modpath = baseconf.path;
        updateConf();
        EventListener.UpdateInitFile += updateConf;
        EventListener.ProxyApiCreateHttpRequest += ForceHttp2;
        EventListener.ProxyApiOverride += ProxyOverride;
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
        EventListener.ProxyApiCreateHttpRequest -= ForceHttp2;
        EventListener.ProxyApiOverride -= ProxyOverride;
    }

    // wogplayer/f-seg tra 401 cho HttpClient .NET mac dinh
    // (curl/python truc tiep 200) -> ep proxy dung h2
    static Task ForceHttp2(EventProxyApiCreateHttpRequest e)
    {
        try
        {
            string h = e?.uri?.Host ?? "";
            if ((h.Contains("wogplayer") || h.Contains("-seg-"))
                && e.requestMessage != null)
                e.requestMessage.Version = HttpVersion.Version20;
        }
        catch { }

        return Task.CompletedTask;
    }

    static readonly Regex PlaylistUrlRx = new("(https?://[^\\s\"']+)", RegexOptions.Compiled);

    // wogplayer.top chan TLS fingerprint cua .NET (401 ca h1/h2,
    // curl/python/Chromium 200). Override: tai master bang curl process,
    // rewrite moi URL thanh /proxy/ roi tra dung content-type m3u8.
    // Segment (f-seg-1) .NET tai duoc nen di proxy thuong.
    // Tra false = da tu xu ly, true = de pipeline mac dinh xu ly.
    static async Task<bool> ProxyOverride(EventProxyApiOverride e)
    {
        try
        {
            string uri = e?.decryptLink?.uri;
            if (string.IsNullOrEmpty(uri) || !uri.Contains("wogplayer.top") || !uri.Contains(".m3u8"))
                return true;

            string m3u = await CurlGet(uri);
            if (string.IsNullOrWhiteSpace(m3u) || !m3u.Contains("#EXTM3U"))
                return true;

            string host = CoreInit.Host(e.httpContext);
            string body = PlaylistUrlRx.Replace(m3u, m =>
                ProxyLink.Encrypt(m.Value.AsSpan(), e.decryptLink, prefix: [host, "/proxy/"]));

            e.httpContext.Response.ContentType = "application/vnd.apple.mpegurl";
            await e.httpContext.Response.WriteAsync(body, Encoding.UTF8);
            return false;
        }
        catch
        {
            return true;
        }
    }

    static async Task<string> CurlGet(string url)
    {
        try
        {
            var psi = new ProcessStartInfo(System.IO.File.Exists("/usr/bin/curl") ? "/usr/bin/curl" : "curl", "")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-sL");
            psi.ArgumentList.Add("--max-time");
            psi.ArgumentList.Add("20");
            psi.ArgumentList.Add("-A");
            psi.ArgumentList.Add("Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36");
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add("https://topgai.net/");
            psi.ArgumentList.Add(url);

            using (var p = Process.Start(psi))
            {
                if (p == null)
                    return null;
                string stdout = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();
                return p.ExitCode == 0 ? stdout : null;
            }
        }
        catch
        {
            return null;
        }
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("TopGai", new SisiSettings("TopGai", "https://topgai.net")
        {
            displayindex = 17,
            streamproxy = true,
            httpversion = 2,
            rch_access = "apk",
            stream_access = "apk",
            headers_stream = HeadersModel.Init(
                ("referer", "https://topgai.net/")
            ).ToDictionary(),
            headers_image = HeadersModel.Init(
                ("referer", "https://topgai.net/")
            ).ToDictionary()
        });
    }
}
