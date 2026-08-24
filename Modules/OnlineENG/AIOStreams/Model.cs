using Shared.Models.Base;
using System;

namespace AIOStreams;

public sealed class ModuleConf : BaseSettings, ICloneable
{
    public ModuleConf()
    {
        plugin = "aiostreams";
        displayname = "AIOStreams";
        displayindex = 1080;
        // AIOStreams is optional. The user enters a private generated
        // manifest URL in init.conf/AdminPanel; never ship one in the module.
        enable = false;
        manifest = string.Empty;
        streamproxy = true;
        streams = true;
        subtitles = true;
    }

    /// <summary>
    /// Configured Stremio manifest URL. It must expose the standard stream
    /// resource and should be an HTTPS URL from a source the user is allowed
    /// to access.
    /// </summary>
    public string manifest { get; set; }

    /// <summary>HTTP timeout for the add-on stream request.</summary>
    public int timeoutSeconds { get; set; } = 25;

    /// <summary>Maximum number of HTTP streams shown for one title.</summary>
    public int maxStreams { get; set; } = 100;

    /// <summary>Read stream resources from the configured AIOStreams manifest.</summary>
    public bool streams { get; set; } = true;

    /// <summary>Read subtitle resources from the configured AIOStreams manifest.</summary>
    public bool subtitles { get; set; } = true;

    /// <summary>How long an AIO response may stay in Lampac memory cache.</summary>
    public int cacheSeconds { get; set; } = 120;

    public ModuleConf Clone()
    {
        return (ModuleConf)MemberwiseClone();
    }

    object ICloneable.Clone()
    {
        return MemberwiseClone();
    }
}

public sealed record AIOStreamItem(
    string Url,
    string Name,
    string Title,
    string Quality,
    string Format,
    System.Collections.Generic.List<HeadersModel> Headers
);
