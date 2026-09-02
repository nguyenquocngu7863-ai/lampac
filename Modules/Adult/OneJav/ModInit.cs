using Shared;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;

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
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("OneJav", new OneJavConf());
    }

    /// <summary>Địa chỉ TorrServer dùng để phát (mặc định server ngoài nhanh).</summary>
    public static string TsHost()
    {
        return string.IsNullOrWhiteSpace(conf?.tsserver)
            ? "http://gren439e.tsarea.tv:8880"
            : conf.tsserver.TrimEnd('/');
    }
}
