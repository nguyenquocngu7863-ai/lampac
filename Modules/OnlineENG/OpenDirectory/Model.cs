using Shared.Models.Base;
using System;

namespace OpenDirectory;

public sealed class ModuleConf : BaseSettings, ICloneable
{
    public ModuleConf()
    {
        plugin = "opendirectory";
        displayname = "Open Directory";
        displayindex = 1070;
        enable = true;
        directoryHost = "https://a.111477.xyz";
        streamproxy = true;
        timeoutSeconds = 20;
        maxFiles = 40;
        maxDirectoryEntries = 2500;
    }

    /// <summary>Public directory host. Only this host is accepted by the bridge.</summary>
    public string directoryHost { get; set; }

    /// <summary>HTTP timeout used to read a directory index.</summary>
    public int timeoutSeconds { get; set; }

    /// <summary>Maximum number of media files shown for one folder.</summary>
    public int maxFiles { get; set; }

    /// <summary>Protects the phone from unexpectedly large directory listings.</summary>
    public int maxDirectoryEntries { get; set; }

    public ModuleConf Clone()
    {
        return (ModuleConf)MemberwiseClone();
    }

    object ICloneable.Clone()
    {
        return MemberwiseClone();
    }
}

public sealed record OpenDirectoryEntry(
    string Name,
    string Url,
    bool IsDirectory
);

public sealed record OpenDirectoryMedia(
    string Name,
    string Url,
    string Format,
    string Quality
);
