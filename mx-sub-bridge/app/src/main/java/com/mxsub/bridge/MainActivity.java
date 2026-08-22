package com.mxsub.bridge;

import android.app.Activity;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;
import android.util.Base64;
import android.util.Log;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.ArrayList;

/**
 * MX Sub Bridge
 * Nhận video URL + subtitleMeta từ Lampa → Mở MX Player với sub
 *
 * subtitleMeta: base64url (không padding) của JSON array [{url, label, language}, ...]
 * Ví dụ URL: http://server/stream/video.mkv?subtitleMeta=eyJ1cmwiOiJodHRw...
 */
public class MainActivity extends Activity {
    private static final String TAG = "MXSubBridge";
    private static final String MX_PLAYER = "com.mxtech.videoplayer.ad";
    private static final String MX_PLAYER_PRO = "com.mxtech.videoplayer.pro";

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        Intent intent = getIntent();
        if (intent == null || intent.getData() == null) {
            Log.e(TAG, "No intent data");
            finish();
            return;
        }

        Uri videoUri = intent.getData();
        String videoUrl = videoUri.toString();
        String title = intent.getStringExtra(Intent.EXTRA_TITLE);
        if (title == null) title = "Video";

        Log.d(TAG, "Video: " + videoUrl);

        // Tách subtitleMeta khỏi URL
        String subtitleMeta = videoUri.getQueryParameter("subtitleMeta");

        // Xóa subtitleMeta khỏi URL gốc (MX Player không cần param này)
        String cleanUrl = removeQueryParam(videoUrl, "subtitleMeta");

        // Parse subtitle metadata
        ArrayList<String[]> subs = new ArrayList<>();
        if (subtitleMeta != null && !subtitleMeta.isEmpty()) {
            subs = parseSubtitleMeta(subtitleMeta);
            Log.d(TAG, "Parsed " + subs.size() + " subtitles from subtitleMeta");
        }

        // Mở MX Player
        launchMxPlayer(cleanUrl, title, subs);

        finish();
    }

    /**
     * Parse subtitleMeta (base64url JSON array)
     */
    private ArrayList<String[]> parseSubtitleMeta(String meta) {
        ArrayList<String[]> subs = new ArrayList<>();
        try {
            // Thêm padding nếu cần
            String b64 = meta.replace('-', '+').replace('_', '/');
            switch (b64.length() % 4) {
                case 2: b64 += "=="; break;
                case 3: b64 += "="; break;
            }

            byte[] decoded = Base64.decode(b64, Base64.DEFAULT);
            String json = new String(decoded, "UTF-8");
            Log.d(TAG, "subtitleMeta JSON: " + json);

            JSONArray arr = new JSONArray(json);
            for (int i = 0; i < arr.length(); i++) {
                JSONObject obj = arr.getJSONObject(i);
                String url = obj.optString("url", "");
                String label = obj.optString("label", "Sub " + (i + 1));
                String lang = obj.optString("language", "vi");
                if (!url.isEmpty()) {
                    subs.add(new String[]{url, label, lang});
                    Log.d(TAG, "Sub: " + label + " -> " + url);
                }
            }
        } catch (Exception e) {
            Log.e(TAG, "Parse subtitleMeta error: " + e.getMessage());
        }
        return subs;
    }

    /**
     * Mở MX Player với video + subtitles
     */
    private void launchMxPlayer(String videoUrl, String title, ArrayList<String[]> subs) {
        try {
            Intent intent = new Intent(Intent.ACTION_VIEW);
            intent.setDataAndType(Uri.parse(videoUrl), "video/*");
            intent.putExtra(Intent.EXTRA_TITLE, title);
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);

            // Thêm subtitles
            if (!subs.isEmpty()) {
                ArrayList<String> subUrls = new ArrayList<>();
                ArrayList<String> subNames = new ArrayList<>();
                for (String[] sub : subs) {
                    subUrls.add(sub[0]);
                    subNames.add(sub[1]);
                }
                // MX Player format
                intent.putExtra("subs", subUrls);
                intent.putExtra("subs.name", subNames);
            }

            // Thử MX Player Pro trước
            intent.setPackage(MX_PLAYER_PRO);
            try {
                startActivity(intent);
                Log.d(TAG, "Launched MX Player Pro with " + subs.size() + " subs");
                return;
            } catch (Exception e) {
                Log.d(TAG, "MX Player Pro not found");
            }

            // MX Player thường
            intent.setPackage(MX_PLAYER);
            try {
                startActivity(intent);
                Log.d(TAG, "Launched MX Player with " + subs.size() + " subs");
                return;
            } catch (Exception e) {
                Log.d(TAG, "MX Player not found");
            }

            // Fallback: bất kỳ player nào
            intent.setPackage(null);
            startActivity(intent);
            Log.d(TAG, "Launched default player");

        } catch (Exception e) {
            Log.e(TAG, "Launch error: " + e.getMessage());
        }
    }

    /**
     * Xóa query param khỏi URL
     */
    private String removeQueryParam(String url, String param) {
        try {
            Uri uri = Uri.parse(url);
            Uri.Builder builder = uri.buildUpon().clearQuery();
            for (String key : uri.getQueryParameterNames()) {
                if (!key.equals(param)) {
                    builder.appendQueryParameter(key, uri.getQueryParameter(key));
                }
            }
            return builder.build().toString();
        } catch (Exception e) {
            return url;
        }
    }
}
