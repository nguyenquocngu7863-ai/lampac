using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace SISI;

/// <summary>
/// Kiểm sức khoẻ các module Adult đang bật bằng cách gọi chính route của app
/// (127.0.0.1) và đếm item trả về. Không scrape thêm gì, mỗi module 1 request/lần.
/// </summary>
public static class HealthMonitor
{
    const string cachePath = "cache/sisi-health.json";

    static readonly Serilog.ILogger Log = Serilog.Log.ForContext("SourceContext", "HealthMonitor");
    static readonly object _lock = new();
    static readonly Dictionary<string, HealthItem> items = new(StringComparer.OrdinalIgnoreCase);
    static Timer _timer;
    static volatile int _running;

    public static DateTime lastRun { get; private set; }
    public static string lastError { get; private set; }
    public static bool running => _running == 1;

    public static void Start()
    {
        Load();
        if (_timer != null)
            return;

        // tick 5 phút, tự quyết định có tới giờ chạy chưa (interval đọc từ conf mỗi lần)
        _timer = new Timer(_ => Tick(), null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5));
    }

    public static void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    async static void Tick()
    {
        try
        {
            var conf = ModInit.conf?.health;
            if (conf == null || !conf.enable)
                return;

            if (lastRun != default && DateTime.UtcNow < lastRun.AddHours(Math.Max(1, conf.interval)))
                return;

            await Run(conf);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "{Class} {CatchId}", "HealthMonitor", "id_hmon_tick");
        }
    }

    public static async Task<(bool ok, string error, int total)> Run(HealthConf conf = null)
    {
        conf ??= ModInit.conf?.health;
        if (conf == null || !conf.enable)
            return Fail("health đang tắt (bật trong init.conf: SISI.health.enable)");

        if (Interlocked.Exchange(ref _running, 1) == 1)
            return Fail("đang kiểm rồi, chờ xong (running=true)");

        try
        {
            var listen = CoreInit.conf.listen;
            string host = $"{(string.IsNullOrEmpty(listen.scheme) ? "http" : listen.scheme)}://{listen.localhost ?? "127.0.0.1"}:{listen.port}";

            var (json, resp) = await Http.BaseGet($"{host}/sisi", timeoutSeconds: Math.Max(5, conf.timeout));
            if (resp == null || (int)resp.StatusCode != 200 || string.IsNullOrEmpty(json))
                return Fail($"không đọc được /sisi (http {(int)resp?.StatusCode})");

            var routes = AdultRoutes();
            if (routes.Count == 0)
                return Fail("không thấy thư mục module/Adult");

            var targets = new List<(string name, string path)>();
            foreach (var channel in (JObject.Parse(json)["channels"] as JArray) ?? new JArray())
            {
                string url = channel.Value<string>("playlist_url");
                if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    continue;

                string path = uri.AbsolutePath;
                if (!IsTarget(path, conf, routes))
                    continue;

                targets.Add((channel.Value<string>("title") ?? path, path));
            }

            if (targets.Count == 0)
                return Fail("không có route nào để kiểm (xem health.modules)");

            var bag = new ConcurrentBag<HealthItem>();
            int parallel = Math.Clamp(conf.parallel <= 0 ? 3 : conf.parallel, 1, 8);

            await Parallel.ForEachAsync(targets, new ParallelOptions { MaxDegreeOfParallelism = parallel },
                async (target, _) => bag.Add(await Probe(host, target.name, target.path, conf)));

            lock (_lock)
            {
                items.Clear();
                foreach (var item in bag)
                    items[item.path] = item;
            }

            lastRun = DateTime.UtcNow;
            lastError = null;
            Save();
            return (true, null, bag.Count);
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    public static JObject Snapshot()
    {
        var conf = ModInit.conf?.health;

        List<HealthItem> list;
        lock (_lock)
            list = items.Values.ToList();

        int rank(HealthItem i) => i.status switch { "error" => 0, "empty" => 1, _ => 2 };

        var ordered = list
            .OrderBy(rank)
            .ThenByDescending(i => i.ms)
            .ToList();

        return new JObject
        {
            ["lastRun"] = lastRun == default ? null : lastRun,
            ["interval"] = conf?.interval ?? 0,
            ["running"] = running,
            ["lastError"] = lastError,
            ["summary"] = new JObject
            {
                ["total"] = ordered.Count,
                ["ok"] = ordered.Count(i => i.status == "ok"),
                ["empty"] = ordered.Count(i => i.status == "empty"),
                ["error"] = ordered.Count(i => i.status == "error")
            },
            ["items"] = JArray.FromObject(ordered)
        };
    }

    #region helpers
    static (bool, string, int) Fail(string error)
    {
        lastError = error;
        return (false, error, 0);
    }

    /// <summary>route của mọi module có mặt trong module/Adult (tên thư mục = route)</summary>
    static HashSet<string> AdultRoutes()
    {
        var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const string root = "module/Adult";
        if (!Directory.Exists(root))
            return routes;

        foreach (var dir in Directory.EnumerateDirectories(root))
            routes.Add("/" + Path.GetFileName(dir).ToLowerInvariant());

        return routes;
    }

    static bool IsTarget(string path, HealthConf conf, HashSet<string> routes)
    {
        string p = path.TrimEnd('/');
        if (p.Length == 0)
            return false;

        if (conf.exclude != null && conf.exclude.Any(x => SameRoute(x, p)))
            return false;

        if (conf.modules != null && conf.modules.Length > 0)
            return conf.modules.Any(x => SameRoute(x, p));

        return routes.Contains(p);
    }

    static bool SameRoute(string item, string path)
        => ("/" + item.Trim('/')).Equals(path, StringComparison.OrdinalIgnoreCase);

    static async Task<HealthItem> Probe(string host, string name, string path, HealthConf conf)
    {
        var item = new HealthItem { name = name, path = path, updated = DateTime.UtcNow };
        var sw = Stopwatch.StartNew();

        try
        {
            var (json, resp) = await Http.BaseGet(host + path, timeoutSeconds: Math.Max(5, conf.timeout), statusCodeOK: false);
            item.code = resp == null ? 0 : (int)resp.StatusCode;

            if (item.code != 200)
            {
                item.status = "error";

                // module trả lỗi dạng {"error":true,"msg":"..."} — lấy msg để biết lý do thật
                // (vd "host not allowed: vjav.com" khi worker relay thiếu ALLOW)
                string msg = null;
                try { msg = JObject.Parse(json ?? "")?.Value<string>("msg"); } catch { }

                item.error = string.IsNullOrEmpty(msg) ? $"http {item.code}" : $"http {item.code}: {msg}";
                return item;
            }

            var jo = JObject.Parse(json ?? "");
            if (jo.Value<bool?>("error") == true)
            {
                item.status = "error";
                item.error = jo.Value<string>("msg") ?? "error";
                return item;
            }

            var list = jo["list"] as JArray;
            if (list == null)
            {
                item.status = "error";
                item.error = "response không có list";
                return item;
            }

            item.items = list.Count;
            item.total = jo.Value<int?>("total_pages") ?? 0;
            item.status = item.items > 0 ? "ok" : "empty";
        }
        catch (Exception ex)
        {
            item.status = "error";
            item.error = ex.Message.Length > 160 ? ex.Message[..160] : ex.Message;
        }
        finally
        {
            item.ms = sw.ElapsedMilliseconds;
        }

        return item;
    }

    static void Save()
    {
        try
        {
            Directory.CreateDirectory("cache");

            var payload = new JObject
            {
                ["lastRun"] = lastRun,
                ["items"] = JArray.FromObject(items.Values.ToList())
            };

            File.WriteAllText(cachePath, payload.ToString(Formatting.Indented));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "{Class} {CatchId}", "HealthMonitor", "id_hmon_save");
        }
    }

    static void Load()
    {
        try
        {
            if (!File.Exists(cachePath))
                return;

            var jo = JObject.Parse(File.ReadAllText(cachePath));
            lastRun = jo.Value<DateTime?>("lastRun") ?? default;

            lock (_lock)
            {
                foreach (var node in (jo["items"] as JArray) ?? new JArray())
                {
                    var item = node.ToObject<HealthItem>();
                    if (item?.path != null)
                        items[item.path] = item;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "{Class} {CatchId}", "HealthMonitor", "id_hmon_load");
        }
    }
    #endregion
}
