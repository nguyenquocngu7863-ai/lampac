using GStreamer.Services;
using Shared;
using Shared.Models.AppConf;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GStreamer;

public class ModInit : IModuleLoaded
{
    public static string modpath;
    public static ModuleConf conf;
    public static string gstRootPath;
    static double? gstVersion;

    public void Loaded(InitspaceModel initspace)
    {
        modpath = initspace.path;

        string cachePath = Path.Combine("cache", "gstranscoding");

        if (Directory.Exists(cachePath))
        {
            try
            {
                Directory.Delete(cachePath, true);
            }
            catch { }
        }

        Directory.CreateDirectory(cachePath);

        updateConf();
        EventListener.UpdateInitFile += updateConf;

        foreach (var m in conf.limit_map)
            CoreInit.conf.WAF.limit_map.Insert(0, m);

        SetupGStreamer();

        gstVersion = ReadGstVersion();
        if (gstVersion.HasValue)
            conf.gst_version = gstVersion.Value;

        InitGst();
        if (conf.useGpu)
        {
            HardwareVideoBackend.Initialize();
            HdrToneMappingBackend.Initialize();
        }
    }

    public void Dispose()
    {
        GService.Dispose();
        EventListener.UpdateInitFile -= updateConf;
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("gst", new ModuleConf()
        {
            gst_version = OperatingSystem.IsWindows() ? 1.28 : 1.22,
            PATH = @"C:\Program Files\gstreamer\1.0\mingw_x86_64",
            inactiveMinutes = 10,
            limit_map = new List<WafLimitRootMap>()
            {
                new("^/gst/", new WafLimitMap { limit = 50, second = 1 })
            }
        });

        if (gstVersion.HasValue)
            conf.gst_version = gstVersion.Value;
    }


    static void InitGst()
    {
        Gst.Module.Initialize();
        GstApp.Module.Initialize();

        var gstArgs = Array.Empty<string>();
        Gst.Functions.Init(ref gstArgs);
    }

    static void SetupGStreamer()
    {
        string registryPath = Path.Combine(
            AppContext.BaseDirectory,
            "cache",
            "gstreamer-registry.bin"
        );

        Environment.SetEnvironmentVariable(
            "GST_REGISTRY",
            registryPath,
            EnvironmentVariableTarget.Process
        );

        Environment.SetEnvironmentVariable(
            "GST_REGISTRY_1_0",
            registryPath,
            EnvironmentVariableTarget.Process
        );

        if (!OperatingSystem.IsWindows())
        {
            // Linux packages place gst-plugin-scanner in different locations
            // (/usr/libexec on Ubuntu, /usr/lib/<arch> on some Debian builds).
            // Do not rely only on the launcher environment: gst-discoverer is
            // started later by GSProbe and inherits this process environment.
            ConfigurePluginScanner(null);
            return;
        }

        string gstRoot = conf.PATH;
        string gstBin = string.IsNullOrWhiteSpace(gstRoot)
            ? null
            : Path.Combine(gstRoot, "bin");

        if (gstBin == null ||
            !File.Exists(Path.Combine(gstBin, "gst-discoverer-1.0.exe")))
        {
            gstRoot = Path.Combine(modpath, "gst-libs", "win-x86_64");
            gstBin = Path.Combine(gstRoot, "bin");
        }

        if (!Directory.Exists(gstBin))
            return;

        gstRootPath = gstRoot;
        conf.PATH = gstRoot;

        string gstPlugins = Path.Combine(gstRoot, "lib", "gstreamer-1.0");

        Environment.SetEnvironmentVariable(
            "GSTREAMER_1_0_ROOT_MINGW_X86_64",
            gstRoot,
            EnvironmentVariableTarget.Process
        );

        Environment.SetEnvironmentVariable(
            "GST_PLUGIN_SYSTEM_PATH_1_0",
            gstPlugins,
            EnvironmentVariableTarget.Process
        );

        ConfigurePluginScanner(gstRoot);

        string gioModules = Path.Combine(gstRoot, "lib", "gio", "modules");
        if (Directory.Exists(gioModules))
        {
            Environment.SetEnvironmentVariable(
                "GIO_EXTRA_MODULES",
                gioModules,
                EnvironmentVariableTarget.Process
            );
        }

        var currentPath = Environment.GetEnvironmentVariable("PATH");

        Environment.SetEnvironmentVariable(
            "PATH",
            string.IsNullOrEmpty(currentPath)
                ? gstBin
                : $"{gstBin}{Path.PathSeparator}{currentPath}",
            EnvironmentVariableTarget.Process
        );

        //Environment.SetEnvironmentVariable(
        //    "GST_DEBUG",
        //    "souphttpsrc:6,matroskademux:5,h264parse:4," +
        //    "hlssink3:4,splitmuxsink:4,mpegtsmux:4,*:2",
        //    EnvironmentVariableTarget.Process
        //);
    }

