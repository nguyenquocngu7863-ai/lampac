using Shared.Models.Module;

namespace OneJav;

/// <summary>
/// Cấu hình module OneJAV.
/// </summary>
public class OneJavConf : ModuleBaseConf
{
    /// <summary>Bật/tắt module.</summary>
    public bool enable { get; set; } = true;

    /// <summary>Site OneJAV.</summary>
    public string host { get; set; } = "https://onejav.com";

    /// <summary>TorrServer ngoài để phát (không qua gst/proxy nội bộ). Vd http://1.2.3.4:8090</summary>
    public string tsserver { get; set; } = "http://gren439e.tsarea.tv:8880";

    /// <summary>User TorrServer nếu có (để trống nếu không bật auth).</summary>
    public string ts_login { get; set; }

    /// <summary>Password TorrServer nếu có.</summary>
    public string ts_passwd { get; set; }

    /// <summary>Tìm magnet trên Sukebei (nyaa) — có số seed.</summary>
    public bool use_sukebei { get; set; } = true;

    /// <summary>Tìm magnet trên ijavtorrent.com.</summary>
    public bool use_ijav { get; set; } = true;
}
