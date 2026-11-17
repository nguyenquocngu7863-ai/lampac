using Newtonsoft.Json;
using Shared.Models.Events;
using Shared.Services;
using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Phantom;

public static class Service
{
    public static void WatchEdgeHash(StreamData watch, string wsUri)
    {
        _ = Task.Factory.StartNew(async () =>
        {
            try
            {
                using (var ws = new ClientWebSocket())
                {
                    ws.Options.SetRequestHeader("User-Agent", Http.UserAgent);

                    using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(60)))
                    {
                        await ws.ConnectAsync(new Uri(wsUri), cts.Token).ConfigureAwait(false);

                        async Task send(string type)
                        {
                            long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                            string payload = JsonConvert.SerializeObject(new
                            {
                                type,
                                current_time = 0,
                                resolution = watch.resolution,
                                track_id = "1",
                                speed = 1,
                                subtitle = -1,
                                ts
                            });

                            await ws.SendAsync(
                                new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload)),
                                WebSocketMessageType.Text,
                                true,
                                cts.Token
                            ).ConfigureAwait(false);
                        }

                        await send("playback_start").ConfigureAwait(false);
                        await send("init").ConfigureAwait(false);

                        _ = Task.Factory.StartNew(async () =>
                        {
                            try
                            {
                                while (!cts.IsCancellationRequested && ws.State == WebSocketState.Open)
                                {
                                    await Task.Delay(TimeSpan.FromSeconds(30), cts.Token).ConfigureAwait(false);
                                    await send("playing").ConfigureAwait(false);
                                }
                            }
                            catch { }
                        }, TaskCreationOptions.LongRunning);

                        var buffer = new byte[16 * 1024];

                        while (!cts.IsCancellationRequested && ws.State == WebSocketState.Open)
                        {
                            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token).ConfigureAwait(false);
                            if (result.MessageType == WebSocketMessageType.Close)
                                break;

                            string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                            string hash = Regex.Match(message ?? string.Empty, "\"edge_hash\":\"([^\"]+)\"").Groups[1].Value;

                            if (!string.IsNullOrEmpty(hash))
                                watch.edge_hash = hash;
                        }
                    }
                }
            }
            catch { }
        }, TaskCreationOptions.LongRunning);
    }

    async public static Task ProxyApiCreateHttpRequest(EventProxyApiCreateHttpRequest e)
    {
        if (e.plugin != null && e.plugin.Equals("phantom", StringComparison.OrdinalIgnoreCase))
        {
            var watch = e.decryptLink?.userdata as StreamData;

            if (string.IsNullOrEmpty(watch?.edge_hash))
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();

                while (string.IsNullOrEmpty(watch?.edge_hash) && sw.Elapsed < TimeSpan.FromSeconds(15))
                    await Task.Delay(50).ConfigureAwait(false);

                if (string.IsNullOrEmpty(watch?.edge_hash))
                {
                    if (ModInit.conf.debug)
                        Console.WriteLine("watch null");

                    e.requestMessage.RequestUri = null;
                    return;
                }
            }

            e.requestMessage.Headers.Clear();

            e.requestMessage.Headers.TryAddWithoutValidation("Connection", "keep-alive");
            e.requestMessage.Headers.TryAddWithoutValidation("sec-ch-ua", Http.defaultUaHeaders["sec-ch-ua"]);
            e.requestMessage.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
            e.requestMessage.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");
            e.requestMessage.Headers.TryAddWithoutValidation("User-Agent", Http.UserAgent);
            e.requestMessage.Headers.TryAddWithoutValidation("Accept", "*/*");
            e.requestMessage.Headers.TryAddWithoutValidation("Accept-Language", "ru-RU,ru;q=0.9,uk-UA;q=0.8,uk;q=0.7,en-US;q=0.6,en;q=0.5");
            e.requestMessage.Headers.TryAddWithoutValidation("Accepts-Controls", watch.edge_hash);
            e.requestMessage.Headers.TryAddWithoutValidation("Authorizations", "Bearer pXzvbyDGLYyB6VkwsWZDv3iMKZtsXNzpzRyxZUcsKHXxsSeaYakbo3hw9mBFRc5VQTpqAX6BW8aDEqyLaHYcXSQiV6KHYTVTK6MYRphNAy5sBjtrevqkDzKmLqNdfMZGEU9NELjmtKfZy3RNGzCd767sNh1mXEj4tCcvqndHtzmwAbZNkhm4ghDEasodotMBewypNQ56uotJAQGX11csfeRfBAPk8DcUWWkkqzxca8vbnEw12vUFbBzT6hz8ZB3F3dzUhUXoL2cr1WM1bXQArRCS1MUNMz3X5WDMMQoZKxj2AMTRqp7QQX4dDB9B7VzEZTmyFULhm1AcHHMkoMvSVvKYoBoAKLycYAgMHeD4ECJcGEAGpnkJhrV57zQ7");
            e.requestMessage.Headers.TryAddWithoutValidation("Origin", watch.requestOrigin);
            e.requestMessage.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");
            e.requestMessage.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
            e.requestMessage.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
            e.requestMessage.Headers.TryAddWithoutValidation("Referer", watch.requestReferer);
            e.requestMessage.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br, zstd");

            if (e.requestMessage.Content?.Headers != null)
                e.requestMessage.Content.Headers.Clear();
        }
    }
}
