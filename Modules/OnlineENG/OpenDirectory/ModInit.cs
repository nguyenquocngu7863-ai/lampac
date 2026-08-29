using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using System;
using System.Collections.Generic;

namespace OpenDirectory;

public sealed class ModInit : IModuleLoaded, IModuleOnline
{
    public static ModuleConf conf;

    public List<ModuleOnlineItem> Invoke(
        HttpContext httpContext,
        RequestModel requestInfo,
        string host,
        OnlineEventsModel args
    )
    {
        if (CoreInit.conf?.disableEng != false || conf == null || !conf.enable || args == null)
            return null;

        if (string.IsNullOrWhiteSpace(args.title) &&
            string.IsNullOrWhiteSpace(args.original_title))
        {
            return null;
        }

        return new List<ModuleOnlineItem>
        {
            new(conf, "opendirectory", "Open Directory", " (HTTP)")
        };
    }

    public void Loaded(InitspaceModel baseconf)
    {
        UpdateConf();
        EventListener.UpdateInitFile += UpdateConf;
        EventListener.OnlineApiQuality += OnlineApiQuality;
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= UpdateConf;
        EventListener.OnlineApiQuality -= OnlineApiQuality;
    }

    static void UpdateConf()
    {
        conf = ModuleInvoke.Init("OpenDirectory", new ModuleConf());
    }

    static string OnlineApiQuality(EventOnlineApiQuality e)
    {
        return e.balanser == "opendirectory" ? " ~ HTTP" : null;
    }
}
