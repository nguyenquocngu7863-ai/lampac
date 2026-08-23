namespace LampaWeb;

public class InitPlugins
{
    public bool pirate_store { get; set; }

    public bool jacred { get; set; }

    public bool dlna { get; set; }

    public bool tracks { get; set; }

    public bool transcoding { get; set; }

    public bool tmdbProxy { get; set; }

    public bool cubProxy { get; set; }

    public bool online { get; set; }

    public bool catalog { get; set; }

    public bool dorama { get; set; }

    /// <summary>Installs the built-in Vietnamese SubSense subtitle plugin in Lampa.</summary>
    public bool subsenseAuto { get; set; }

    public bool sisi { get; set; }

    public bool torrserver { get; set; }

    public bool backup { get; set; }

    public bool sync { get; set; }

    public bool bookmark { get; set; }

    public bool timecode { get; set; }

    public bool watch_together { get; set; }

    // StremioSub is the single built-in subtitle provider. Keep older
    // auto-subtitle plugins opt-in so they cannot attach duplicate tracks.
    public bool subsense { get; set; }

    public bool subfinder { get; set; }

    /// <summary>Loads the Stremio SubDL/SubSource subtitle plugin.</summary>
    public bool stremiosub { get; set; } = true;

    /// <summary>Shows a protected AdminPanel shortcut in Lampa Settings.</summary>
    public bool adminpanel { get; set; } = true;

    /// <summary>Loads the GStreamer player helper when the GStreamer module is installed.</summary>
    public bool gst { get; set; } = true;
}
