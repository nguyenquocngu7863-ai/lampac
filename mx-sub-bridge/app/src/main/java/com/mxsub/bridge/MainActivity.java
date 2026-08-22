package com.mxsub.bridge;

import android.app.Activity;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;
import android.os.Environment;
import android.util.Base64;
import android.util.Log;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStreamReader;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.ArrayList;

/**
 * MX Sub Bridge v2
 * Nhận video URL + subtitleMeta từ Lampa → Tải sub về máy → Mở MX Player
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
        String cleanUrl = removeQueryParam(videoUrl, "subtitleMeta");

        // Parse subtitle metadata
        ArrayList<String[]> subs = new ArrayList<>();
        if (subtitleMeta != null && !subtitleMeta.isEmpty()) {
            subs = parseSubtitleMeta(subtitleMeta);
            Log.d(TAG, "Parsed " + subs.size() + " subtitles");
        }

        // Tải sub về máy và mở MX Player trong background
        final String finalUrl = cleanUrl;
        final String finalTitle = title;
        final ArrayList<String[]> finalSubs = subs;

        new Thread(() -> {
            ArrayList<String[]> localSubs = new ArrayList<>();

            for (String[] sub : finalSubs) {
                String localPath = downloadSubtitle(sub[0], sub[1]);
                if (localPath != null) {
                    localSubs.add(new String[]{localPath, sub[1]});
                    Log.d(TAG, "Downloaded: " + sub[1] + " -> " + localPath);
                }
            }

            // Mở MX Player
            launchMxPlayer(finalUrl, finalTitle, localSubs);

            runOnUiThread(this::finish);
        }).start();
    }

    /**
     * Tải sub từ URL về máy
     */
    private String downloadSubtitle(String subUrl, String label) {
        try {
            URL url = new URL(subUrl);
            HttpURLConnection conn = (HttpURLConnection) url.openConnection();
            conn.setConnectTimeout(10000);
            conn.setReadTimeout(10000);

            if (conn.getResponseCode() != 200) {
                Log.e(TAG, "Download failed: " + conn.getResponseCode());
                return null;
            }

            // Tạo thư mục lưu sub
            File subDir = new File(getCacheDir(), "subs");
            if (!subDir.exists()) subDir.mkdirs();

            // Tên file sub
            String ext = ".srt";
            if (subUrl.contains(".vtt")) ext = ".vtt";
            String fileName = "sub_" + System.currentTimeMillis() + ext;
            File subFile = new File(subDir, fileName);

            // Lưu file
            FileOutputStream fos = new FileOutputStream(subFile);
            byte[] buffer = new byte[4096];
            int bytesRead;
            while ((bytesRead = conn.getInputStream().read(buffer)) != -1) {
                fos.write(buffer, 0, bytesRead);
            }
            fos.close();
            conn.disconnect();

            Log.d(TAG, "Saved sub: " + subFile.getAbsolutePath() + " (" + subFile.length() + " bytes)");
            return subFile.getAbsolutePath();

        } catch (Exception e) {
            Log.e(TAG, "Download error: " + e.getMessage());
            return null;
        }
    }

    /**
     * Parse subtitleMeta (base64url JSON array)
     */
    private ArrayList<String[]> parseSubtitleMeta(String meta) {
        ArrayList<String[]> subs = new ArrayList<>();
        try {
            String b64 = meta.replace('-', '+').replace('_', '/');
            switch (b64.length() % 4) {
                case 2: b64 += "=="; break;
                case 3: b64 += "="; break;
            }

            byte[] decoded = Base64.decode(b64, Base64.DEFAULT);
            String json = new String(decoded, "UTF-8");
            Log.d(TAG, "subtitleMeta: " + json);

            JSONArray arr = new JSONArray(json);
            for (int i = 0; i < arr.length(); i++) {
                JSONObject obj = arr.getJSONObject(i);
                String url = obj.optString("url", "");
                String label = obj.optString("label", "Sub " + (i + 1));
                if (!url.isEmpty()) {
                    subs.add(new String[]{url, label});
                }
            }
        } catch (Exception e) {
            Log.e(TAG, "Parse error: " + e.getMessage());
        }
        return subs;
    }

    /**
     * Mở MX Player với video + sub local
     */
    private void launchMxPlayer(String videoUrl, String title, ArrayList<String[]> subs) {
        try {
            Intent intent = new Intent(Intent.ACTION_VIEW);
            intent.setDataAndType(Uri.parse(videoUrl), "video/*");
            intent.putExtra(Intent.EXTRA_TITLE, title);
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);

            // Thêm sub local
            if (!subs.isEmpty()) {
                ArrayList<String> subPaths = new ArrayList<>();
                ArrayList<String> subNames = new ArrayList<>();
                for (String[] sub : subs) {
                    subPaths.add(sub[0]);
                    subNames.add(sub[1]);
                }
                intent.putExtra("subs", subPaths);
                intent.putExtra("subs.name", subNames);
                Log.d(TAG, "Passing " + subs.size() + " local subs to MX Player");
            }

            // Thử MX Player Pro
            intent.setPackage(MX_PLAYER_PRO);
            try {
                startActivity(intent);
                Log.d(TAG, "Launched MX Player Pro");
                return;
            } catch (Exception e) {}

            // MX Player thường
            intent.setPackage(MX_PLAYER);
            try {
                startActivity(intent);
                Log.d(TAG, "Launched MX Player");
                return;
            } catch (Exception e) {}

            // Fallback
            intent.setPackage(null);
            startActivity(intent);
            Log.d(TAG, "Launched default player");

        } catch (Exception e) {
            Log.e(TAG, "Launch error: " + e.getMessage());
        }
    }

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
