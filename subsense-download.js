(function () {
'use strict';

var SUBSENSE_BASE = 'https://subsense.nepiraw.com/lxolz7e9-%7B%22languages%22%3A%5B%22vi%22%5D%7D';

function log() {
    var args = ['[SubSense]'].concat(Array.prototype.slice.call(arguments));
    console.log.apply(console, args);
}

function extractImdbId(movie) {
    if (!movie) return null;
    if (movie.imdb_id) return movie.imdb_id;
    if (movie.external_ids && movie.external_ids.imdb_id) return movie.external_ids.imdb_id;
    if (typeof movie.id === 'string' && /^tt\d+/.test(movie.id)) return movie.id;
    function scan(obj, depth) {
      if (!obj || typeof obj !== 'object' || depth > 3) return null;
      for (var key in obj) {
        if (!obj.hasOwnProperty(key)) continue;
        var val = obj[key];
        if (/imdb/i.test(key) && typeof val === 'string' && /^tt\d+/.test(val)) return val;
        if (typeof val === 'object') { var found = scan(val, depth + 1); if (found) return found; }
      }
      return null;
    }
    return scan(movie, 0);
}

function fetchSubs(imdbId, type, season, episode, cb) {
    var id = imdbId;
    if (season && episode) id += ':' + season + ':' + episode;
    else if (season) id += ':' + season;

    var url = SUBSENSE_BASE + '/subtitles/' + type + '/' + id + '.json';
    log('API:', url);

    fetch(url).then(function(r) { return r.json(); })
        .then(function(d) { cb(null, d.subtitles || []); })
        .catch(function(e) { cb(e); });
}

function downloadSub(sub, cb) {
    fetch(sub.url).then(function(r) { return r.text(); })
        .then(function(text) { cb(null, text, sub.label); })
        .catch(function(e) { cb(e); });
}

function saveToFile(filename, content) {
    // Tạo link download
    var blob = new Blob([content], { type: 'text/plain' });
    var url = URL.createObjectURL(blob);
    var a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    setTimeout(function() {
        URL.revokeObjectURL(url);
        if (a.parentNode) a.parentNode.removeChild(a);
    }, 3000);
}

function attachAutoSub(playData) {
    log('Play data:', playData);

    var imdbId = extractImdbId(playData);
    if (!imdbId) { log('No IMDB'); return; }

    var season = playData.season || 0;
    var episode = playData.episode || 0;
    var type = (season > 0 || episode > 0) ? 'series' : 'movie';

    log('IMDB:', imdbId, 'S:', season, 'E:', episode);

    fetchSubs(imdbId, type, season, episode, function(err, subs) {
        if (err || !subs.length) { log('No subs'); return; }
        log('Found', subs.length, 'subs');

        // Tải sub đầu tiên (tiếng Việt ưu tiên)
        var viSub = subs.find(function(s) { return s.lang === 'vi'; });
        var sub = viSub || subs[0];

        downloadSub(sub, function(err, text, label) {
            if (err || !text) { log('Download failed'); return; }

            var filename = (label || 'sub').replace(/[^a-zA-Z0-9\-_ ]/g, '').trim() + '.srt';
            saveToFile(filename, text);
            log('Saved:', filename);
            Lampa.Noty.show('Đã tải phụ đề: ' + filename);
        });
    });
}

// Hook player ready
Lampa.Player.listener.follow('ready', function(data) {
    if (data) {
        setTimeout(function() { attachAutoSub(data); }, 2000);
    }
});

// Hook movie page
Lampa.Listener.follow('full', function(e) {
    if (e.type === 'complite' && e.data && e.data.movie) {
        window._subsense_movie = e.data.movie;
    }
});

log('plugin loaded - download mode');

// Settings
try {
    Lampa.SettingsApi.addComponent({
        component: 'subsense',
        icon: '<svg width="30" height="30" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="2" y="4" width="20" height="16" rx="2"/><path d="M6 9h8"/><path d="M6 13h12"/><path d="M6 17h6"/></svg>',
        name: 'SubSense'
    });
    Lampa.SettingsApi.addParam({
        component: 'subsense',
        param: { name: 'subsense_manifest', type: 'input', values: '', default: '', placeholder: 'https://subsense.nepiraw.com/xxxx/manifest.json' },
        field: { name: 'SubSense manifest URL', description: 'Lấy tại subsense.nepiraw.com/configure → Copy Manifest URL' },
        onChange: function(v) { SUBSENSE_BASE = v.replace(/\/manifest\.json.*$/, '').replace(/^stremio:\/\//, 'https://'); }
    });
} catch(e) {}

// Đọc config
var savedUrl = Lampa.Storage.get('subsense_manifest', '');
if (savedUrl) {
    SUBSENSE_BASE = savedUrl.replace(/\/manifest\.json.*$/, '').replace(/^stremio:\/\//, 'https://');
}

window.SubSensePlugin = { fetchSubs: fetchSubs, attachAutoSub: attachAutoSub };
})();
