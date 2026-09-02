using Shared.Models.Module;

namespace OneJav;

/// <summary>
/// Cấu hình module OneJAV. Phần torrserver để trống = dùng TorrServer tích hợp.
/// </summary>
public class OneJavConf : ModuleBaseConf
{
    /// <summary>Tên hiển thị trong Lampa.</summary>
    public string displayname { get; set; }

    /// <summary>Site OneJAV (mặc định https://onejav.com).</summary>
    public string host { get; set; }

    /// <summary>Bật/tắt module.</summary>
    public bool enable { get; set; } = true;

    /// <summary>Địa chỉ TorrServer ngoài (vd http://192.168.1.10:8090). Để trống = dùng TorrServer tích hợp.</summary>
    public string torrserver { get; set; }

    /// <summary>Tìm thêm magnet trên Sukebei (nyaa) khi OneJAV không có sẵn.</summary>
    public bool use_sukebei { get; set; } = true;

    /// <summary>Tìm thêm magnet trên ijavtorrent.com.</summary>
    public bool use_ijav { get; set; } = true;

    /// <summary>Field tương thích cũ.</summary>
    public bool sukebei { get; set; } = true;
}
