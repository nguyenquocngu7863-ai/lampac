(function () {
    var taskId = null;
    var heartbeatTimer = null;
    var hlsTimeoutTimer = null;

    function getHlsConstructor() {
        if (typeof window !== 'undefined' && window.Hls)
            return window.Hls;

        // `Hls` is not necessarily loaded when this plugin is evaluated.
        // `typeof` keeps this safe on old WebViews where the global is absent.
        if (typeof Hls !== 'undefined')
            return Hls;

        return null;
    }

    function applyHlsTimeouts() {
        var hls = getHlsConstructor();
        if (!hls || !hls.DefaultConfig)
            return false;

        // hls.js 1.x keeps fragment timeout separately from the manifest
        // timeout. GStreamer may need long to produce a cold segment; use
        // generous timeouts so playback waits instead of erroring early.
        var config = hls.DefaultConfig;
        config.manifestLoadingTimeOut = 120000;
        config.manifestLoadingMaxRetryTimeout = 120000;
        config.levelLoadingTimeOut = 120000;
        config.levelLoadingMaxRetryTimeout = 120000;
        config.fragLoadingTimeOut = 120000;
        config.fragLoadingMaxRetry = 6;
        config.fragLoadingRetryDelay = 1000;
        config.fragLoadingMaxRetryTimeout = 120000;

        return true;
    }

    function tuneHlsTimeouts() {
        try {
            if (applyHlsTimeouts()) {
                if (hlsTimeoutTimer) {
                    clearInterval(hlsTimeoutTimer);
                    hlsTimeoutTimer = null;
                }
                return;
            }

            // Some Lampa builds load hls.js lazily immediately before the
            // player is created. Retry briefly instead of silently leaving the
            // library's 10/20-second defaults in place.
            if (hlsTimeoutTimer)
                return;

            var attempts = 0;
            hlsTimeoutTimer = setInterval(function () {
                attempts++;

                if (applyHlsTimeouts() || attempts >= 120) {
                    clearInterval(hlsTimeoutTimer);
                    hlsTimeoutTimer = null;
                }
            }, 250);
        } catch (error) {
            console.log('GStreamer', 'could not tune hls.js timeouts', error);
        }
    }

    function account(url) {
        url = url + '';
        if (url.indexOf('account_email=') == -1) {
            var email = Lampa.Storage.get('account_email');
            if (email) url = Lampa.Utils.addUrlComponent(url, 'account_email=' + encodeURIComponent(email));
        }
        if (url.indexOf('uid=') == -1) {
            var uid = Lampa.Storage.get('lampac_unic_id', '');
            if (uid) url = Lampa.Utils.addUrlComponent(url, 'uid=' + encodeURIComponent(uid));
        }
        if (url.indexOf('token=') == -1) {
            var token = '{token}';
            if (token != '') url = Lampa.Utils.addUrlComponent(url, 'token={token}');
        }
        return url;
    }

    function resolveMediaUrl(data) {
        if (data && data.url) return data.url;
        return '';
    }

    function isMkvSource(data) {
        var url = resolveMediaUrl(data);
        if (!url) return false;

        if (
            /\/dlna\/stream(?:\?|$)/i.test(url) &&
            /[?&]path=[^&#]*\.mkv(?:[&#]|$)/i.test(url)
        ) {
            return true;
        }

        url = url.split('#')[0].split('?')[0];

        return (
            /\.mkv$/i.test(url) ||
            /\.avi$/i.test(url) ||
            /\/lite\/pidtor\//i.test(url)
        );
    }

    function nameAudioCodec(capsName) {
        var codec = (capsName || '')
            .replace(/^audio\/x-/i, '')
            .replace(/^audio\//i, '')
            .toLowerCase();

        var names = {
            ac3: 'AC-3',
            eac3: 'E-AC-3',
            aac: 'AAC',
            mp3: 'MP3',
            opus: 'Opus',
            vorbis: 'Vorbis',
            flac: 'FLAC',
            dts: 'DTS',
            truehd: 'TrueHD'
        };

        return names[codec] || codec.toUpperCase();
    }

    function formatAudioItem(track, index) {
        var title = track.title || ('Аудиодорожка #' + (index + 1));
        var lang = (track.language || '').toUpperCase();
        var codec = nameAudioCodec(track.capsName);

        var rate = track.rate
            ? Math.round(track.rate / 1000) + ' kHz'
            : '';

        var subtitleParts = [];

        if (lang && lang !== 'UND') subtitleParts.push(lang);
        if (codec) subtitleParts.push(codec);
        if (rate) subtitleParts.push(rate);

        return {
            title: title,
            subtitle: subtitleParts.join(' • '),
            padName: track.padName,
            audioIndex: index || 0
        };
    }

    function createPlaylist(data, audioIndex) {
        var playlist = []

        if (data.playlist) {
            data.playlist.forEach(function (p) {
                playlist.push({
                    title: p.title,
                    url_orig: p.url,
                    url: account('{localhost}/gst/start.m3u8?linkencode=' + encodeURIComponent(Lampa.Base64.encode(p.url))) + '&audio=' + audioIndex
                })
            })
        }

        return playlist
    }

    function handlePlayerStart(e) {
        if (isMkvSource(e.data)) {
            if (e.data.url.indexOf('/gst/') != -1 || e.data.url.indexOf('.m3u8') != -1)
                return;

            e.abort()

            setTimeout(() => {
                Lampa.Player.close();

                Lampa.Loading.start(function () { }, 'Получение списка аудио дорожек...');

                var src = e.data.url.replace(/&(preload|stat|m3u)/g, '&play');

                var addAttempts = 0;

                function addSource() {
                    // 4K/HDR probe + first segment can take well over a minute.
                    var network = new Lampa.Reguest();
                    network.timeout = 120000;

                    network.native(account('{localhost}/gst/add?linkencode=' + encodeURIComponent(Lampa.Base64.encode(src))), function (response) {
                        Lampa.Loading.stop();

                        var json = typeof response === 'string' ? JSON.parse(response) : response;
                        if (!json || !json.id || !json.hls) {
                            Lampa.Noty.show('Не удалось запустить транскодинг');
                            return;
                        }

                        // GStreamer may take long to produce a cold fragment.
                        tuneHlsTimeouts();

                        var tracks = json.probe && Array.isArray(json.probe.tracks)
                            ? json.probe.tracks
                            : [];

                        var items = tracks
                            .filter(function (track) {
                                return track && track.type === 'audio';
                            })
                            .map(function (track, index) {
                                return formatAudioItem(track, index);
                            });

                        var last_controller = Lampa.Controller.enabled().name

                        delete e.data.torrent_hash;
                        e.data.hls_type = 'hlsjs';
                        e.data.hls_manifest_timeout = 120000;
                        e.data.hls_retry_timeout = 120000;
                        e.data.hls_frag_timeout = 120000;
                        e.data.hls_frag_retry_timeout = 120000;

                        function startPlayback(item) {
                            var audioIndex = item ? item.audioIndex : 0;

                            e.data.url_orig = e.data.url
                            e.data.url = audioIndex
                                ? json.hls + '?audio=' + audioIndex
                                : json.hls;

                            Lampa.Player.play(e.data);
                            Lampa.Player.playlist(createPlaylist(e.data, audioIndex))
                            Lampa.Player.callback(function () {
                                Lampa.Controller.toggle('modal')

                                e.data.url = e.data.url_orig

                                Lampa.PlayerPlaylist.get().forEach(function (p) {
                                    p.url = p.url_orig
                                })
                            })
                            taskId = json.id;
                        }

                        if (!items.length || items.length == 1) {
                            startPlayback(items[0]);
                            return;
                        }

                        // Always ask the user which audio track to play when there
                        // is more than one. Auto-selecting English breaks dubbed
                        // releases (e.g. Spanish-only movies).
                        Lampa.Select.show({
                            title: 'Выберите аудиодорожку',
                            items: items,
                            onSelect: function (item) {
                                Lampa.Select.close();
                                startPlayback(item);
                            },
                            onBack: function () {
                                Lampa.Controller.toggle(last_controller)
                            }
                        });
                    }, function (error) {
                        // The server returns 502 while it is still probing a slow
                        // source. Retry once instead of erroring immediately.
                        addAttempts++;
                        if (addAttempts < 2) {
                            Lampa.Loading.start(function () { }, 'Ожидание транскодинга...');
                            setTimeout(addSource, 5000);
                            return;
                        }

                        Lampa.Loading.stop();
                        Lampa.Noty.show('Не удалось запустить транскодинг');
                    });
                }

                addSource();
            }, 10);
        }
    }

    function handlePlayerDestroy() {
        if (taskId != null) {
            var network = new Lampa.Reguest();
            network.timeout = 5000;
            network.native('{localhost}/gst/remove?id=' + taskId, function (response) { }, function (error) { });
            taskId = null;
        }
    }

    function sendHeartbeat() {
        if (taskId != null) {
            var net = new Lampa.Reguest();
            net.native('{localhost}/gst/' + taskId + '/heartbeat', function () { }, function (error) { }, null, {
                dataType: 'text',
                timeout: 3000
            });
        }
    }

    function stopHeartbeat() {
        if (heartbeatTimer) {
            clearInterval(heartbeatTimer);
            heartbeatTimer = null;
        }
    }

    function handleVideoPause() {
        if (taskId == null)
            return;

        stopHeartbeat();
        heartbeatTimer = setInterval(sendHeartbeat, 1000 * 20);
    }

    function handleVideoPlay() {
        if (taskId != null)
            stopHeartbeat();
    }

    if (!window.lampac_transcoding_plugin) {
        window.lampac_transcoding_plugin = true;
        Lampa.Utils.putScriptAsync(["{localhost}/gst/tracks.js"]);

        Lampa.Player.listener.follow('create', handlePlayerStart);
        Lampa.Player.listener.follow('destroy', handlePlayerDestroy);
        Lampa.PlayerVideo.listener.follow('pause', handleVideoPause);
        Lampa.PlayerVideo.listener.follow('play', handleVideoPlay);
    }
})();