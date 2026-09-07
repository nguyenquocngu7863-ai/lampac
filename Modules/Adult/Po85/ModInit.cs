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

                if (!File.Exists(resolver) || !File.Exists("/usr/bin/node"))
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
                        uhdprocess.StartInfo.FileName = "/usr/bin/node";
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
            displayindex = 19,
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
