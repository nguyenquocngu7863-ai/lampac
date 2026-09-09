using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared.Models.Online.Settings;
using Shared.Models.Templates;
using Shared.Services;
using System.Web;

namespace Shared;

public class BaseENGController : BaseOnlineController
{
    public BaseENGController(OnlinesSettings init) : base(init) { }

    static readonly int hlsTimeout = (int)TimeSpan.FromSeconds(30).TotalMilliseconds;

    public Task<ActionResult> ViewTmdb(bool checksearch, long id, long tmdb_id, string imdb_id, string title, string original_title, byte serial, short s = -1, bool rjson = false, bool mp4 = false, string method = "play", int hls_manifest_timeout = 0, string extension = "m3u8")
    {
        if (checksearch)
            return Task.FromResult<ActionResult>(Content("data-json=", "application/json; charset=utf-8"));

        return ViewTmdbAsync(id, tmdb_id, imdb_id, title, original_title, serial, s, rjson, mp4, method, hls_manifest_timeout, extension);
    }

    async Task<ActionResult> ViewTmdbAsync(long id, long tmdb_id, string imdb_id, string title, string original_title, byte serial, short s = -1, bool rjson = false, bool mp4 = false, string method = "play", int hls_manifest_timeout = 0, string extension = "m3u8")
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        if (tmdb_id > 0)
            id = tmdb_id;

        if (hls_manifest_timeout == 0)
            hls_manifest_timeout = hlsTimeout;

        string plugin = init.plugin.ToLowerAndTrim();

        if (serial == 1)
        {
            #region Сериал
            var tmdb = await InvokeCacheResult<JToken>($"tmdb:seasons:{id}", TimeSpan.FromHours(4), async e =>
            {
                var cub = CoreInit.conf.cub;

                var proxyManager = cub.useproxy
                    ? new ProxyManager("cub_api", cub)
                    : null;

                var root = await Http.Get<JObject>($"{cub.scheme}://tmdb.{cub.mirror}/3/tv/{id}?api_key={cub.api_key}", proxy: proxyManager?.Get());

                if (root == null || !root.ContainsKey("seasons"))
                    return e.Fail("seasons");

                return e.Success(root["seasons"]);
            });

            JArray seasonsArr = tmdb.IsSuccess && tmdb.Value is JArray arr ? arr : null;

            // Mirror tmdb.cub.best có thể chết (redirect về TMDB đòi key trong khi
            // cub.api_key trống) -> toàn bộ nguồn ENG liệt kê mùa rỗng. Fallback
            // Cinemeta theo IMDB (không cần key) khi client có gửi imdb_id.
            if (seasonsArr == null && !string.IsNullOrWhiteSpace(imdb_id) &&
                imdb_id.Trim().StartsWith("tt", StringComparison.OrdinalIgnoreCase))
            {
                var fb = await InvokeCacheResult<JArray>($"tmdb:seasons:cinemeta:{imdb_id.Trim().ToLower()}", TimeSpan.FromHours(4), async e =>
                {
                    var meta = await Http.Get<JObject>($"https://v3-cinemeta.strem.io/meta/series/{imdb_id.Trim()}.json");
                    var videos = meta?["meta"]?["videos"] as JArray;
                    if (videos == null)
                        return e.Fail("seasons");

                    var counts = new Dictionary<int, int>();
                    foreach (var v in videos)
                    {
                        int sn = v.Value<int?>("season") ?? 0;
                        int ep = v.Value<int?>("episode") ?? 0;
                        if (sn <= 0 || ep <= 0)
                            continue;
                        counts[sn] = counts.TryGetValue(sn, out int c) ? Math.Max(c, ep) : ep;
                    }

                    var res = new JArray();
                    foreach (var kv in counts.OrderBy(i => i.Key))
                        res.Add(new JObject { ["season_number"] = kv.Key, ["episode_count"] = kv.Value });

                    return res.Count == 0 ? e.Fail("seasons") : e.Success(res);
                });
                if (fb.IsSuccess)
                    seasonsArr = fb.Value;
                if (seasonsArr == null)
                    return OnError("seasons");
            }

            if (seasonsArr == null)
                return OnError(tmdb.ErrorMsg);

            if (s == -1)
            {
                #region Сезоны
                var tpl = new SeasonTpl();
                string enc_title = HttpUtility.UrlEncode(title);
                string enc_original_title = HttpUtility.UrlEncode(original_title);

                foreach (var season in seasonsArr)
                {
                    int number = season.Value<int>("season_number");
                    if (1 > number)
                        continue;

                    tpl.Append(
                        $"{number} сезон",
                        $"{host}/lite/{plugin}?id={id}&imdb_id={imdb_id}&serial=1&rjson={rjson}&title={enc_title}&original_title={enc_original_title}&s={number}",
                        number
                    );
                }

                return ContentTpl(tpl);
                #endregion
            }
            else
            {
                #region Серии
                var etpl = new EpisodeTpl();

                foreach (var season in seasonsArr)
                {
                    if (season.Value<int>("season_number") != s)
                        continue;

                    for (short i = 1; i <= season.Value<short>("episode_count"); i++)
                    {
                        string path = (mp4 || method == "call") ? "video" : $"video.{extension}";
                        string uri = $"{host}/lite/{plugin}/{path}?id={id}&imdb_id={imdb_id}&s={s}&e={i}";

                        if (method == "play")
                            uri = accsArgs(uri);

                        etpl.Append(
                            $"{i} серия",
                            title ?? original_title,
                            s,
                            i,
                            uri,
                            method,
                            streamlink: method == "call"
                                ? accsArgs($"{host}/lite/{plugin}/{(mp4 ? "video" : $"video.{extension}")}?id={id}&imdb_id={imdb_id}&s={s}&e={i}&play=true")
                                : null,
                            vast: init.vast,
                            hls_manifest_timeout: hls_manifest_timeout
                        );
                    }
                }

                return ContentTpl(etpl);
                #endregion
            }
            #endregion
        }
        else
        {
            #region Фильм
            var mtpl = new MovieTpl(title, original_title);

            string path = (mp4 || method == "call") ? "video" : $"video.{extension}";
            string uri = $"{host}/lite/{plugin}/{path}?id={id}&imdb_id={imdb_id}";

            if (method == "play")
                uri = accsArgs(uri);

            mtpl.Append(
                "English",
                uri,
                method,
                stream: method == "call"
                    ? accsArgs($"{host}/lite/{plugin}/{(mp4 ? "video" : $"video.{extension}")}?id={id}&imdb_id={imdb_id}&play=true")
                    : null,
                vast: init.vast,
                hls_manifest_timeout: hls_manifest_timeout
            );

            return ContentTpl(mtpl);
            #endregion
        }
    }
}
