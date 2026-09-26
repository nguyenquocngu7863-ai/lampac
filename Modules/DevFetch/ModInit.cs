using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace DevFetch;

// TAM: proxy cleanup cho agent tu fetch web qua IP may (xoa ca module truoc khi push)
public class ModInit : IModuleLoaded, IModuleSisi
{
    public static SisiSettings conf;
    public static string modpath;

    public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
    {
        return new List<SisiModuleItem>()
        {
            new("DevFetch", conf, "devfetch")
        };
    }

    public void Loaded(InitspaceModel baseconf)
    {
        modpath = baseconf.path;
        updateConf();
        EventListener.UpdateInitFile += updateConf;
        StartSniff();
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
    }

    // Sniffer trinh duyet chung (port 9197): mo trang, kich play, bat link media.
    // Khong supervisor loop (tam) - chet thi restart lampac.
    void StartSniff()
    {
        Task.Run(async () =>
        {
            try
            {
                string resolver = Path.Combine(modpath ?? "", "sniff.js");
                string nodeBin = System.Environment.GetEnvironmentVariable("LAMPAC_NODE");
                if (string.IsNullOrEmpty(nodeBin))
                    nodeBin = "/usr/bin/node";

                if (!File.Exists(resolver) || !File.Exists(nodeBin))
                    return;
                var p = new Process();
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.StartInfo.FileName = nodeBin;
                p.StartInfo.Arguments = "\"" + resolver + "\" --port 9197";
                p.StartInfo.WorkingDirectory = modpath;
                // Dung chung playwright-core cua Po85 (ne xung dot npm/nodesource)
                try { p.StartInfo.Environment["NODE_PATH"] = System.IO.Path.GetFullPath(System.IO.Path.Combine(modpath ?? "", "..", "Adult", "Po85", "uhd", "node_modules")); } catch { }
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                await p.WaitForExitAsync();
            }
            catch { }
        });
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("DevFetch", new SisiSettings("DevFetch", "https://example.com")
        {
            displayindex = 99,
            streamproxy = false,
            rch_access = "apk",
            stream_access = "apk",
        });
    }
}
