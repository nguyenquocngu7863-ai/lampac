using Shared.Services;
using System;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Shared.Services.Utilities;

namespace LampaWeb;

public static class LampaCron
{
    static string currentapp;

    public static void Start()
    {
        var init = ModInit.conf;
        _cronTimer = new Timer(cron, null, TimeSpan.Zero, TimeSpan.FromMinutes(Math.Max(init.intervalupdate, 5)));
    }

    public static void Stop()
    {
        _cronTimer.Dispose();
    }

    static Timer _cronTimer;

    static int _updatingDb = 0;

    async static void cron(object state)
    {
        if (Interlocked.Exchange(ref _updatingDb, 1) == 1)
            return;

        try
        {
            var init = ModInit.conf;
            bool istree = !string.IsNullOrEmpty(init.tree);

            async Task<bool> update()
            {
                if (!init.autoupdate)
                    return false;

                if (!File.Exists("wwwroot/lampa-main/app.min.js"))
                    return true;

                if (istree && File.Exists("wwwroot/lampa-main/tree") && init.tree == File.ReadAllText("wwwroot/lampa-main/tree"))
                    return false;

                bool changeversion = false;

                await Http.GetSpan($"https://raw.githubusercontent.com/{init.git}/{(istree ? init.tree : "main")}/app.min.js", gitapp =>
                {
                    if (!gitapp.Contains("author: 'Yumata'", StringComparison.Ordinal))
                        return;

                    if (currentapp == null)
                        currentapp = CrypTo.md5File("wwwroot/lampa-main/app.min.js");

                    if (!string.IsNullOrEmpty(currentapp) && CrypTo.md5(gitapp) != currentapp)
                        changeversion = true;

                });

                if (istree)
                    File.WriteAllText("wwwroot/lampa-main/tree", init.tree);

                return changeversion;
            }

            if (await update())
            {
                string uri = istree ?
                    $"https://github.com/{init.git}/archive/{init.tree}.zip" :
                    $"https://github.com/{init.git}/archive/refs/heads/main.zip";

                byte[] array = await Http.Download(uri);
                if (array != null)
                {
                    currentapp = null;
                    string repo = init.git.Split('/')[^1];
                    var targetDirectory = $"{repo}-{(istree ? init.tree : "main")}";
                    var isDefaultDirectory = targetDirectory.Equals("lampa-main", StringComparison.OrdinalIgnoreCase);

                    await File.WriteAllBytesAsync("wwwroot/lampa.zip", array);
                    ZipFile.ExtractToDirectory("wwwroot/lampa.zip", "wwwroot/", overwriteFiles: true);

                    if (!isDefaultDirectory)
                    {
                        foreach (string infilePath in Directory.GetFiles($"wwwroot/{targetDirectory}", "*", SearchOption.AllDirectories))
                        {
                            string outfile = infilePath.Replace($"{targetDirectory}", "lampa-main");
                            Directory.CreateDirectory(Path.GetDirectoryName(outfile));
                            File.Copy(infilePath, outfile, true);
                        }

                        File.WriteAllText("wwwroot/lampa-main/tree", init.tree);
                    }

                    string html = File.ReadAllText("wwwroot/lampa-main/index.html");
                    html = html.Replace("</body>", "<script src=\"/lampainit.js\"></script></body>");

                    File.WriteAllText("wwwroot/lampa-main/index.html", html);
                    File.CreateText("wwwroot/lampa-main/personal.lampa");

                    if (!File.Exists("wwwroot/lampa-main/plugins_black_list.json"))
                        File.WriteAllText("wwwroot/lampa-main/plugins_black_list.json", "[]");

                    if (!File.Exists("wwwroot/lampa-main/plugins/modification.js"))
                    {
                        Directory.CreateDirectory("wwwroot/lampa-main/plugins");
                        File.WriteAllText("wwwroot/lampa-main/plugins/modification.js", string.Empty);
                    }

                    File.Delete("wwwroot/lampa.zip");

                    if (!isDefaultDirectory)
                        Directory.Delete($"wwwroot/{targetDirectory}", true);
                }
            }

            // The frontend archive can be installed after setup-termux --sync,
            // or replace lang/meta.js during a later automatic update. Reapply
            // the custom language every cron pass after the frontend is ready.
            InstallVietnameseLanguage();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "{Class} {CatchId}", "LampaCron", "id_30qftt0j");
        }
        finally
        {
            Volatile.Write(ref _updatingDb, 0);
        }
    }

    static void InstallVietnameseLanguage()
    {
        string source = Path.Combine(ModInit.modpath, "lang", "vi.js");
        if (!File.Exists(source) || !File.Exists("wwwroot/lampa-main/app.min.js"))
            return;

        string langDirectory = "wwwroot/lampa-main/lang";
        Directory.CreateDirectory(langDirectory);

        string target = Path.Combine(langDirectory, "vi.js");
        if (!File.Exists(target) || CrypTo.md5File(source) != CrypTo.md5File(target))
            File.Copy(source, target, true);

        string metaPath = Path.Combine(langDirectory, "meta.js");
        if (!File.Exists(metaPath))
            return;

        string meta = File.ReadAllText(metaPath);
        if (Regex.IsMatch(meta, @"(^|[,\{])\s*vi\s*:", RegexOptions.Multiline))
            return;

        const string entry = "vi: { code: \"vi\", name: \"Tiếng Việt\", lang_choice_title: \"Chào mừng\", lang_choice_subtitle: \"Chọn ngôn ngữ của bạn\" },";
        string patched = Regex.Replace(
            meta,
            @"languages\s*:\s*\{",
            match => match.Value + " " + entry,
            RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1)
        );

        if (!ReferenceEquals(patched, meta) && patched != meta)
            File.WriteAllText(metaPath, patched);
    }
}
