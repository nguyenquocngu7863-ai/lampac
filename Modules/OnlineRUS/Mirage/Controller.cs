using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Attributes;
using Shared.Models.AppConf;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.PlaywrightCore;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Mirage;

public class MirageController : BaseOnlineController<ModuleConf>
{
    static IPage page;
    static Timer timer;
    static (string hls, long id_file, string token_movie, int lastseek, DateTime lastreq) curenthsl = new();
    static object locker = new();
    static System.Net.Http.HttpClient dlClient = new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromSeconds(20) };
    static Dictionary<long, (string variantUrl, string referer, List<string> segs, string initMap)> dlStates = new();
    static string MirageDir(long id_file) => $"cache/mirage/{id_file}";

    static MirageController()
    {
        Directory.CreateDirectory("cache/mirage");
        CoreInit.conf.WAF.limit_map.Insert(0, new WafLimitRootMap("^/lite/mirage/trans/", new WafLimitMap { limit = 1000, second = 1 }));

        timer = new Timer(_ =>
        {
            if (page != null && DateTime.Now.AddMinutes(-5) > curenthsl.lastreq)
            {
                try
                {
                    page.CloseAsync();
                    page = null;
                    curenthsl = default;
                }
                catch { }
            }
            try
            {
                // xoa cache phim cu (>60ph) de nhe RAM/disk, giu phim dang xem
                foreach (var dir in Directory.GetDirectories("cache/mirage"))
                {
                    try
                    {
                        var di = new DirectoryInfo(dir);
                        if (DateTime.Now - di.LastWriteTime > TimeSpan.FromMinutes(60))
                            Directory.Delete(dir, true);
                    }
                    catch { }
                }
            }
            catch { }
        }, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1));
    }

    public MirageController() : base(ModInit.conf)
    {
        loadKitInitialization = (j, i, c) =>
        {
            if (j.ContainsKey("m4s"))
                i.m4s = c.m4s;
            return i;
        };
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/mirage")]
    async public Task<ActionResult> Index(string orid, string imdb_id, long kinopoisk_id, string title, string original_title, byte serial, string original_language, short year, int t = -1, short s = -1, bool origsource = false, bool rjson = false, bool similar = false)
    {
        if (similar)
            return await RouteSpiderSearch(title, origsource, rjson);

        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        var result = await search(orid, imdb_id, kinopoisk_id, title, serial, original_language, year);
        if (result.category_id == 0 || result.data == null)
            return OnError();

        JToken data = result.data;
        string tokenMovie = data["token_movie"] != null ? data.Value<string>("token_movie") : null;
        var frame = await iframe(tokenMovie);
        if (frame.all == null)
            return OnError();

        if (result.category_id is 1 or 3)
        {
            #region Фильм
            var videos = frame.all["theatrical"].ToObject<Dictionary<string, Dictionary<string, JObject>>>();

            var mtpl = new MovieTpl(title, original_title, videos.Count);

            foreach (var i in videos)
            {
                var file = i.Value.First().Value;

                string translation = file.Value<string>("translation");
                string quality = file.Value<string>("quality");
                long id = file.Value<long>("id");
                bool uhd = init.m4s ? file.Value<bool>("uhd") : false;

                string link = $"{host}/lite/mirage/video?id_file={id}&token_movie={data.Value<string>("token_movie")}";
                mtpl.Append(
                    translation,
                    link,
                    "call",
                    accsArgs($"{link.Replace("/video", "/video.m3u8")}&play=true"),
                    voice_name: uhd ? "2160p" : quality,
                    quality: uhd ? "2160p" : ""
                );
            }

            return ContentTpl(mtpl);
            #endregion
        }
        else
        {
            #region Сериал
            string defaultargs = $"&orid={orid}&imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&original_language={original_language}";

            if (s == -1)
            {
                #region Сезоны
                string q = null;

                try
                {
                    if (init.m4s)
                        q = frame.active.Value<bool>("uhd") == true ? "2160p" : null;
                }
                catch { }

                Dictionary<string, JToken> seasons;
                if (frame.all["seasons"] != null)
                    seasons = frame.all["seasons"].ToObject<Dictionary<string, JToken>>();
                else
                    seasons = frame.all.ToObject<Dictionary<string, JToken>>();

                if (seasons.First().Key.StartsWith("t"))
                {
                    var tpl = new SeasonTpl(q);

                    var seasonNumbers = new HashSet<short>();

                    foreach (var translation in seasons)
                    {
                        var file = translation.Value["file"];
                        if (file == null)
                            continue;

                        foreach (var season in file.ToObject<Dictionary<string, object>>())
                        {
                            if (short.TryParse(season.Key, out short seasonNumber))
                                seasonNumbers.Add(seasonNumber);
                        }
                    }

                    if (!seasonNumbers.Any())
                        seasonNumbers.Add(frame.active.Value<short>("seasons"));

                    foreach (short i in seasonNumbers.OrderBy(i => i))
                    {
                        tpl.Append(
                            $"{i} сезон",
                            $"{host}/lite/mirage?rjson={rjson}&s={i}{defaultargs}",
                            i.ToString()
                        );
                    }

                    return ContentTpl(tpl);
                }
                else
                {
                    var tpl = new SeasonTpl(q, seasons.Count);

                    foreach (var season in seasons)
                    {
                        tpl.Append(
                            $"{season.Key} сезон",
                            $"{host}/lite/mirage?rjson={rjson}&s={season.Key}{defaultargs}",
                            season.Key
                        );
                    }

                    return ContentTpl(tpl);
                }
                #endregion
            }
            else
            {
                var vtpl = new VoiceTpl();
                var etpl = new EpisodeTpl();
                var voices = new HashSet<int>();

                string sArhc = s.ToString();

                if (frame.all[sArhc] is JArray)
                {
                    #region Перевод
                    foreach (var episode in frame.all[sArhc])
                    {
                        foreach (var voice in episode.ToObject<Dictionary<string, JObject>>().Select(i => i.Value))
                        {
                            int id_translation = voice.Value<int>("id_translation");
                            if (voices.Contains(id_translation))
                                continue;

                            voices.Add(id_translation);

                            if (t == -1)
                                t = id_translation;

                            vtpl.Append(
                                TranslateVoice(voice.Value<string>("translation")),
                                t == id_translation,
                                $"{host}/lite/mirage?rjson={rjson}&s={s}&t={id_translation}{defaultargs}"
                            );
                        }
                    }
                    #endregion

                    foreach (var episode in frame.all[sArhc])
                    {
                        foreach (var voice in episode.ToObject<Dictionary<string, JObject>>().Select(i => i.Value))
                        {
                            if (voice.Value<int>("id_translation") != t)
                                continue;

                            string translation = TranslateVoice(voice.Value<string>("translation"));
                            short e = voice.Value<short>("episode");

                            string link = $"{host}/lite/mirage/video?id_file={voice.Value<long>("id")}&token_movie={data.Value<string>("token_movie")}";
                            string streamlink = accsArgs($"{link.Replace("/video", "/video.m3u8")}&play=true");

                            if (e > 0)
                            {
                                etpl.Append(
                                    $"{e} серия",
                                    title ?? original_title,
                                    s,
                                    e,
                                    link,
                                    "call",
                                    voice_name: translation,
                                    streamlink: streamlink
                                );
                            }
                        }
                    }
                }
                else if (frame.all.ToObject<Dictionary<string, object>>().First().Key.StartsWith("t"))
                {
                    #region Перевод
                    foreach (var node in frame.all)
                    {
                        if (!node.First["file"].ToObject<Dictionary<string, object>>().ContainsKey(sArhc))
                            continue;

                        var voice = node.First["file"].First.First.First.First;
                        int id_translation = voice.Value<int>("id_translation");
                        if (voices.Contains(id_translation))
                            continue;

                        voices.Add(id_translation);

                        if (t == -1)
                            t = id_translation;

                        vtpl.Append(
                            TranslateVoice(voice.Value<string>("translation")),
                            t == id_translation,
                            $"{host}/lite/mirage?rjson={rjson}&s={s}&t={id_translation}{defaultargs}"
                        );
                    }
                    #endregion

                    foreach (var node in frame.all)
                    {
                        foreach (var season in node.First["file"].ToObject<Dictionary<string, object>>())
                        {
                            if (season.Key != sArhc)
                                continue;

                            if (season.Value is JArray sjar)
                            {

                            }
                            else if (season.Value is JObject sjob)
                            {
                                foreach (var episode in sjob.ToObject<Dictionary<string, JObject>>())
                                {
                                    if (episode.Value.Value<int>("id_translation") != t)
                                        continue;

                                    string translation = episode.Value.Value<string>("translation");
                                    short e = episode.Value.Value<short>("episode");

                                    string link = $"{host}/lite/mirage/video?id_file={episode.Value.Value<long>("id")}&token_movie={data.Value<string>("token_movie")}";
                                    string streamlink = accsArgs($"{link.Replace("/video", "/video.m3u8")}&play=true");

                                    if (e > 0)
                                    {
                                        etpl.Append(
                                            $"{e} серия",
                                            title ?? original_title,
                                            s,
                                            e,
                                            link,
                                            "call",
                                            voice_name: translation,
                                            streamlink: streamlink
                                        );
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    #region Перевод
                    foreach (var episode in frame.all[sArhc].ToObject<Dictionary<string, Dictionary<string, JObject>>>())
                    {
                        foreach (var voice in episode.Value.Select(i => i.Value))
                        {
                            int id_translation = voice.Value<int>("id_translation");
                            if (voices.Contains(id_translation))
                                continue;

                            voices.Add(id_translation);

                            if (t == -1)
                                t = id_translation;

                            vtpl.Append(
                                TranslateVoice(voice.Value<string>("translation")),
                                t == id_translation,
                                $"{host}/lite/mirage?rjson={rjson}&s={s}&t={id_translation}{defaultargs}"
                            );
                        }
                    }
                    #endregion

                    foreach (var episode in frame.all[sArhc].ToObject<Dictionary<string, Dictionary<string, JObject>>>())
                    {
                        foreach (var voice in episode.Value.Select(i => i.Value))
                        {
                            string translation = TranslateVoice(voice.Value<string>("translation"));
                            if (voice.Value<int>("id_translation") != t)
                                continue;

                            string link = $"{host}/lite/mirage/video?id_file={voice.Value<long>("id")}&token_movie={data.Value<string>("token_movie")}";
                            string streamlink = accsArgs($"{link.Replace("/video", "/video.m3u8")}&play=true");

                            etpl.Append(
                                $"{episode.Key} серия",
                                title ?? original_title,
                                s,
                                episode.Key,
                                link,
                                "call",
                                voice_name: translation,
                                streamlink: streamlink
                            );
                        }
                    }
                }

                etpl.Append(vtpl);

                return ContentTpl(etpl);
            }
            #endregion
        }
    }


    #region Video
    [HttpGet, Staticache(manually: true)]
    [Route("lite/mirage/video")]
    [Route("lite/mirage/video.m3u8")]
    async public Task<ActionResult> Video(long id_file, string token_movie, bool play)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        string hls = null;

        if (curenthsl.id_file == id_file && curenthsl.token_movie == token_movie)
            hls = curenthsl.hls;
        else
        {
            hls = await goMovie($"{init.linkhost}/?token_movie={token_movie}&token={init.token}", id_file);
            if (hls == null)
                return OnError();

            curenthsl = (hls, id_file, token_movie, 0, DateTime.Now);
        }

        if (play)
            return Redirect(hls);

        return ContentTo(VideoTpl.ToJson(
            "play",
            hls,
            "auto",
            vast: init.vast,
            httpContext: HttpContext
        ));
    }

    [HttpGet]
    [AllowAnonymous]
    [Route("lite/mirage/trans/{id_file:long}/{fileName}")]
    [Route("lite/mirage/trans/{fileName}")]
    async public Task<ActionResult> Trans(string fileName, long id_file = 0)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        if (!Regex.IsMatch(fileName, "^([a-z0-9\\-]+\\.[a-z0-9]+)$"))
            return BadRequest();

        curenthsl.lastreq = DateTime.Now;

        if (id_file == 0)
            id_file = curenthsl.id_file;

        string dir = MirageDir(id_file);
        string path = $"{dir}/{fileName}";
        int.TryParse(Regex.Match(fileName, "seg-([0-9]+)").Groups[1].Value, out int indexSeg);

        if (indexSeg > 20)
        {
            try
            {
                string oldpath = $"{dir}/{fileName.Replace($"seg-{indexSeg}", $"seg-{indexSeg - 4}")}";
                if (System.IO.File.Exists(oldpath))
                    System.IO.File.Delete(oldpath);
            }
            catch { }
        }

        var timeout = TimeSpan.FromSeconds(20);
        var sw = Stopwatch.StartNew();

        while (!System.IO.File.Exists(path) && sw.Elapsed < timeout)
        {
            if (fileName == "master.m3u8")
            {
                await Task.Delay(1_000);
                continue;
            }

            if (await FetchSegOnDemand(id_file, indexSeg))
                break;

            if (indexSeg > 0)
            {
                int seek = (indexSeg * 6) - 10;
                if (seek > 90 && curenthsl.lastseek != seek)
                {
                    curenthsl.lastseek = seek;

                    try
                    {
                        if (page != null)
                        {
                            await page.EvaluateAsync(@"() => 
                                    document.getElementById('player').contentWindow.postMessage(
                                      JSON.stringify({
                                        api: ""seek"",
                                        value: " + seek + @"
                                      }),
                                      ""*""
                                    );
                                ");
                        }
                    }
                    catch { }
                }

                await Task.Delay(4_000);
            }
            else
            {
                await Task.Delay(1_000);
            }
        }

        string type = fileName.Contains(".m3u")
            ? "application/vnd.apple.mpegurl"
            : "video/MP2T";

        return File(System.IO.File.OpenRead(path), type);
    }
    #endregion


    #region iframe
    async Task<(JToken all, JToken active)> iframe(string token_movie)
    {
        if (string.IsNullOrEmpty(token_movie))
            return default;

        string memKey = $"mirage:iframe:{token_movie}";
        if (!hybridCache.TryGetValue(memKey, out (JToken all, JToken active) cache))
        {
            string json = null;
            string uri = $"{init.linkhost}/?token_movie={token_movie}&token={init.token}";

            await httpHydra.GetSpan(uri, safety: true, addheaders: HeadersModel.Init(
                ("accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7"),
                ("referer", "https://kinogo-go.tv/"),
                ("sec-fetch-dest", "iframe"),
                ("sec-fetch-mode", "navigate"),
                ("sec-fetch-site", "cross-site"),
                ("upgrade-insecure-requests", "1")
            ),
            spanAction: html =>
            {
                json = Rx.Match(html, "fileList = JSON.parse\\('([^\n\r]+)'\\);");
            });

            if (string.IsNullOrEmpty(json))
                return default;

            try
            {
                var root = JsonConvert.DeserializeObject<JObject>(json);
                if (root == null || !root.ContainsKey("all"))
                    return default;

                cache = (root["all"], root["active"]);

                hybridCache.Set(memKey, cache, cacheTime(40));
            }
            catch { return default; }
        }

        return cache;
    }
    #endregion

    #region goMovie
    async Task<string> goMovie(string uri, long id_file)
    {
        try
        {
            var browser = new PlaywrightBrowser();

            if (page != null)
            {
                try { await page.CloseAsync(); } catch { }
                page = null;
            }

            try
            {
                foreach (var file in Directory.GetFiles(MirageDir(id_file)))
                    System.IO.File.Delete(file);
            }
            catch { }

            page = await browser.NewPageAsync(init.plugin, proxy: proxy_data, keepopen: false).ConfigureAwait(false);
            if (page == null)
                return default;

            await page.RouteAsync("**/*", async route =>
            {
                try
                {
                    if (route.Request.Url.Contains("kinogo-go.tv"))
                    {
                        await route.FulfillAsync(new RouteFulfillOptions
                        {
                            Body = PlaywrightBase.IframeHtml(uri + "&autoplay")
                        });
                    }
                    else if (route.Request.Url.Contains("/?token_movie="))
                    {
                        var fetchHeaders = route.Request.Headers;
                        fetchHeaders.TryAdd("accept-encoding", "gzip, deflate, br, zstd");
                        fetchHeaders.TryAdd("cache-control", "no-cache");
                        fetchHeaders.TryAdd("pragma", "no-cache");
                        fetchHeaders.TryAdd("sec-fetch-dest", "iframe");
                        fetchHeaders.TryAdd("sec-fetch-mode", "navigate");
                        fetchHeaders.TryAdd("sec-fetch-site", "cross-site");
                        fetchHeaders.TryAdd("sec-fetch-storage-access", "active");

                        var fetchResponse = await route.FetchAsync(new RouteFetchOptions
                        {
                            Url = route.Request.Url,
                            Method = "GET",
                            Headers = fetchHeaders,
                        }).ConfigureAwait(false);

                        string body = await fetchResponse.TextAsync().ConfigureAwait(false);

                        var injected = @"
                                <script>
                                (function() {
                                    try { localStorage.setItem('allplay', '{""volume"":0.5,""muted"":true}'); } catch(e) {}
                                })();
                                </script>";

                        await route.FulfillAsync(new RouteFulfillOptions
                        {
                            Status = fetchResponse.Status,
                            Body = injected + body,
                            Headers = fetchResponse.Headers
                        }).ConfigureAwait(false);
                    }
                    else if (route.Request.Method == "POST" && route.Request.Url.Contains("/movies/"))
                    {
                        string newUrl = Regex.Replace(route.Request.Url, "/[0-9]+$", $"/{id_file}");

                        var fetchHeaders = route.Request.Headers;
                        fetchHeaders.TryAdd("accept-encoding", "gzip, deflate, br, zstd");
                        fetchHeaders.TryAdd("cache-control", "no-cache");
                        fetchHeaders.TryAdd("dnt", "1");
                        fetchHeaders.TryAdd("pragma", "no-cache");
                        fetchHeaders.TryAdd("priority", "u=1, i");
                        fetchHeaders.TryAdd("sec-fetch-dest", "empty");
                        fetchHeaders.TryAdd("sec-fetch-mode", "cors");
                        fetchHeaders.TryAdd("sec-fetch-site", "same-origin");
                        fetchHeaders.TryAdd("sec-fetch-storage-access", "active");

                        var fetchResponse = await route.FetchAsync(new RouteFetchOptions
                        {
                            Url = newUrl,
                            Method = "POST",
                            Headers = fetchHeaders,
                            PostData = route.Request.PostDataBuffer
                        }).ConfigureAwait(false);

                        string json = await fetchResponse.TextAsync().ConfigureAwait(false);

                        _ = Task.Run(async () =>
                        {
                            try { await ServerFetchHls(id_file, json, uri); } catch { }
                        });

                        await route.FulfillAsync(new RouteFulfillOptions
                        {
                            Status = fetchResponse.Status,
                            Body = json,
                            Headers = fetchResponse.Headers
                        }).ConfigureAwait(false);
                    }
                    else
                    {
                        if (route.Request.Url.Contains("/stat") || route.Request.Url.Contains("/lists.php"))
                        {
                            await route.AbortAsync();
                            return;
                        }

                        await route.ContinueAsync();
                    }
                }
                catch { }
            });

            TaskCompletionSource<bool> tcsPageResponse = new();

            page.Response += async (s, e) =>
            {
                if (e.Request.Method == "GET")
                {
                    try
                    {
                        if ((e.Url.Contains(".ts") || e.Url.Contains(".m4s")) && !tcsPageResponse.Task.IsCompleted)
                        {
                            tcsPageResponse.SetResult(true);

                            await page.EvaluateAsync(@"() => 
                                    document.getElementById('player').contentWindow.postMessage(
                                      JSON.stringify({
                                        api: ""pause""
                                      }),
                                      ""*""
                                    );
                                ");
                        }
                    }
                    catch { }

                    if (e.Url.Contains(".m3u8") ||
                        e.Url.Contains(".ts") ||
                        e.Url.Contains(".mp4") ||
                        e.Url.Contains(".m4s"))
                    {
                        try
                        {
                            string fn = Path.GetFileName(e.Url);
                            string dir = MirageDir(id_file);
                            // master.m3u8 server-side rewrite uu tien, khong de browser ghi de
                            if (fn == "master.m3u8" || (fn.EndsWith(".m3u8") && System.IO.File.Exists($"{dir}/master.m3u8")))
                                return;
                            Directory.CreateDirectory(dir);
                            var file = await e.BodyAsync();
                            System.IO.File.WriteAllBytes($"{dir}/{fn}", file);
                        }
                        catch { }

                        //if (e.Url.Contains(".ts"))
                        //{
                        //    try
                        //    {
                        //        lock (locker)
                        //        {
                        //            string log = e.Url +
                        //                "\n\n" +
                        //                string.Join("\n", e.Request.Headers.Select(i => i.Key + ": " + i.Value)) +
                        //                "\n\n" +
                        //                string.Join("\n", e.Headers.Select(i => i.Key + ": " + i.Value));

                        //            System.IO.File.AppendAllText("cache/miragelog.txt", log + "\n\n======================\n\n\n");
                        //        }
                        //    }
                        //    catch { }
                        //}
                    }
                }
            };

            PlaywrightBase.GotoAsync(page, "https://kinogo-go.tv/");

            _ = Task.Run(async () =>
            {
                try
                {
                    var deadline = DateTime.Now.AddSeconds(25);
                    int tick = 0;
                    while (DateTime.Now < deadline && !tcsPageResponse.Task.IsCompleted)
                    {
                        if (tick % 10 == 0)
                        {
                            foreach (var frame in page.Frames)
                            {
                                try
                                {
                                    await frame.EvaluateAsync(@"() => {
                                        var b = document.querySelector('button');
                                        if (b) b.click();
                                        var v = document.querySelector('video');
                                        if (v) { v.muted = true; v.play().catch(() => {}); }
                                    }");
                                }
                                catch { }
                            }
                        }
                        tick++;
                        await Task.Delay(100);
                    }
                }
                catch { }
            });

            bool tcsOk = false;
            try
            {
                var tcsTask = tcsPageResponse.Task;
                var masterTask = WaitMasterFile(id_file);
                var done = await Task.WhenAny(tcsTask, masterTask);
                if (done == tcsTask)
                {
                    try { tcsOk = await tcsTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
                }
            }
            catch { }
            if (tcsOk || System.IO.File.Exists($"{MirageDir(id_file)}/master.m3u8"))
                return $"{host}/lite/mirage/trans/{id_file}/master.m3u8";
            else
            {
                try { await page.CloseAsync(); } catch { }
                page = null;
                return default;
            }
        }
        catch
        {
            if (page != null)
            {
                try { await page.CloseAsync(); } catch { }
                page = null;
            }

            return default;
        }
    }

    #region ServerFetchHls
    static async Task ServerFetchHls(long id_file, string json, string pageUri)
    {
        try
        {
            var jo = JObject.Parse(json);
            var hlsArr = jo["hlsSource"]?.FirstOrDefault();
            var qtoken = hlsArr?["quality"];
            var qlist = qtoken == null ? null : qtoken.Children<JProperty>().ToList();
            if (qlist == null || qlist.Count == 0)
                return;

            var q = qlist.FirstOrDefault(x => x.Name == "1080") ?? qlist.FirstOrDefault();
            string masterUrl = ((string)q.Value).Split(new[] { " or " }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
            if (string.IsNullOrWhiteSpace(masterUrl) || !masterUrl.StartsWith("http"))
                return;

            string referer = "https://aport-as.allarknow.online/";
            string origin = "https://aport-as.allarknow.online";

            string master = await DlText(masterUrl, referer, origin);
            if (string.IsNullOrWhiteSpace(master) || !master.Contains("#EXTM3U"))
                return;

            string baseUrl = masterUrl.Substring(0, masterUrl.LastIndexOf('/') + 1);
            // chon variant thap nhat (cuoi playlist) de nhe CPU/RAM Termux
            var variants = master.Split('\n').Select(l => l.Trim()).Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#")).ToList();
            if (variants.Count == 0)
                return;
            string variantFile = variants[variants.Count - 1];

            string variantUrl = variantFile.StartsWith("http") ? variantFile : baseUrl + variantFile;
            string variant = await DlText(variantUrl, referer, origin);
            if (string.IsNullOrWhiteSpace(variant) || !variant.Contains("#EXTINF"))
                return;

            string vbase = variantUrl.Substring(0, variantUrl.LastIndexOf('/') + 1);
            string initMap = Regex.Match(variant, "#EXT-X-MAP:URI=\"([^\"]+)\"").Groups[1].Value;

            var segs = new List<string>();
            foreach (var line in variant.Split('\n'))
            {
                var t = line.Trim();
                if (t.StartsWith("seg-"))
                    segs.Add(t);
            }
            if (segs.Count == 0)
                return;

            dlStates[id_file] = (variantUrl, referer, segs, initMap);

            string dir = MirageDir(id_file);
            Directory.CreateDirectory(dir);

            if (!string.IsNullOrWhiteSpace(initMap))
            {
                var initBytes = await DlBytes(vbase + initMap, referer, origin);
                if (initBytes != null)
                    await System.IO.File.WriteAllBytesAsync($"{dir}/init.mp4", initBytes);
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("#EXTM3U");
            sb.AppendLine("#EXT-X-VERSION:6");
            sb.AppendLine("#EXT-X-TARGETDURATION:7");
            sb.AppendLine("#EXT-X-PLAYLIST-TYPE:VOD");
            sb.AppendLine("#EXT-X-MEDIA-SEQUENCE:1");
            if (!string.IsNullOrWhiteSpace(initMap))
                sb.AppendLine("#EXT-X-MAP:URI=\"init.mp4\"");
            int n = 0;
            foreach (var s in segs)
            {
                n++;
                sb.AppendLine("#EXTINF:6.000,");
                sb.AppendLine($"seg-{n}.m4s");
            }
            sb.AppendLine("#EXT-X-ENDLIST");
            await System.IO.File.WriteAllTextAsync($"{dir}/master.m3u8", sb.ToString());

            // tai vai seg dau de Trans() co file ngay
            for (int i = 0; i < Math.Min(3, segs.Count); i++)
            {
                var b = await DlBytes(vbase + segs[i], referer, origin);
                if (b != null)
                {
                    try { await System.IO.File.WriteAllBytesAsync($"{dir}/seg-{i + 1}.m4s", b); } catch { }
                }
            }
        }
        catch { }
    }

    static async Task<string> DlText(string url, string referer, string origin)
    {
        try
        {
            using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            req.Headers.Referrer = new Uri(referer);
            req.Headers.TryAddWithoutValidation("Origin", origin);
            using var res = await dlClient.SendAsync(req);
            if (!res.IsSuccessStatusCode)
                return null;
            return await res.Content.ReadAsStringAsync();
        }
        catch { return null; }
    }

    static async Task<byte[]> DlBytes(string url, string referer, string origin)
    {
        try
        {
            using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            req.Headers.Referrer = new Uri(referer);
            req.Headers.TryAddWithoutValidation("Origin", origin);
            using var res = await dlClient.SendAsync(req);
            if (!res.IsSuccessStatusCode)
                return null;
            return await res.Content.ReadAsByteArrayAsync();
        }
        catch { return null; }
    }

    static async Task<bool> WaitMasterFile(long id_file)
    {
        try
        {
            string dir = MirageDir(id_file);
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(35))
            {
                if (System.IO.File.Exists($"{dir}/master.m3u8"))
                    return true;
                await Task.Delay(1_000);
            }
        }
        catch { }
        return false;
    }

    public static async Task<bool> FetchSegOnDemand(long id_file, int indexSeg)
    {
        try
        {
            if (!dlStates.TryGetValue(id_file, out var st))
                return false;
            if (st.segs == null || indexSeg < 1 || indexSeg > st.segs.Count)
                return false;

            string path = $"{MirageDir(id_file)}/seg-{indexSeg}.m4s";
            if (System.IO.File.Exists(path))
                return true;

            string vbase = st.variantUrl.Substring(0, st.variantUrl.LastIndexOf('/') + 1);
            var b = await DlBytes(vbase + st.segs[indexSeg - 1], st.referer, "https://aport-as.allarknow.online");
            if (b == null)
                return false;

            await System.IO.File.WriteAllBytesAsync(path, b);
            return true;
        }
        catch { return false; }
    }
    #endregion
    #endregion


    #region SpiderSearch
    [HttpGet, Staticache(manually: true)]
    [Route("lite/mirage-search")]

    static string TranslateVoice(string v)
    {
        if (string.IsNullOrEmpty(v)) return v;
        var s = v.Trim();
        return s switch
        {
            "Украинский" => "Tiếng Ukraina",
            "Казахский" => "Tiếng Kazakhstan",
            "Узбекский" => "Tiếng Uzbekistan",
            "Дублированный" => "Lồng tiếng",
            "Оригинальный" => "Gốc",
            "Оригинал" => "Gốc",
            "Русский" => "Tiếng Nga",
            "Английский" => "Tiếng Anh",
            "Дубляж" => "Lồng tiếng",
            "Многоголосый" => "Thuyết minh đa giọng",
            "Двухголосый" => "Thuyết minh đôi",
            "Одноголосый" => "Thuyết minh đơn",
            "Субтитры" => "Phụ đề",
            _ => s.Contains("Многоголосый") ? "Thuyết minh đa giọng" : s.Contains("Дублированный") ? "Lồng tiếng" : s.Contains("Оригинальн") ? "Gốc" : v
        };
    }

    async public Task<ActionResult> RouteSpiderSearch(string title, bool origsource = false, bool rjson = false)
    {
        if (string.IsNullOrWhiteSpace(title))
            return OnError();

        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        var cache = await InvokeCacheResult<JArray>($"mirage:search:{title}", 40, async e =>
        {
            var root = await httpHydra.Get<JObject>($"{init.apihost}/?token={init.token}&name={HttpUtility.UrlEncode(title)}&list", safety: true);
            if (root == null || !root.ContainsKey("data"))
                return e.Fail("data");

            return e.Success(root["data"].ToObject<JArray>());
        });

        return ContentTpl(cache, () =>
        {
            var stpl = new SimilarTpl(cache.Value.Count);

            foreach (var j in cache.Value)
            {
                stpl.Append(
                    j.Value<string>("name") ?? j.Value<string>("original_name"),
                    j.Value<int>("year").ToString(),
                    string.Empty,
                    $"{host}/lite/mirage?orid={j.Value<string>("token_movie")}",
                    PosterApi.Size(j.Value<string>("poster"))
                );
            }

            return stpl;
        });
    }
    #endregion

    #region search
    async ValueTask<(bool refresh_proxy, int category_id, JToken data)> search(string token_movie, string imdb_id, long kinopoisk_id, string title, int serial, string original_language, int year)
    {
        string memKey = $"mirage:view:{kinopoisk_id}:{imdb_id}";
        if (0 >= kinopoisk_id && string.IsNullOrEmpty(imdb_id))
            memKey = $"mirage:viewsearch:{title}:{serial}:{original_language}:{year}";

        if (!string.IsNullOrEmpty(token_movie))
            memKey = $"mirage:view:{token_movie}";

        JObject root;

        if (!hybridCache.TryGetValue(memKey, out (int category_id, JToken data) res))
        {
            string stitle = title.ToLowerAndTrim();

            if (memKey.Contains(":viewsearch:"))
            {
                if (string.IsNullOrWhiteSpace(title) || year == 0)
                    return default;

                root = await httpHydra.Get<JObject>($"{init.apihost}/?token={init.token}&name={HttpUtility.UrlEncode(title)}&list={(serial == 1 ? "serial" : "movie")}", safety: true);
                if (root == null)
                    return (true, 0, null);

                if (root.ContainsKey("data"))
                {
                    foreach (var item in root["data"])
                    {
                        if (item.Value<string>("name")?.ToLowerAndTrim() == stitle)
                        {
                            int y = item.Value<int>("year");
                            if (y > 0 && (y == year || y == (year - 1) || y == (year + 1)))
                            {
                                if (original_language == "ru" && item.Value<string>("country")?.ToLowerAndTrim() != "россия")
                                    continue;

                                res.data = item;
                                res.category_id = item.Value<int>("category_id");
                                break;
                            }
                        }
                    }
                }
            }
            else
            {
                root = await httpHydra.Get<JObject>($"{init.apihost}/?token={init.token}&kp={kinopoisk_id}&imdb={imdb_id}&token_movie={token_movie}", safety: true);
                if (root == null)
                    return (true, 0, null);

                if (root.ContainsKey("data"))
                {
                    res.data = root.GetValue("data");
                    res.category_id = res.data.Value<int>("category");
                }
            }

            if (res.data != null || (root.ContainsKey("error_info") && root.Value<string>("error_info") == "not movie"))
                hybridCache.Set(memKey, res, cacheTime(res.category_id is 1 or 3 ? 120 : 40));
            else
                hybridCache.Set(memKey, res, cacheTime(2));
        }

        return (false, res.category_id, res.data);
    }
    #endregion
}
