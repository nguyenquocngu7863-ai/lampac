(function () {
    var taskId = null;
    var heartbeatTimer = null;
    var hlsTimeoutTimer = null;
    var gstEnabled = null;
    var gstStatusAt = 0;
    var gstStatusPending = false;
    var gstStatusWaiters = [];

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
        var title = objectField(track, 'title') || ('Аудиодорожка #' + (index + 1));
        var lang = (objectField(track, 'language') || '').toUpperCase();
        var codec = nameAudioCodec(objectField(track, 'capsName'));

        var rateValue = objectField(track, 'rate');
        var rate = rateValue
            ? Math.round(rateValue / 1000) + ' kHz'
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
        // timeout. GStreamer may need longer to produce a cold 4K segment.
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

    function objectField(object, name) {
        if (!object)
            return undefined;

        if (object[name] !== undefined)
            return object[name];

        var pascalName = name.charAt(0).toUpperCase() + name.slice(1);
        return object[pascalName];
    }

    function isLargeHdrSource(json) {
        var probe = objectField(json, 'probe');
        var video = objectField(probe, 'video');
        if (!video)
            return false;

        var width = Number(objectField(video, 'width') || 0);
        var height = Number(objectField(video, 'height') || 0);
        if (Math.max(width, height) < 3000)
            return false;

        var isHdr = objectField(video, 'isHdr') === true ||
            objectField(video, 'isDolbyVision') === true;
        var transfer = String(
            objectField(video, 'videoTransfer') ||
            objectField(video, 'transfer') ||
            objectField(video, 'colorimetry') ||
            ''
        );

        return isHdr || /pq|hlg|dolby|2084|bt2100/i.test(transfer);
    }

    function gstBaseUrl(hlsUrl) {
        var match = String(hlsUrl || '').match(/^(.*)\/master\.m3u8(?:\?.*)?$/i);
        return match ? match[1] : null;
    }

    function warmupRequest(url, responseType, callback) {
        var xhr = new XMLHttpRequest();
        var finished = false;

        function finish(success) {
            if (finished)
                return;

            finished = true;
            callback(success === true);
        }

        try {
            xhr.open('GET', url, true);
            xhr.timeout = 120000;
            if (responseType)
                xhr.responseType = responseType;
            xhr.setRequestHeader('Cache-Control', 'no-cache');
            xhr.onload = function () {
                finish(xhr.status >= 200 && xhr.status < 400);
            };
            xhr.onerror = function () { finish(false); };
            xhr.ontimeout = function () { finish(false); };
            xhr.onabort = function () { finish(false); };
            xhr.send();
        } catch (error) {
            finish(false);
        }
    }

    function warmupFirstSegment(hlsUrl, audioIndex, callback) {
        var base = gstBaseUrl(hlsUrl);
        if (!base) {
            callback(false);
            return;
        }

        var query = '?audio=' + encodeURIComponent(audioIndex || 0);

        // Prime the exact same task and audio track that hls.js will use. The
        // first 4K HDR fragment is then already in Lampac's disk cache when
        // the TV player starts, so a cold CPU encode cannot hit the short
        // WebView fragment timeout.
        warmupRequest(base + '/master.m3u8' + query, 'text', function (masterOk) {
            if (!masterOk) {
                callback(false);
                return;
            }

            warmupRequest(base + '/init.mp4' + query, 'arraybuffer', function (initOk) {
                if (!initOk) {
                    callback(false);
                    return;
                }

                warmupRequest(base + '/seg/0.m4s' + query, 'arraybuffer', callback);
            });
        });
    }

    function playWithWarmup(data, json, hlsUrl, audioIndex, play) {
        if (!isLargeHdrSource(json)) {
            play();
            return;
        }

        Lampa.Loading.start(function () { }, 'Chuẩn bị đoạn HDR 4K đầu tiên...');
        warmupFirstSegment(hlsUrl, audioIndex, function (success) {
            Lampa.Loading.stop();

            if (!success)
                console.log('GStreamer', '4K HDR warmup failed; letting hls.js retry');

            play();
        });
    }

    // Apply immediately when hls.js is already present, or poll briefly when
    // the Lampa build loads it lazily.
    tuneHlsTimeouts();

    function finishGstStatus(enabled) {
        gstStatusPending = false;
        gstEnabled = enabled === true;
        gstStatusAt = Date.now();

        var waiters = gstStatusWaiters;
        gstStatusWaiters = [];
        waiters.forEach(function (callback) {
            try { callback(gstEnabled); } catch (error) { }
        });
    }

    function getGstStatus(callback) {
        var now = Date.now();
        if (gstEnabled !== null && now - gstStatusAt < 5000) {
            callback(gstEnabled);
            return;
        }

        gstStatusWaiters.push(callback);
        if (gstStatusPending)
            return;

        gstStatusPending = true;

        try {
            var network = new Lampa.Reguest();
            network.timeout(3000);
            network.native(account('{localhost}/gst/status'), function (response) {
                var enabled = false;
                try {
                    var json = typeof response === 'string' ? JSON.parse(response) : response;
                    var value = json && (json.enabled !== undefined ? json.enabled : json.Enabled);
                    enabled = value === true || String(value).toLowerCase() === 'true';
                } catch (error) { }
                finishGstStatus(enabled);
            }, function () {
                // If the status endpoint is temporarily unavailable, try
                // GStreamer once and let /gst/add fail open per-file. A known
                // cached false value still keeps direct playback.
                finishGstStatus(gstEnabled === null ? true : gstEnabled);
            });
        } catch (error) {
            finishGstStatus(gstEnabled === null ? true : gstEnabled);
        }
    }

    function forceLandscape() {
        try {
            var screenObject = window.screen;
            var orientation = screenObject && screenObject.orientation;
            var lock = orientation && orientation.lock;

            if (typeof lock === 'function') {
                var result = lock.call(orientation, 'landscape');
                if (result && typeof result.catch === 'function')
                    result.catch(function () { });
                return;
            }

            var legacyLock = screenObject && (
                screenObject.lockOrientation ||
                screenObject.mozLockOrientation ||
                screenObject.msLockOrientation ||
                screenObject.webkitLockOrientation
            );

            if (typeof legacyLock === 'function')
                legacyLock.call(screenObject, 'landscape');
        } catch (error) {
            // Orientation locking is optional on desktop browsers and old TV
            // WebViews; playback must continue when the platform rejects it.
        }
    }

    function unlockOrientation() {
        try {
            var screenObject = window.screen;
            var orientation = screenObject && screenObject.orientation;
            if (orientation && typeof orientation.unlock === 'function')
                orientation.unlock();
        } catch (error) { }
    }

    function playDirectAfterGstFailure(e, reason) {
        try {
            Lampa.Loading.stop();
            console.log('GStreamer', 'falling back to direct playback', reason || 'unknown error');

            if (e && e.data) {
                // Prevent this same MKV from re-entering the interceptor. The
                // next playlist item remains independent and will be tested.
                e.data.__gstDirect = true;
                Lampa.Player.play(e.data);
            }
        } catch (error) {
            console.log('GStreamer', 'direct fallback failed', error);
        }
    }

    function withoutAddonSelect(url) {
        return String(url || '')
            .replace(/([?&])(?:webstreamr|k20)_select=1&?/i, '$1');
    }

    function isAddonSelection(e) {
        return !!(e && e.data &&
            typeof e.data.url === 'string' &&
            /(?:webstreamr|k20)_select=1/i.test(e.data.url) &&
            !e.data.__addonSelected);
    }

    function showWebstreamrSelection(e) {
        var quality = e.data && e.data.quality;
        var items = [];

        if (quality && typeof quality === 'object') {
            for (var name in quality) {
                if (!Object.prototype.hasOwnProperty.call(quality, name) || !quality[name])
                    continue;

                items.push({
                    title: name,
                    url: quality[name]
                });
            }
        }

        if (items.length < 2) {
            e.data.__addonSelected = true;
            e.data.url = withoutAddonSelect(e.data.url);
            if (e.data.playlist)
                delete e.data.playlist;
            Lampa.Player.play(e.data);
            return;
        }

        e.abort();

        Lampa.Select.show({
            title: /k20_select/i.test(e.data.url) ? 'Chọn link K20' : 'Chọn link WebStreamr',
            items: items,
            onSelect: function (item) {
                Lampa.Select.close();

                var selected = Lampa.Arrays.clone(e.data);
                selected.url = withoutAddonSelect(item.url);
                selected.__addonSelected = true;
                // Do not let a failed first source start the whole episode
                // playlist before the user has chosen a link.
                if (selected.playlist)
                    delete selected.playlist;

                Lampa.Player.play(selected);
            },
            onBack: function () {
                // Lampa.Select closes itself before invoking onBack. Calling
                // close() here recursively re-enters jQuery and overflows the
                // old WebView call stack.
            }
        });
    }

    function startGstreamerTranscode(e) {
        if (e.data.url.indexOf('/gst/') != -1 || e.data.url.indexOf('.m3u8') != -1)
            return;

        e.abort()

        setTimeout(() => {
            Lampa.Player.close();

            Lampa.Loading.start(function () { }, 'Получение списка аудио дорожек...');

            var src = e.data.url.replace(/&(preload|stat|m3u)/g, '&play');

            var network = new Lampa.Reguest();
            // 4K/HDR files need more time for probing and the first segment.
            network.timeout(90000);

            network.native(account('{localhost}/gst/add?linkencode=' + encodeURIComponent(Lampa.Base64.encode(src))), function (response) {
                var json;
                try {
                    json = typeof response === 'string' ? JSON.parse(response) : response;
                } catch (error) {
                    playDirectAfterGstFailure(e, 'invalid /gst/add response');
                    return;
                }

                if (!json || !json.id || !json.hls) {
                    playDirectAfterGstFailure(e, 'GStreamer rejected this source');
                    return;
                }

                Lampa.Loading.stop();

                var probe = objectField(json, 'probe');
                var tracks = probe && Array.isArray(objectField(probe, 'tracks'))
                    ? objectField(probe, 'tracks')
                    : [];

                var items = tracks
                    .filter(function (track) {
                        return track && objectField(track, 'type') === 'audio';
                    })
                    .map(function (track, index) {
                        return formatAudioItem(track, index);
                    });



                tuneHlsTimeouts();

                delete e.data.torrent_hash;
                e.data.hls_type = 'hlsjs';
                // GStreamer may need up to 120s to produce a cold 4K
                // CPU fragment. These fields are consumed by newer Lampa
                // builds; applyHlsTimeouts also covers older hls.js builds.
                e.data.hls_manifest_timeout = 120000;
                e.data.hls_retry_timeout = 120000;
                e.data.hls_frag_timeout = 120000;
                e.data.hls_frag_retry_timeout = 120000;

                function playAudioTrack(item) {
                    var audioIndex = item ? item.audioIndex : 0;
                    e.data.url_orig = e.data.url;
                    e.data.url = audioIndex ? json.hls + '?audio=' + audioIndex : json.hls;
                    taskId = json.id;

                    playWithWarmup(e.data, json, e.data.url, audioIndex, function () {
                        Lampa.Player.play(e.data);
                        Lampa.Player.playlist(createPlaylist(e.data, audioIndex));
                        Lampa.Player.callback(function () {
                            Lampa.Controller.toggle('modal');
                            e.data.url = e.data.url_orig;
                            Lampa.PlayerPlaylist.get().forEach(function (p) {
                                p.url = p.url_orig;
                            });
                        });
                    });
                }

                if (!items.length || items.length == 1) {
                    playAudioTrack(items[0]);
                    return;
                }

                // Prefer the original English audio whenever probe metadata
                // identifies it.  Files without an English track still show
                // the selector, so Vietnamese/dub-only releases remain usable.
                var englishTrack = items.find(function (item) {
                    return /(?:^|[^a-z])(en|eng|english)(?:[^a-z]|$)/i.test(
                        (item.subtitle || '') + ' ' + (item.title || '')
                    );
                });
                if (englishTrack) {
                    playAudioTrack(englishTrack);
                    return;
                }

                var last_controller = Lampa.Controller.enabled().name;

                Lampa.Select.show({
                    title: 'Chọn audio',
                    items: items,
                    onSelect: function (item) {
                        Lampa.Select.close();
                        playAudioTrack(item);
                    },
                    onBack: function () {
                        Lampa.Controller.toggle(last_controller);
                    }
                });
            }, function (error) {
                playDirectAfterGstFailure(e, 'GStreamer request failed');
            });
        }, 10);

    }

    function handlePlayerStart(e) {
        if (isAddonSelection(e)) {
            showWebstreamrSelection(e);
            return;
        }

        if (!isMkvSource(e.data))
            return;

        // The adapter uses an MKV-looking redirect deliberately. This flag is
        // set only when the server reports that GStreamer is disabled.
        if (e.data && e.data.__gstDirect)
            return;

        var statusAge = Date.now() - gstStatusAt;
        if (gstEnabled === true && statusAge < 5000) {
            startGstreamerTranscode(e);
            return;
        }

        if (gstEnabled === false && statusAge < 5000)
            return;

        // Wait for the real server setting before aborting the native player.
        e.abort();
        getGstStatus(function (enabled) {
            if (!enabled) {
                e.data.__gstDirect = true;
                Lampa.Player.play(e.data);
                return;
            }

            startGstreamerTranscode(e);
        });
    }

    function handlePlayerDestroy() {
        unlockOrientation();

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
        forceLandscape();

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