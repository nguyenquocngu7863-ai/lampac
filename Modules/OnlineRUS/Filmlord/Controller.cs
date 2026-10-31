using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Filmlord;

public class FilmlordController : BaseOnlineController<FilmlordConf>
{
    static FilmlordSession session;
    static readonly object sessionLock = new object();

    // Alloha (4K) поднимается ~13 c (Playwright + плеер), поэтому результат держим 30 минут
    // и прогреваем в фоне при открытии карточки — к выбору озвучки ссылка уже готова.
    static readonly ConcurrentDictionary<string, (EmbedModel md, DateTimeOffset ex)> allohaCache = new();
    static readonly ConcurrentDictionary<string, Task<EmbedModel>> allohaTasks = new();

    public FilmlordController() : base(ModInit.conf)
    {
        loadKitInitialization = (j, i, c) =>
        {
            if (j.ContainsKey("dash"))
                i.dash = c.dash;

            if (j.ContainsKey("both"))
                i.both = c.both;

            return i;
        };
    }

    FilmlordSession Session()
    {
        var current = session;

        if (current != null && current.MatchHost(init.host))
            return current;

        lock (sessionLock)
        {
            if (session == null || !session.MatchHost(init.host))
                session = new FilmlordSession(init.host, Http.UserAgent);

            return session;
        }
    }

    /// <summary>
    /// Alloha с кэшем на 30 минут и склейкой одновременных вызовов: резолв ~10 c,
    /// а прогрев и запрос карточки стартуют вместе — без склейки плеер поднимался бы дважды.
    /// </summary>
    Task<EmbedModel> Alloha(string pageUri)
    {
        return allohaTasks.GetOrAdd(pageUri, key => Task.Run(async () =>
        {
            try
            {
                if (allohaCache.TryGetValue(key, out var cached) && cached.ex > DateTimeOffset.Now)
                    return cached.md;

                var md = await FilmlordAlloha.Resolve(key);

                if (md != null && !md.IsEmpty)
                {
                    if (allohaCache.Count > 64)
                        allohaCache.Clear();

                    allohaCache[key] = (md, DateTimeOffset.Now.AddMinutes(30));
                }

                return md;
            }
            catch
            {
                return null;
            }
            finally
            {
                allohaTasks.TryRemove(key, out _);
            }
        }));
    }

    /// <summary>Фоновый прогрев Alloha: пока пользователь выбирает озвучку, ссылка уже резолвится.</summary>
    void PrewarmAlloha(string pageUri)
    {
        if (!init.alloha || string.IsNullOrEmpty(pageUri))
            return;

        if (allohaCache.TryGetValue(pageUri, out var cached) && cached.ex > DateTimeOffset.Now)
            return;

        _ = Alloha(pageUri);
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/filmlord")]
    async public Task<ActionResult> Index(string title, string original_title, string uri = null, short serial = 0, short year = 0, short s = -1, bool rjson = false, bool similar = false, string voice = null)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(uri))
            return OnError();

        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        #region Поиск
        if (string.IsNullOrWhiteSpace(uri))
        {
            var search = await InvokeCacheResult<List<SearchItem>>($"filmlord:search:{title}:{year}:{serial}", 240, textJson: true, onget: async e =>
            {
                var items = await Session().Search(title);

                if ((items == null || items.Count == 0) && !string.IsNullOrWhiteSpace(original_title) && original_title != title)
                    items = await Session().Search(original_title);

                if (items == null || items.Count == 0)
                    return e.Fail("search");

                return e.Success(items);
            });

            if (!search.IsSuccess)
                return OnError(search.ErrorMsg);

            var best = FilmlordParser.PickBest(search.Value, title, year, serial);

            if (similar || best == null)
                return ContentTpl(BuildSimilar(search.Value, title, original_title, serial, year, rjson));

            uri = best.uri;
        }
        #endregion

        // 4K и английская дорожка есть только в плеере Alloha, Collaps отдаёт 672x350,
        // поэтому основной источник — Alloha, а Collaps остаётся запасным вариантом.
        // Прогрев стартует до запроса карточки: к выбору озвучки ссылка уже готова.
        PrewarmAlloha(uri);

        var cache = await InvokeCacheResult<EmbedModel>($"filmlord:view:{uri}", 20, textJson: true, onget: async e =>
        {
            EmbedModel md = init.alloha ? await Alloha(uri) : null;

            if (md == null || md.IsEmpty)
                md = await Session().Embed(uri);

            if (md == null || md.IsEmpty)
                return e.Fail("embed");

            return e.Success(md);
        });

