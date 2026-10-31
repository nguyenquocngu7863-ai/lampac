using Shared.Models.Base;
using System;

namespace Filmlord;

public class FilmlordConf : BaseSettings, ICloneable
{
    public FilmlordConf(string plugin, string host, bool enable = true, bool streamproxy = false)
    {
        this.enable = enable;
        this.plugin = plugin;
        this.streamproxy = streamproxy;

        if (host != null)
            this.host = host.StartsWith("http") ? host : Decrypt(host);
    }


    /// <summary>true — отдавать DASH (до 1080p), false — HLS (~640p).</summary>
    public bool dash { get; set; }

    /// <summary>true — показывать и HLS, и DASH отдельными пунктами.</summary>
    public bool both { get; set; }

    /// <summary>true — добирать поток из плеера Alloha (4K) через Playwright, если Collaps не дал ссылку.</summary>
    public bool alloha { get; set; } = true;


    public FilmlordConf Clone()
    {
        return (FilmlordConf)MemberwiseClone();
    }

    object ICloneable.Clone()
    {
        return MemberwiseClone();
    }
}
