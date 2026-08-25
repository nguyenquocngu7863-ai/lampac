namespace LampaWeb;

public class InitPlugins
{
    public bool pirate_store { get; set; }

    public bool jacred { get; set; }

    /// <summary>Syncs the local Jackett URL and API key into Lampa parser settings.</summary>
    public bool jackett { get; set; } = true;

    public bool dlna { get; set; }

    public bool tracks { get; set; }

    public bool transcoding { get; set; }

    public bool tmdbProxy { get; set; }

    public bool cubProxy { get; set; }

    public bool online { get; set; }

    /// <summary>Loads compact responsive styles for Online result cards.</summary>
    public bool onlineCompact { get; set; } = true;

    public bool catalog { get; set; }

    public bool dorama { get; set; }

    /// <summary>Installs the built-in Vietnamese SubSense subtitle plugin in Lampa.</summary>
    /// <remarks>Opt-in: the stable default is StremioSub below.</remarks>
    public bool subsenseAuto { get; set; }

    public bool sisi { get; set; }

    public bool torrserver { get; set; }

    public bool backup { get; set; }

    public bool sync { get; set; }

    public bool bookmark { get; set; }

    public bool timecode { get; set; }

    public bool watch_together { get; set; }

    /// <summary>Loads the legacy direct SubSense provider.</summary>
    /// <remarks>Opt-in; enable only instead of <see cref="stremiosub"/>.</remarks>
    public bool subsense { get; set; }

    /// <summary>Loads the SubDL/SubSource API provider.</summary>
    /// <remarks>Opt-in; enable only instead of <see cref="stremiosub"/>.</remarks>
    public bool subfinder { get; set; }

    /// <summary>Loads the stable Stremio SubDL/SubSource provider.</summary>
    public bool stremiosub { get; set; } = true;

    /// <summary>Shows a protected AdminPanel shortcut in Lampa Settings.</summary>
    public bool adminpanel { get; set; } = true;

    /// <summary>Loads the GStreamer player helper when the GStreamer module is installed.</summary>
    public bool gst { get; set; } = true;
}