        // Качество (720p/1080p) берём из master.m3u8 / .mpd. При both: true качество уже
        // приходит отдельными пунктами HLS/DASH, поэтому лишний запрос не нужен.
        // master.m3u8 тянем один раз: из него и качество, и названия аудио-дорожек
        string qualityLabel = null;

        if (!init.both && cache.IsSuccess && cache.Value?.movie != null && !string.IsNullOrEmpty(cache.Value.movie.hls))
        {
            var master = await Session().Fetch(cache.Value.movie.hls, FilmlordAlloha.HeadersFor(cache.Value.movie.hls));

            if (cache.Value.movie.audio == null || cache.Value.movie.audio.Count == 0)
                cache.Value.movie.audio = FilmlordParser.AudioNames(master);

            qualityLabel = FilmlordParser.MaxQuality(master, false);
        }

        return ContentTpl(cache, () => Tpl(cache.Value, title, original_title, uri, serial, year, s, rjson, qualityLabel, voice));
    }


    [HttpGet, Staticache(manually: true)]
    [Route("lite/filmlord-search")]
    async public Task<ActionResult> RouteSearch(string title, string original_title, short serial = 0, short year = 0, bool rjson = false)
    {
        if (string.IsNullOrWhiteSpace(title))
            return OnError("title", gbcache: false);

        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        var search = await InvokeCacheResult<List<SearchItem>>($"filmlord:search:{title}:{year}:{serial}", 240, textJson: true, onget: async e =>
        {
            var items = await Session().Search(title);

            if ((items == null || items.Count == 0) && !string.IsNullOrWhiteSpace(original_title) && original_title != title)
                items = await Session().Search(original_title);

            if (items == null || items.Count == 0)
                return e.Fail("search");

            return e.Success(items);
        });

        if (!search.IsSuccess)
            return ContentTpl(new SimilarTpl(0));

        return ContentTpl(BuildSimilar(search.Value, title, original_title, serial, year, rjson));
    }


    [HttpGet, AllowAnonymous]
    [Route("lite/filmlord/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string names = null, string voice = null)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        if (!IsAllowedUri(uri))
            return OnError();

        // CDN Alloha (vkvideo.cloud) отдаёт манифест только с заголовками плеера
        var streamHeaders = FilmlordAlloha.HeadersFor(uri);

        string master = await Session().Fetch(uri, streamHeaders);
        if (string.IsNullOrEmpty(master))
            return OnError();

        // CDN Alloha отдаёт варианты относительными ссылками — разворачиваем до проксирования
        master = FilmlordParser.Absolutize(master, uri);

        // Технические имена дорожек (rus0, ukr4, eng6) заменяем на названия озвучек
        if (!string.IsNullOrEmpty(names))
            master = FilmlordParser.RenameAudio(master, names);

        // Выбранная в Lampa озвучка оставляет в мастере только эту аудиодорожку
        master = FilmlordParser.SelectHlsAudio(master, voice);

        // CDN отдаёт сегменты только тому IP, который запросил master, поэтому весь плейлист
        // (и вложенные m3u8) обязан идти через прокси Lampac — принудительно, независимо от streamproxy
        // Заголовки CDN прикладываем к каждой ссылке: ProxyAPI переписывает вложенные
        // плейлисты и сегменты с тем же decryptLink, поэтому авторизация едет по всей цепочке
        var proxyHeaders = streamHeaders == null ? null : HeadersModel.Init(streamHeaders);

        master = FilmlordParser.ProxyUrls(master, u => HostStreamProxy(init, u, proxyHeaders, force_streamproxy: true));

        return Content(master, "application/vnd.apple.mpegurl");
    }


    [HttpGet, AllowAnonymous]
    [Route("lite/filmlord/video.mpd")]
    async public Task<ActionResult> VideoDash(string uri, string names = null, string voice = null)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        if (!IsAllowedUri(uri))
            return OnError();

        string mpd = await Session().Fetch(uri);
        if (string.IsNullOrEmpty(mpd))
            return OnError();

        // Технические языки дорожек (rus0, ukr4, eng6) заменяем на названия озвучек
        if (!string.IsNullOrEmpty(names))
            mpd = FilmlordParser.RenameDashAudio(mpd, names);

        // Выбранная в Lampa озвучка оставляет в MPD только эту аудиодорожку
        mpd = FilmlordParser.SelectDashAudio(mpd, voice);

        // Сегменты CDN привязаны к IP сервера: <BaseURL> переводим на /proxy-dash/ (аналог ProxyAPI.Dash),
        // иначе плеер пойдёт в CDN напрямую со своего IP и получит 410
        mpd = Regex.Replace(mpd, "<BaseURL>([^<]+)</BaseURL>", m =>
        {
            string enc = ProxyLink.Encrypt(m.Groups[1].Value, requestInfo.IP, forceMd5: true);
            return $"<BaseURL>{host}/proxy-dash/{enc}/</BaseURL>";
        });

        return Content(mpd, "application/dash+xml");
    }


    #region Similar
    SimilarTpl BuildSimilar(List<SearchItem> items, string title, string original_title, short serial, short year, bool rjson)
    {
        var stpl = new SimilarTpl(items.Count);

        foreach (var item in items)
        {
            stpl.Append(
                item.name,
                item.year,
                string.Empty,
                $"{host}/lite/filmlord?rjson={rjson}&uri={HttpUtility.UrlEncode(item.uri)}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&serial={serial}&year={year}",
                Poster(item.poster)
            );
        }

        return stpl;
    }

    ITplResult Tpl(EmbedModel md, string title, string original_title, string uri, short serial, short year, short s, bool rjson, string qualityLabel = null, string voice = null)
    {
        if (md == null)
            return null;

        string enc_uri = HttpUtility.UrlEncode(uri);
        string enc_title = HttpUtility.UrlEncode(title);
        string enc_original_title = HttpUtility.UrlEncode(original_title);

        string page_url(string voice_name)
            => $"{host}/lite/filmlord?rjson={rjson}&uri={enc_uri}&title={enc_title}&original_title={enc_original_title}&serial={serial}&year={year}&s={s}&voice={HttpUtility.UrlEncode(voice_name)}";

        if (md.movie != null)
        {
            #region Фильм
            var voices = VoiceNames(md.movie.audio, md.movie.voicename);

            if (voices.Count == 0)
                voices.Add("По умолчанию");

            string selected = string.IsNullOrEmpty(voice) || !voices.Contains(voice) ? voices[0] : voice;

            var vtpl = new VoiceTpl(voices.Count);

            foreach (var item in voices)
                vtpl.Append(item, item == selected, page_url(item));

            var mtpl = new MovieTpl(title, original_title, vtpl, 1);

            foreach (var st in Streams(md.movie.voicename, md.movie.hls, md.movie.dash, md.movie.audio, voice))
                mtpl.Append(st.label, st.link, subtitles: Subtitles(md.movie.cc), voice_name: selected, quality: qualityLabel, vast: init.vast);

            return mtpl.IsEmpty ? null : mtpl;
            #endregion
        }

        if (md.seasons == null || md.seasons.Length == 0)
            return null;

        if (s == -1)
        {
            #region Сезоны
            var tpl = new SeasonTpl(md.seasons.Length);

            foreach (var season in md.seasons)
            {
                tpl.Append(
                    $"{season.season} сезон",
                    $"{host}/lite/filmlord?rjson={rjson}&uri={enc_uri}&title={enc_title}&original_title={enc_original_title}&serial={serial}&year={year}&s={season.season}",
                    season.season.ToString()
                );
            }

            return tpl;
            #endregion
        }
        else
        {
            #region Серии
            var season = md.seasons.FirstOrDefault(i => i.season == s);

            if (season?.episodes == null)
                return null;

            var voices = new List<string>();
            var seenVoices = new HashSet<string>();

            // Lampa для сериалов берёт voice_name из VoiceTpl (.videos__button), а не из details,
            // поэтому без этого блока в «Озвучках» было «Неизвестно».
            foreach (var episode in season.episodes)
            {
                foreach (var item in VoiceNames(episode.audio, episode.voicename))
                {
                    if (seenVoices.Add(item))
                        voices.Add(item);
                }
            }

            if (voices.Count == 0)
                voices.Add("По умолчанию");

            string selected = string.IsNullOrEmpty(voice) || !voices.Contains(voice) ? voices[0] : voice;

            var vtpl = new VoiceTpl(voices.Count);

            foreach (var item in voices)
                vtpl.Append(item, item == selected, page_url(item));

            var etpl = new EpisodeTpl(vtpl, season.episodes.Count);

            foreach (var episode in season.episodes)
            {
                var streams = Streams(episode.voicename, episode.hls, episode.dash, episode.audio, voice);

                if (streams.Count == 0)
                    continue;

                // При both: true не дублируем серии: один пункт на серию, а HLS/DASH — как выбор качества в плеере
                StreamQualityTpl quality = null;

                if (streams.Count > 1)
                {
                    quality = new StreamQualityTpl(streams.Count);

                    foreach (var st in streams)
                        quality.Append(st.link, st.label);
                }
                else if (!string.IsNullOrEmpty(qualityLabel))
                {
                    quality = new StreamQualityTpl(1);
                    quality.Append(streams[0].link, qualityLabel);
                }

                etpl.Append(
                    $"{episode.episode} серия",
                    title ?? original_title,
                    s,
                    episode.episode,
                    streams[0].link,
                    streamquality: quality,
                    subtitles: Subtitles(episode.cc),
                    voice_name: selected,
                    vast: init.vast
                );
            }

            return etpl.IsEmpty ? null : etpl;
            #endregion
        }
    }
    #endregion

    #region Helpers
    /// <summary>Варианты потока (HLS и/или DASH) с учётом init.conf: dash / both.</summary>
    List<(string label, string link)> Streams(string voicename, string hls, string dash, List<string> audio, string voice = null)
    {
        var list = new List<(string label, string link)>();

        if (init.both)
        {
            if (!string.IsNullOrEmpty(hls))
                list.Add(("HLS", StreamUrl(hls, audio, "m3u8", voice)));

            if (!string.IsNullOrEmpty(dash))
                list.Add(("DASH 1080p", StreamUrl(dash, audio, "mpd", voice)));

            return list;
        }

        string stream = init.dash ? (dash ?? hls) : hls;

        if (string.IsNullOrEmpty(stream))
            stream = hls ?? dash;

        if (!string.IsNullOrEmpty(stream))
        {
            bool mpd = stream.Contains(".mpd", StringComparison.OrdinalIgnoreCase);
            list.Add((voicename ?? "По умолчанию", StreamUrl(stream, audio, mpd ? "mpd" : "m3u8", voice)));
        }

        return list;
    }

    static List<string> VoiceNames(List<string> audio, string voicename)
    {
        if (audio != null && audio.Count > 0)
            return audio;

        if (string.IsNullOrEmpty(voicename))
            return new List<string>();

        return voicename.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(i => i.Trim())
                        .Where(i => i.Length > 0)
                        .ToList();
    }

    /// <summary>
    /// Ссылка на поток. HLS идёт через свой маршрут (нормальные подписи дорожек), DASH — через свой
    /// маршрут (подписи дорожек в манифесте). Суффикс <c>#.m3u8</c> / <c>#.mpd</c> — подсказка типа плееру.
    /// </summary>
    string StreamUrl(string stream, List<string> audio, string ext, string voice = null)
    {
        if (string.IsNullOrEmpty(stream))
            return null;

        string names = (audio != null && audio.Count > 0)
            ? $"&names={Uri.EscapeDataString(string.Join("|", audio))}"
            : string.Empty;

        string voiceParam = string.IsNullOrEmpty(voice)
            ? string.Empty
            : $"&voice={Uri.EscapeDataString(voice)}";

        // accsArgs обязателен: маршрут /lite/... проходит через проверку доступа, иначе плеер получит accsdb
        if (ext == "mpd")
            return accsArgs($"{host}/lite/filmlord/video.mpd?uri={Uri.EscapeDataString(stream)}{names}{voiceParam}") + "#.mpd";

        return accsArgs($"{host}/lite/filmlord/video.m3u8?uri={Uri.EscapeDataString(stream)}{names}{voiceParam}") + "#.m3u8";
    }

    SubtitleTpl Subtitles(List<Cc> cc)
    {
        if (cc == null || cc.Count == 0)
            return null;

        var subtitles = new SubtitleTpl(cc.Count);

        foreach (var item in cc)
        {
            if (string.IsNullOrEmpty(item?.url) || string.IsNullOrEmpty(item?.name))
                continue;

            subtitles.Append(item.name, HostStreamProxy(item.url, force_streamproxy: true));
        }

        return subtitles;
    }

    string Poster(string img)
    {
        if (string.IsNullOrWhiteSpace(img))
            return null;

        if (img.StartsWith("//", StringComparison.Ordinal))
            return "https:" + img;

        if (img.StartsWith("/", StringComparison.Ordinal))
            return (Session().ActiveHost ?? init.host) + img;

        return img;
    }

    /// <summary>
    /// Маршруты потоков анонимные (иначе accsdb режет плеер), поэтому принимаем только ссылки CDN —
    /// чтобы сервер нельзя было использовать как открытый прокси.
    /// </summary>
    static bool IsAllowedUri(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return false;

        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u))
            return false;

        if (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp)
            return false;

        // CDN Collaps — interkh.com, CDN Alloha (4K) — vkvideo.cloud
        string[] allowed = { "interkh.com", "vkvideo.cloud" };

        foreach (string domain in allowed)
        {
            if (u.Host.Equals(domain, StringComparison.OrdinalIgnoreCase)
                || u.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
    #endregion
}
