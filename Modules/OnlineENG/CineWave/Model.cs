using Shared.Models.Base;
using System;

namespace CineWave;

public sealed class ModuleConf : BaseSettings, ICloneable
{
    public ModuleConf()
    {
        plugin = "cinewave";
        displayname = "CineWave";
        displayindex = 1065;
        enable = true;
        siteHost = "https://www.cinewave.su";
        streamproxy = false;
        timeoutSeconds = 30;
        resolveSeconds = 32;
        cacheSeconds = 1200;
    }

    /// <summary>CineWave frontend host that serves /play/{encId} pages.</summary>
    public string siteHost { get; set; }

    /// <summary>HTTP timeout for metadata requests (TMDB/Cinemeta).</summary>
    public int timeoutSeconds { get; set; }

    /// <summary>
    /// How long the headless browser may wait for the first m3u8/mp4 request
    /// to appear on the CineWave play page.
    /// </summary>
    public int resolveSeconds { get; set; }

    /// <summary>How long a resolved stream URL stays in memory cache.</summary>
    public int cacheSeconds { get; set; }

    public ModuleConf Clone()
    {
        return (ModuleConf)MemberwiseClone();
    }

    object ICloneable.Clone()
    {
        return MemberwiseClone();
    }
}

public sealed record CineWaveStream(
    string Url,
    System.Collections.Generic.List<HeadersModel> Headers
);