    static void ConfigurePluginScanner(string gstRoot)
    {
        var candidates = new List<string>();

        AddScannerCandidate(candidates, Environment.GetEnvironmentVariable("GST_PLUGIN_SCANNER"));
        AddScannerCandidate(candidates, Environment.GetEnvironmentVariable("GST_PLUGIN_SCANNER_1_0"));

        if (!string.IsNullOrWhiteSpace(gstRoot))
        {
            AddScannerCandidate(
                candidates,
                Path.Combine(gstRoot, "libexec", "gstreamer-1.0", "gst-plugin-scanner" +
                    (OperatingSystem.IsWindows() ? ".exe" : string.Empty))
            );
        }

        string[] fixedPaths =
        {
            "/usr/libexec/gstreamer-1.0/gst-plugin-scanner",
            "/usr/lib/gstreamer-1.0/gst-plugin-scanner",
            "/usr/local/libexec/gstreamer-1.0/gst-plugin-scanner",
            "/usr/local/lib/gstreamer-1.0/gst-plugin-scanner",
            "/usr/lib/aarch64-linux-gnu/gstreamer-1.0/gst-plugin-scanner",
            "/usr/lib/arm-linux-gnueabihf/gstreamer-1.0/gst-plugin-scanner",
            "/usr/lib/x86_64-linux-gnu/gstreamer-1.0/gst-plugin-scanner"
        };

        foreach (string path in fixedPaths)
            AddScannerCandidate(candidates, path);

        // Some distributions use a multiarch directory that is not one of the
        // common names above. Search only immediate /usr/lib children so this
        // startup repair does not walk the whole filesystem.
        foreach (string root in new[] { "/usr/lib", "/usr/local/lib" })
        {
            try
            {
                if (!Directory.Exists(root))
                    continue;

                foreach (string directory in Directory.EnumerateDirectories(root))
                {
                    AddScannerCandidate(
                        candidates,
                        Path.Combine(directory, "gstreamer-1.0", "gst-plugin-scanner")
                    );
                    AddScannerCandidate(
                        candidates,
                        Path.Combine(directory, "gstreamer1.0", "gst-plugin-scanner")
                    );

                    // Ubuntu ARM64 can install it one level deeper:
                    // <multiarch>/gstreamer1.0/gstreamer-1.0/gst-plugin-scanner.
                    AddScannerCandidate(
                        candidates,
                        Path.Combine(directory, "gstreamer1.0", "gstreamer-1.0", "gst-plugin-scanner")
                    );
                }
            }
            catch
            {
                // An inaccessible optional library directory must not prevent
                // the GStreamer module from loading.
            }
        }

        foreach (string candidate in candidates)
        {
            if (!File.Exists(candidate))
                continue;

            Environment.SetEnvironmentVariable(
                "GST_PLUGIN_SCANNER",
                candidate,
                EnvironmentVariableTarget.Process
            );
            Environment.SetEnvironmentVariable(
                "GST_PLUGIN_SCANNER_1_0",
                candidate,
                EnvironmentVariableTarget.Process
            );

            Serilog.Log.Information("GStreamer plugin scanner configured: {Scanner}", candidate);
            return;
        }

        Serilog.Log.Warning(
            "GStreamer plugin scanner was not found; gst-discoverer probing may fail."
        );
    }

    static void AddScannerCandidate(List<string> candidates, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || candidates.Contains(path, StringComparer.OrdinalIgnoreCase))
            return;

        candidates.Add(path);
    }

    static double? ReadGstVersion()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = OperatingSystem.IsWindows()
                        ? Path.Combine(
                            gstRootPath ?? conf.PATH,
                            "bin",
                            "gst-inspect-1.0.exe"
                        )
                        : "gst-inspect-1.0",

                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            if (!process.WaitForExit(3000))
            {
                process.Kill(true);
                return null;
            }

            foreach (string output in new string[] { process.StandardOutput.ReadToEnd(), process.StandardError.ReadToEnd() })
            {
                var match = Regex.Match(output, @"(?:GStreamer|version)\s+(\d+)\.(\d+)(?:\.\d+)?", RegexOptions.IgnoreCase);
                if (!match.Success)
                    return null;

                string major = match.Groups[1].Value;
                string minor = match.Groups[2].Value.PadLeft(2, '0');

                if (double.TryParse($"{major}.{minor}", NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double version))
                    return version;

                return null;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
