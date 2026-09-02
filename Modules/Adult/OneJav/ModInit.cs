using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using System.Collections.Generic;

namespace OneJav;

public class ModInit : IModuleLoaded
{
    public static string modpath;
    public static OneJavConf conf;

    public void Loaded(InitspaceModel baseconf)
    {
        modpath = baseconf.path;

        updateConf();
        EventListener.UpdateInitFile += updateConf;

        foreach (var m in conf.limit_map)
            CoreInit.conf.WAF.limit_map.Insert(0, m);
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("OneJav", new OneJavConf()
        {
            enable = true,
            displayname = "OneJAV 🎌",
            host = "https://onejav.com",
            sukebei = true,
            use_sukebei = true,
            use_ijav = true,
            // TorrServer mặc định dùng instance tích hợp (cổng TorrServer module).
            // Điền torrserver (vd http://192.168.1.10:8090) để dùng TorrServer ngoài.
            torrserver = "",
            limit_map = new List<WafLimitRootMap>()
            {
                new("^/onejav/", new WafLimitMap { limit = 20, second = 1 })
            }
        });
    }

    /// <summary>
    /// Địa chỉ TorrServer nội bộ: ưu tiên cấu hình OneJav, sau đó TorrServer tích hợp.
    /// </summary>
    public static string TsHost()
    {
        if (!string.IsNullOrWhiteSpace(conf?.torrserver))
            return conf.torrserver.TrimEnd('/');

        int port = 9085;
        if (CoreInit.CurrentConf != null &&
            CoreInit.CurrentConf.TryGetValue("TorrServer", out var tsConf))
        {
            var p = tsConf.Value<int?>("tsport");
            if (p.HasValue) port = p.Value;
        }

        return $"http://{CoreInit.conf.listen.localhost}:{port}";
    }
}
