using Shared.Models.Base;
using Shared.Models.Templates;
using System;

namespace Sootio;

public sealed class ModuleConf : BaseSettings, ICloneable
{
    public ModuleConf()
    {
        plugin = "sootio";
        displayname = "Sootio (HTTP)";
        displayindex = 1075;
        enable = true;
        // This is the public Sootio httpstreaming configuration supplied by
        // the user. Replace it with another generated manifest if needed.
        manifest = "https://sooti.click/%7B%22DebridServices%22%3A%5B%7B%22provider%22%3A%22httpstreaming%22%2C%22http4khdhub%22%3Atrue%2C%22httpHDHub4u%22%3Atrue%2C%22httpUHDMovies%22%3Atrue%2C%22httpMoviesDrive%22%3Atrue%2C%22httpMKVCinemas%22%3Atrue%2C%22httpMalluMv%22%3Atrue%2C%22httpCineDoze%22%3Atrue%2C%22httpVixSrc%22%3Atrue%2C%22httpMoviesMod%22%3Atrue%2C%22httpMoviesLeech%22%3Atrue%2C%22httpAnimeFlix%22%3Atrue%2C%22http111477%22%3Atrue%2C%22httpPixeldrain%22%3Atrue%2C%22httpMkvBase%22%3Atrue%2C%22httpVadapav%22%3Atrue%2C%22enableProxy%22%3Afalse%2C%22proxyUrl%22%3A%22%22%2C%22proxyPassword%22%3A%22%22%7D%5D%2C%22Languages%22%3A%5B%5D%2C%22Resolutions%22%3A%5B%5D%2C%22Scrapers%22%3A%5B%221337x%22%2C%22knaben%22%2C%22torrents-csv%22%2C%22rarbg%22%2C%22extto%22%2C%22limetorrents%22%5D%2C%22IndexerScrapers%22%3A%5B%22stremthru%22%5D%2C%22ScrapersConfigured%22%3Atrue%2C%22minSize%22%3A0%2C%22maxSize%22%3A200%2C%22ShowCatalog%22%3Atrue%2C%22ProxyApplyAll%22%3Afalse%2C%22DebridProvider%22%3A%22httpstreaming%22%7D/manifest.json";
        streamproxy = true;
    }

    /// <summary>Configured Sootio Stremio manifest URL.</summary>
    public string manifest { get; set; }

    /// <summary>HTTP timeout for the add-on stream request.</summary>
    public int timeoutSeconds { get; set; } = 30;

    /// <summary>Maximum number of HTTP streams shown for one title.</summary>
    public int maxStreams { get; set; } = 100;

    public ModuleConf Clone()
    {
        return (ModuleConf)MemberwiseClone();
    }

    object ICloneable.Clone()
    {
        return MemberwiseClone();
    }
}

public sealed record SootioStreamItem(
    string Url,
    string Name,
    string Title,
    string Quality,
    string Format,
    SubtitleTpl Subtitles,
    System.Collections.Generic.List<HeadersModel> Headers
);
