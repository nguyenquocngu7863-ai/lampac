using Microsoft.Playwright;
using Shared.PlaywrightCore;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Filmlord;

/// <summary>
/// Плеер Alloha (absciss.algonoew.online) считает ссылку на поток в JS и отказывается работать вне iframe
/// (в html есть проверка isFramed), поэтому открываем СТРАНИЦУ ФИЛЬМА: она сама вставит iframe,
/// плеер запросит /bnsi/movies/{id} и попросит master.m3u8 с CDN (vkvideo.cloud). Ловим эти запросы.
/// </summary>
public static class FilmlordAlloha
{
    static readonly Regex manifestRx = new(@"master\.m3u8", RegexOptions.IgnoreCase);

    static readonly Regex resolutionRx = new(@"RESOLUTION=([0-9]+)x[0-9]+", RegexOptions.IgnoreCase);

    static readonly HttpClient probe = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>
    /// CDN vkvideo.cloud не пускает по Referer/UA: плеер присылает собственные заголовки
    /// (authorizations: Bearer …, accepts-controls: …), без них nginx отдаёт 403.
    /// Заголовки живут в рамках сессии плеера, поэтому запоминаем их по URL манифеста.
    /// </summary>
    static readonly ConcurrentDictionary<string, Dictionary<string, string>> streamHeaders = new(StringComparer.OrdinalIgnoreCase);

    static readonly string[] keepHeaders = { "authorizations", "accepts-controls", "origin", "referer", "user-agent" };

    /// <summary>Заголовки, с которыми плеер запросил этот манифест (null — если не поймали).</summary>
    public static Dictionary<string, string> HeadersFor(string url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        return streamHeaders.TryGetValue(url, out var headers) ? headers : null;
    }

    public static async Task<EmbedModel> Resolve(string pageUrl, int seconds = 20)
    {
        if (string.IsNullOrEmpty(pageUrl))
            return null;

        try
        {
            using var browser = new PlaywrightBrowser();

            var page = await browser.NewPageAsync("Filmlord").ConfigureAwait(false);
            if (page == null)
                return null;

            // Плеер выбирает стартовое качество по размеру окна: на дефолтном viewport
            // (1280x720) он начинает с 480p, на большом — сразу выше.
            await page.SetViewportSizeAsync(1920, 1080).ConfigureAwait(false);

            // Картинки/шрифты/стили страницы фильма для резолва не нужны, а на Android
            // их загрузка отнимает несколько секунд до старта плеера.
            await page.RouteAsync("**/*", route =>
            {
                string type = route.Request.ResourceType;

                if (type == "image" || type == "font" || type == "stylesheet")
                    return route.AbortAsync();

                return route.ContinueAsync();
            }).ConfigureAwait(false);

            var masters = new List<string>();

            page.Response += (_, resp) =>
            {
                try
                {
                    if (resp == null)
                        return;

                    string url = resp.Url ?? "";

                    // берём только манифест с CDN плеера, служебные плейлисты статистики не подходят
                    if (!manifestRx.IsMatch(url))
                        return;

                    if (!url.Contains("vkvideo", StringComparison.OrdinalIgnoreCase) && !url.Contains("interkh", StringComparison.OrdinalIgnoreCase))
                        return;

                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var header in resp.Request.Headers)
                    {
                        foreach (string name in keepHeaders)
                        {
                            if (name.Equals(header.Key, StringComparison.OrdinalIgnoreCase))
                                headers[name] = header.Value;
                        }
                    }

                    if (streamHeaders.Count > 64)
                        streamHeaders.Clear();

                    // заголовки запоминаем для всех пойманных манифестов: каждый вариант
                    // проксируется отдельной ссылкой и должен авторизоваться сам
                    streamHeaders[url] = headers;

                    lock (masters)
                    {
                        if (!masters.Contains(url))
                            masters.Add(url);
                    }
                }
                catch
                {
                    // обработчик не должен ронять запрос
                }
            };

            await page.GotoAsync(pageUrl, new PageGotoOptions()
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = seconds * 1000
            }).ConfigureAwait(false);

            // Плеер выдаёт манифесты по одному на каждое качество (480p → 1080p → 4K),
            // поэтому ждём не первый, а несколько — иначе в карточку уходит 480p.
            // Ждём до трёх манифестов, но не дольше 8 секунд: на части тайтлов плеер
            // отдаёт единственный 480p, и держать зрителя 20 секунд ради этого нельзя.
            for (int i = 0; i < 16; i++)
            {
                lock (masters)
                {
                    if (masters.Count >= 3)
                        break;
                }

                await Task.Delay(500).ConfigureAwait(false);
            }

            List<string> found;

            lock (masters)
                found = new List<string>(masters);

            if (found.Count == 0)
                return null;

            string hls = found.Count == 1 ? found[0] : await BestAsync(found).ConfigureAwait(false);

            return new EmbedModel()
            {
                movie = new MovieItem()
                {
                    hls = hls
                }
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Самый качественный манифест из пойманных (у каждого качества свой master.m3u8).</summary>
    static async Task<string> BestAsync(List<string> masters)
    {
        string best = null;
        int bestWidth = 0;

        foreach (string url in masters)
        {
            int width = await WidthAsync(url).ConfigureAwait(false);

            if (width > bestWidth)
            {
                bestWidth = width;
                best = url;
            }
        }

        return best ?? masters[0];
    }

    static async Task<int> WidthAsync(string url)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);

            var headers = HeadersFor(url);

            if (headers != null)
            {
                foreach (var header in headers)
                    req.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            using var resp = await probe.SendAsync(req).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
                return 0;

            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            int width = 0;

            foreach (Match m in resolutionRx.Matches(body))
            {
                if (int.TryParse(m.Groups[1].Value, out int w) && w > width)
                    width = w;
            }

            return width;
        }
        catch
        {
            return 0;
        }
    }
}
