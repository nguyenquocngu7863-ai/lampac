(function () {
  'use strict';

  /*
   * SubSense download-only plugin.
   *
   * This plugin deliberately does not touch Lampa.Player.  It adds a button to
   * the full card, lets the user choose an episode/subtitle, and hands the
   * selected URL to the optional SubSense Termux Bridge application.
   */

  var DEFAULT_SUBSENSE_BASE = 'https://subsense.nepiraw.com/bu8e9cfy-ZSnP5mVsCkziQ-DtDwHymaozxOkAUjM0O4fIZCp0Y3wd4behaNV8shVg6U5yv7RhcX7O4Rpi6xee5TQdKCetjN8wmBS-xeADnVc0LVN5jpYQmFyWOyMW7WTVxw04MHCYjyE6XHlc3Jwb1gSm6tLiMsIkGAE85EcfaM_EdeNl472Wr8knESDBNY52CDi0bJKFZ5dOZ5dgN-KhoC2LCSo-mwrKxzUt4GdnbkuzMgeFxntuHVBe2DYJhWIFkNCb4CKsEpOp9TAsifN_Jg';
  var MANIFEST_STORAGE_KEY = 'subsense_download_manifest';
  var BRIDGE_STORAGE_KEY = 'subsense_download_bridge_scheme';
  var DEFAULT_BRIDGE_SCHEME = 'mxsub';
  var SUBSENSE_BASE = DEFAULT_SUBSENSE_BASE;
  var BRIDGE_SCHEME = DEFAULT_BRIDGE_SCHEME;
  var subtitleCache = Object.create ? Object.create(null) : {};

  function log() {
    var args = ['[SubSense download]'].concat(Array.prototype.slice.call(arguments));
    if (typeof console !== 'undefined' && console.log) console.log.apply(console, args);
  }

  function notify(message) {
    if (typeof Lampa !== 'undefined' && Lampa.Noty && typeof Lampa.Noty.show === 'function') {
      Lampa.Noty.show(message);
    } else {
      log(message);
    }
  }

  function stringValue(value, fallback) {
    if (value === null || typeof value === 'undefined') return fallback || '';
    return String(value);
  }

  function normalizeManifestBase(value) {
    var base = stringValue(value, '').trim();

    if (!base) return DEFAULT_SUBSENSE_BASE;

    base = base.replace(/^stremio:\/\//i, 'https://');
    base = base.replace(/\/manifest\.json(?:\?.*)?$/i, '');
    base = base.replace(/\/+$/, '');

    if (!/^https?:\/\//i.test(base)) return DEFAULT_SUBSENSE_BASE;
    return base;
  }

  function normalizeBridgeScheme(value) {
    var scheme = stringValue(value, DEFAULT_BRIDGE_SCHEME)
      .trim()
      .replace(/[^a-z0-9+.-]/gi, '');

    return scheme || DEFAULT_BRIDGE_SCHEME;
  }

  function looksLikeImdb(value) {
    return typeof value === 'string' && /^tt\d+$/i.test(value.trim());
  }

  function extractImdbId(movie) {
    if (!movie || typeof movie !== 'object') return null;

    if (looksLikeImdb(movie.imdb_id)) return movie.imdb_id.trim();
    if (movie.external_ids && looksLikeImdb(movie.external_ids.imdb_id)) {
      return movie.external_ids.imdb_id.trim();
    }
    if (looksLikeImdb(movie.id)) return movie.id.trim();

    var seen = [];

    function scan(value, depth) {
      if (!value || typeof value !== 'object' || depth > 4) return null;
      if (seen.indexOf(value) >= 0) return null;
      seen.push(value);

      for (var key in value) {
        if (!Object.prototype.hasOwnProperty.call(value, key)) continue;

        var item = value[key];
        if (/imdb/i.test(key) && looksLikeImdb(item)) return item.trim();

        if (item && typeof item === 'object') {
          var found = scan(item, depth + 1);
          if (found) return found;
        }
      }

      return null;
    }

    return scan(movie, 0);
  }

  function isSeries(movie) {
    if (!movie) return false;

    return !!(
      movie.first_air_date ||
      movie.number_of_seasons ||
      movie.number_of_episodes ||
      movie.media_type === 'tv' ||
      movie.type === 'series' ||
      (movie.name && movie.original_name)
    );
  }

  function detectFileType(url, declaredFormat) {
    var declared = stringValue(declaredFormat, '').toLowerCase().replace(/^\./, '');
    if (/^(srt|vtt|zip|rar)$/.test(declared)) return declared;

    var clean = stringValue(url, '').split('?')[0].split('#')[0];
    try {
      clean = decodeURIComponent(clean);
    } catch (e) {}

    if (/\.srt$/i.test(clean)) return 'srt';
    if (/\.vtt$/i.test(clean)) return 'vtt';
    if (/\.zip$/i.test(clean)) return 'zip';
    if (/\.rar$/i.test(clean)) return 'rar';
    return 'unknown';
  }

  function isHttpUrl(url) {
    return /^https?:\/\//i.test(stringValue(url, '').trim());
  }

  function normalizeSubtitle(item, index) {
    var source = item;
    var url = '';

    if (typeof source === 'string') {
      url = source;
      source = {};
    } else if (source && typeof source === 'object') {
      url = source.url || source.file || source.link || source.download_url || source.download || '';
    } else {
      return null;
    }

    url = stringValue(url, '').trim();
    if (url.indexOf('//') === 0) url = 'https:' + url;
    if (!isHttpUrl(url)) return null;

    var language = stringValue(
      source.language || source.lang || source.lang_code || source.iso_639_1 || 'vi',
      'vi'
    ).trim().toLowerCase();

    var label = stringValue(
      source.label || source.name || source.title || source.release || source.source,
      ''
    ).trim();

    if (!label) label = language === 'vi' ? 'Tiếng Việt' : 'Sub ' + (index + 1);

    var format = detectFileType(url, source.format || source.ext || source.extension || '');

    return {
      url: url,
      label: label,
      language: language || 'vi',
      format: format,
      source: source
    };
  }

  function subtitlesFromResponse(data) {
    var list = [];

    if (Array.isArray(data)) list = data;
    else if (data && Array.isArray(data.subtitles)) list = data.subtitles;
    else if (data && Array.isArray(data.results)) list = data.results;
    else if (data && Array.isArray(data.result)) list = data.result;

    return list.map(normalizeSubtitle).filter(function (item) {
      return !!item;
    });
  }

  function subtitleSortScore(subtitle) {
    var language = stringValue(subtitle.language, '').toLowerCase();
    var languageScore = /^(vi|vie|vietnamese)$/.test(language) ? 0 : 1;
    var formatScore = {
      srt: 0,
      vtt: 1,
      zip: 2,
      unknown: 3,
      rar: 4
    }[subtitle.format];

    if (typeof formatScore === 'undefined') formatScore = 3;
    return languageScore * 10 + formatScore;
  }

  function sortSubtitles(list) {
    return list.slice().sort(function (a, b) {
      var score = subtitleSortScore(a) - subtitleSortScore(b);
      if (score) return score;
      return a.label.localeCompare(b.label);
    });
  }

  function buildSubtitleRequestId(imdbId, type, season, episode) {
    var id = imdbId;

    if (type === 'series') {
      if (!season || !episode) return null;
      id += ':' + season + ':' + episode;
    }

    return id;
  }

  function fetchSubs(imdbId, type, season, episode, callback) {
    var id = buildSubtitleRequestId(imdbId, type, season, episode);
    if (!id) {
      callback(new Error('Thiếu số mùa/tập của series'), []);
      return;
    }

    var key = SUBSENSE_BASE + '|' + type + '|' + id;
    if (subtitleCache[key]) {
      callback(null, subtitleCache[key].slice());
      return;
    }

    var url = SUBSENSE_BASE + '/subtitles/' + encodeURIComponent(type) + '/' + encodeURIComponent(id) + '.json';
    log('request:', url);

    if (typeof $ === 'undefined' || !$.ajax) {
      callback(new Error('Lampa network API không sẵn sàng'), []);
      return;
    }

    $.ajax({
      url: url,
      type: 'GET',
      dataType: 'json',
      timeout: 30000,
      success: function (data) {
        var list = sortSubtitles(subtitlesFromResponse(data));
        subtitleCache[key] = list.slice();
        callback(null, list);
      },
      error: function (xhr, status, error) {
        var message = error || status || (xhr && xhr.status) || 'request failed';
        callback(new Error('Không lấy được danh sách sub: ' + message), []);
      }
    });
  }

  function getEpisodes(context) {
    var data = context && context.data;
    var episodesData = data && data.episodes;
    var source = [];

    if (episodesData && Array.isArray(episodesData.episodes)) source = episodesData.episodes;
    else if (episodesData && Array.isArray(episodesData.episodes_original)) source = episodesData.episodes_original;

    var result = [];
    var seen = {};

    source.forEach(function (episode) {
      if (!episode) return;

      var season = parseInt(episode.season_number || episode.season || 0, 10);
      var number = parseInt(episode.episode_number || episode.episode || 0, 10);
      if (season < 1 || number < 1) return;

      var key = season + ':' + number;
      if (seen[key]) return;
      seen[key] = true;

      result.push({
        season: season,
        episode: number,
        name: stringValue(episode.name || episode.title, '').trim(),
        airDate: stringValue(episode.air_date, '').trim(),
        source: episode
      });
    });

    result.sort(function (a, b) {
      return a.season - b.season || a.episode - b.episode;
    });

    return result;
  }

  function episodeCode(episode) {
    return 'S' + ('0' + episode.season).slice(-2) + 'E' + ('0' + episode.episode).slice(-2);
  }

  function episodeTitle(episode) {
    var title = episodeCode(episode);
    if (episode.name) title += ' · ' + episode.name;
    if (episode.airDate) title += ' · ' + episode.airDate;
    return title;
  }

  function showEpisodeSelector(context) {
    var episodes = getEpisodes(context);
    var select = typeof Lampa !== 'undefined' && Lampa.Select;

    if (!episodes.length) {
      var movie = context.movie;
      var season = parseInt(movie && (movie.season || movie.season_number), 10);
      var episode = parseInt(movie && (movie.episode || movie.episode_number), 10);

      if (season > 0 && episode > 0) {
        loadSubtitlesForContext(context, season, episode, episodeCode({ season: season, episode: episode }));
      } else {
        notify('Không tìm thấy danh sách tập. Hãy mở đúng tập rồi thử lại.');
      }
      return;
    }

    if (episodes.length === 1) {
      loadSubtitlesForContext(context, episodes[0].season, episodes[0].episode, episodeCode(episodes[0]));
      return;
    }

    if (!select || typeof select.show !== 'function') {
      notify('Phiên bản Lampa này không có cửa sổ chọn tập.');
      return;
    }

    select.show({
      title: 'Chọn tập để tải phụ đề',
      fullsize: true,
      items: episodes.map(function (episode) {
        return {
          title: episodeTitle(episode),
          subtitle: 'SubSense',
          template: 'selectbox_item',
          episode: episode
        };
      }),
      onSelect: function (item) {
        loadSubtitlesForContext(context, item.episode.season, item.episode.episode, episodeCode(item.episode));
      }
    });
  }

  function subtitleTitle(subtitle, index) {
    var language = subtitle.language ? subtitle.language.toUpperCase() : 'SUB';
    var format = subtitle.format === 'unknown' ? 'file' : subtitle.format.toUpperCase();
    return language + ' · ' + subtitle.label + ' [' + format + ']';
  }

  function showSubtitleSelector(context, subtitles, season, episode, episodeLabel) {
    var select = typeof Lampa !== 'undefined' && Lampa.Select;
    if (!select || typeof select.show !== 'function') {
      notify('Phiên bản Lampa này không có cửa sổ chọn phụ đề.');
      return;
    }

    var title = 'Chọn phụ đề để tải';
    if (context.movie && (context.movie.title || context.movie.name)) {
      title += ': ' + (context.movie.title || context.movie.name);
    }
    if (episodeLabel) title += ' · ' + episodeLabel;

    select.show({
      title: title,
      fullsize: true,
      items: subtitles.map(function (subtitle, index) {
        return {
          title: subtitleTitle(subtitle, index),
          subtitle: subtitle.format === 'rar' ? 'RAR không tự chuyển sang SRT' : 'Lưu vào Downloads/Subtitles',
          template: 'selectbox_item',
          subtitleData: subtitle
        };
      }),
      onSelect: function (item) {
        downloadSubtitle(item.subtitleData, context, season, episode);
      }
    });
  }

  function cleanFilenamePart(value, fallback) {
    var text = stringValue(value, fallback || '').trim();

    text = text
      .replace(/[\u0000-\u001f<>:"/\\|?*]/g, ' ')
      .replace(/\s+/g, ' ')
      .replace(/[. ]+$/g, '')
      .trim();

    return text || (fallback || 'subtitle');
  }

  function outputExtension(subtitle) {
    return subtitle && subtitle.format === 'rar' ? '.rar' : '.srt';
  }

  function buildFilename(movie, subtitle, season, episode) {
    var title = cleanFilenamePart(
      movie && (movie.title || movie.name || movie.original_title || movie.original_name),
      'subtitle'
    );
    var suffix = '';

    if (season && episode) {
      suffix += ' ' + episodeCode({ season: season, episode: episode });
    }

    var label = cleanFilenamePart(subtitle && subtitle.label, '');
    if (label && !/^(sub|subtitle|ti[eế]ng vi[eệ]t)$/i.test(label)) suffix += ' - ' + label;

    var extension = outputExtension(subtitle);
    var filename = cleanFilenamePart(title + suffix, 'subtitle');
    var maximum = 120 - extension.length;

    if (filename.length > maximum) filename = filename.slice(0, maximum).replace(/[. ]+$/g, '');
    return (filename || 'subtitle') + extension;
  }

  function isAndroid() {
    try {
      if (typeof Lampa !== 'undefined' && Lampa.Platform && Lampa.Platform.is) {
        return Lampa.Platform.is('android');
      }
    } catch (e) {}

    return typeof AndroidJS !== 'undefined' || (typeof Android !== 'undefined' && Android.openBrowser);
  }

  function getNativeBrowser() {
    if (typeof Android !== 'undefined' && Android && typeof Android.openBrowser === 'function') return Android;
    if (typeof AndroidJS !== 'undefined' && AndroidJS && typeof AndroidJS.openBrowser === 'function') return AndroidJS;
    if (typeof Lampa !== 'undefined' && Lampa.Android && typeof Lampa.Android.openBrowser === 'function') return Lampa.Android;
    return null;
  }

  function bridgeUrl(subtitle, filename, title) {
    return BRIDGE_SCHEME + '://download?url=' + encodeURIComponent(subtitle.url) +
      '&filename=' + encodeURIComponent(filename) +
      '&format=' + encodeURIComponent(subtitle.format) +
      '&title=' + encodeURIComponent(title || filename);
  }

  function openWithBridge(subtitle, filename, title) {
    var nativeBrowser = getNativeBrowser();
    if (!nativeBrowser) return false;

    try {
      nativeBrowser.openBrowser(bridgeUrl(subtitle, filename, title));
      return true;
    } catch (error) {
      log('bridge launch failed:', error);
      return false;
    }
  }

  function vttToSrt(text) {
    var lines = stringValue(text, '').replace(/\r/g, '').split('\n');
    var output = [];
    var cue = 0;
    var active = false;

    lines.forEach(function (line, index) {
      if (/^WEBVTT(?:\s|$)/i.test(line) && index < 3) return;

      if (line.indexOf('-->') >= 0) {
        cue += 1;
        // A timestamp line is the only place where dots should become SRT commas.
        output.push(cue);
        output.push(line.replace(/\./g, ','));
        active = true;
        return;
      }

      if (!line) {
        if (active) output.push('');
        active = false;
        return;
      }

      if (active) output.push(line);
    });

    return output.join('\n');
  }

  function browserDownload(subtitle, filename) {
    if (subtitle.format === 'zip' || subtitle.format === 'rar' || typeof $ === 'undefined' || !$.ajax) {
      var link = document.createElement('a');
      link.href = subtitle.url;
      link.download = filename;
      link.target = '_blank';
      document.body.appendChild(link);
      link.click();
      setTimeout(function () {
        if (link.parentNode) link.parentNode.removeChild(link);
      }, 1000);
      notify('Đã mở link phụ đề. Trình duyệt sẽ xử lý việc tải file.');
      return;
    }

    $.ajax({
      url: subtitle.url,
      type: 'GET',
      dataType: 'text',
      timeout: 30000,
      success: function (text) {
        if (subtitle.format === 'vtt') text = vttToSrt(text);
        var blobUrl = URL.createObjectURL(new Blob([text], { type: 'text/plain;charset=utf-8' }));
        var link = document.createElement('a');
        link.href = blobUrl;
        link.download = filename;
        document.body.appendChild(link);
        link.click();
        setTimeout(function () {
          URL.revokeObjectURL(blobUrl);
          if (link.parentNode) link.parentNode.removeChild(link);
        }, 3000);
        notify('Đã tải phụ đề: ' + filename);
      },
      error: function () {
        notify('Không tải được phụ đề trong trình duyệt. Hãy cài SubSense Termux Bridge.');
      }
    });
  }

  function downloadSubtitle(subtitle, context, season, episode) {
    if (!subtitle || !subtitle.url) return;

    if (subtitle.format === 'rar') {
      notify('Bản RAR không được chuyển sang SRT. Hãy chọn bản SRT/ZIP khác.');
      return;
    }

    var filename = buildFilename(context.movie, subtitle, season, episode);
    var title = cleanFilenamePart(context.movie && (context.movie.title || context.movie.name), 'SubSense');

    if (isAndroid() && openWithBridge(subtitle, filename, title)) {
      notify('Đã gửi tải phụ đề qua Termux: ' + filename);
      return;
    }

    if (isAndroid()) {
      notify('Chưa tìm thấy SubSense Termux Bridge. Cài APK bridge rồi thử lại.');
      return;
    }

    browserDownload(subtitle, filename);
  }

  function loadSubtitlesForContext(context, season, episode, episodeLabel) {
    if (context.loading) return;
    context.loading = true;

    var movie = context.movie;
    var imdbId = extractImdbId(movie);
    var type = isSeries(movie) ? 'series' : 'movie';

    if (!imdbId) {
      context.loading = false;
      notify('Phim không có IMDB ID nên SubSense không thể tìm phụ đề.');
      return;
    }

    notify('Đang tìm phụ đề SubSense...');

    fetchSubs(imdbId, type, season, episode, function (error, subtitles) {
      context.loading = false;

      if (error) {
        log(error.message || error);
        notify(error.message || 'Không lấy được danh sách phụ đề.');
        return;
      }

      if (!subtitles.length) {
        notify('Không tìm thấy phụ đề cho nội dung này.');
        return;
      }

      showSubtitleSelector(context, subtitles, season, episode, episodeLabel);
    });
  }

  function createDownloadButton(event) {
    if (!event || !event.data || !event.data.movie || !event.body) return;

    var movie = event.data.movie;
    if (!extractImdbId(movie)) {
      log('skip card without imdb id');
      return;
    }

    var body = $(event.body);
    var buttons = body.find('.full-start-new__buttons').first();
    if (!buttons.length) buttons = body.find('.full-start__buttons').first();
    if (!buttons.length || buttons.find('.subsense-download-button').length) return;

    var button = $(
      '<div class="full-start__button selector subsense-download-button">' +
        '<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">' +
          '<path d="M12 3V15M12 15L7 10M12 15L17 10M5 20H19" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"/>' +
        '</svg>' +
        '<span>Tải phụ đề</span>' +
      '</div>'
    );

    var context = {
      movie: movie,
      data: event.data,
      loading: false
    };
    var lastAction = 0;

    button.on('hover:enter.subsenseDownload click.subsenseDownload', function (inputEvent) {
      var now = Date.now();
      if (now - lastAction < 500) return;
      lastAction = now;

      if (inputEvent && inputEvent.preventDefault) inputEvent.preventDefault();

      if (isSeries(movie)) showEpisodeSelector(context);
      else loadSubtitlesForContext(context, null, null, null);
    });

    var options = buttons.find('.button--options').first();
    if (options.length) options.before(button);
    else buttons.append(button);
  }

  function registerSettings() {
    if (typeof Lampa === 'undefined' || !Lampa.SettingsApi || typeof Lampa.SettingsApi.addComponent !== 'function') return;
    if (window.__subsenseDownloadSettingsRegistered) return;
    window.__subsenseDownloadSettingsRegistered = true;

    var manifest = '';
    var scheme = DEFAULT_BRIDGE_SCHEME;

    if (Lampa.Storage && Lampa.Storage.get) {
      manifest = Lampa.Storage.get(MANIFEST_STORAGE_KEY, '');
      scheme = Lampa.Storage.get(BRIDGE_STORAGE_KEY, DEFAULT_BRIDGE_SCHEME);
    }

    SUBSENSE_BASE = normalizeManifestBase(manifest);
    BRIDGE_SCHEME = normalizeBridgeScheme(scheme);

    try {
      Lampa.SettingsApi.addComponent({
        component: 'subsense_download',
        icon: '<svg width="30" height="30" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 3v12m0 0-5-5m5 5 5-5M5 21h14" stroke-linecap="round" stroke-linejoin="round"/></svg>',
        name: 'SubSense tải xuống'
      });

      Lampa.SettingsApi.addParam({
        component: 'subsense_download',
        param: {
          name: MANIFEST_STORAGE_KEY,
          type: 'input',
          values: manifest,
          default: manifest,
          placeholder: 'https://subsense.nepiraw.com/.../manifest.json'
        },
        field: {
          name: 'SubSense manifest URL',
          description: 'Có thể để trống để dùng cấu hình tiếng Việt mặc định.'
        },
        onChange: function (value) {
          SUBSENSE_BASE = normalizeManifestBase(value);
          if (Lampa.Storage && Lampa.Storage.set) Lampa.Storage.set(MANIFEST_STORAGE_KEY, stringValue(value, ''));
          subtitleCache = Object.create ? Object.create(null) : {};
        }
      });

      Lampa.SettingsApi.addParam({
        component: 'subsense_download',
        param: {
          name: BRIDGE_STORAGE_KEY,
          type: 'input',
          values: scheme,
          default: DEFAULT_BRIDGE_SCHEME,
          placeholder: DEFAULT_BRIDGE_SCHEME
        },
        field: {
          name: 'Bridge URI scheme',
          description: 'Giữ mxsub nếu dùng APK trong thư mục mx-sub-bridge.'
        },
        onChange: function (value) {
          BRIDGE_SCHEME = normalizeBridgeScheme(value);
          if (Lampa.Storage && Lampa.Storage.set) Lampa.Storage.set(BRIDGE_STORAGE_KEY, BRIDGE_SCHEME);
        }
      });
    } catch (error) {
      log('settings registration failed:', error);
    }
  }

  if (typeof Lampa !== 'undefined' && Lampa.Listener && Lampa.Listener.follow) {
    Lampa.Listener.follow('full', function (event) {
      if (event && event.type === 'complite') createDownloadButton(event);
    });
  }

  registerSettings();

  window.SubSenseDownloadPlugin = {
    fetchSubs: fetchSubs,
    extractImdbId: extractImdbId,
    downloadSubtitle: downloadSubtitle,
    clearCache: function () {
      subtitleCache = Object.create ? Object.create(null) : {};
    }
  };

  log('download-only plugin loaded');
})();
