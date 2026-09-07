using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Online.Settings;
using Shared.Services;
using System.Collections.Generic;

namespace Vidrock;

public class ModInit : IModuleLoaded, IModuleOnline
{
    public static OnlinesSettings conf;

    public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        var online = new List<ModuleOnlineItem>();
        bool allowWhenEngDisabled = conf?.enabled == true;
        if ((args.original_language == null || args.original_language == "en") &&
            (CoreInit.conf.disableEng == false || allowWhenEngDisabled))
        {
            if (args.source != null && (args.source is "tmdb" or "cub") && long.TryParse(args.id, out long id) && id > 0)
                online.Add(new(conf, "vidrock", "Vidrock", " (ENG)"));
        }
        return online;
    }

    public void Loaded(HttpContext httpContext, RequestModel requestInfo)
    {
        conf = ModuleInvoke.Init("Vidrock", new OnlinesSettings("Vidrock", "https://vidrock.ru")
        {
            displayname = "Vidrock (ENG)",
            displayindex = 21,
            host = "https://vidrock.ru",
            apihost = "https://vidrock.ru",
            proxy = new Shared.Models.ProxySettings()
            {
                useAuth = false,
                bypassOnFailure = true
            },
            headers = HeadersModel.Init(
                ("Referer", "https://vidrock.ru/"),
                ("Origin", "https://vidrock.ru")
            )
        });

        conf.headers = HeadersModel.Init(
            ("Referer", "https://vidrock.ru/"),
            ("Origin", "https://vidrock.ru")
        );

        conf.group = 1;
    }
}
