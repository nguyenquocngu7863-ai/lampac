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

import java.io.File;
import java.io.FileOutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.ArrayList;

/**
 * MX Sub Bridge v3
 * Nhận subtitleMeta → Tải TẤT CẢ sub về máy → Mở MX Player
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
            toast("Không có dữ liệu");
            finish();
            return;
        }

        Uri videoUri = intent.getData();
        String videoUrl = videoUri.toString();
        String title = intent.getStringExtra(Intent.EXTRA_TITLE);
        if (title == null) title = "Video";

        // Tách subtitleMeta
        String subtitleMeta = videoUri.getQueryParameter("subtitleMeta");
        String cleanUrl = removeQueryParam(videoUrl, "subtitleMeta");

        // Parse subtitle metadata
        ArrayList<String[]> subs = new ArrayList<>();
        if (subtitleMeta != null && !subtitleMeta.isEmpty()) {
            subs = parseSubtitleMeta(subtitleMeta);
        }

        if (subs.isEmpty()) {
            toast("Không tìm thấy phụ đề");
            launchMxPlayer(cleanUrl, title, new ArrayList<>());
            finish();
            return;
        }

        toast("Đang tải " + subs.size() + " phụ đề...");

        // Tải TẤT CẢ sub về máy
        final String finalUrl = cleanUrl;
        final String finalTitle = title;
        final ArrayList<String[]> finalSubs = subs;

        new Thread(() -> {
            ArrayList<String[]> localSubs = new ArrayList<>();
            int downloaded = 0;

            for (String[] sub : finalSubs) {
                String localPath = downloadSubtitle(sub[0], sub[1]);
                if (localPath != null) {
                    localSubs.add(new String[]{localPath, sub[1]});
                    downloaded++;
                }
            }

            final int finalDownloaded = downloaded;
            runOnUiThread(() -> {
                toast("Đã tải " + finalDownloaded + "/" + finalSubs.size() + " phụ đề");
                
                // Mở MX Player
                launchMxPlayer(finalUrl, finalTitle, localSubs);
                finish();
            });
        }).start();
    }

    private void toast(String msg) {
        android.widget.Toast.makeText(this, msg, android.widget.Toast.LENGTH_LONG).show();
    }

    /**
     * Tải sub về máy
     */
    private String downloadSubtitle(String subUrl, String label) {
        try {
            URL url = new URL(subUrl);
            HttpURLConnection conn = (HttpURLConnection) url.openConnection();
            conn.setConnectTimeout(15000);
            conn.setReadTimeout(15000);

            if (conn.getResponseCode() != 200) {
                Log.e(TAG, "Download failed: " + conn.getResponseCode() + " for " + subUrl);
                return null;
            }

            // Lưu vào Downloads/subs/
            File subDir = new File(Environment.getExternalStoragePublicDirectory(
                Environment.DIRECTORY_DOWNLOADS), "subs");
            if (!subDir.exists()) subDir.mkdirs();

            // Tên file: label + timestamp
            String ext = ".srt";
            if (subUrl.contains(".vtt")) ext = ".vtt";
            String safeName = label.replaceAll("[^a-zA-Z0-9\\-_ ]", "").trim();
            if (safeName.length() > 50) safeName = safeName.substring(0, 50);
            String fileName = safeName + "_" + System.currentTimeMillis() + ext;
            File subFile = new File(subDir, fileName);

            // Lưu file
            FileOutputStream fos = new FileOutputStream(subFile);
            byte[] buffer = new byte[8192];
            int bytesRead;
            while ((bytesRead = conn.getInputStream().read(buffer)) != -1) {
                fos.write(buffer, 0, bytesRead);
            }
            fos.close();
            conn.disconnect();

            Log.d(TAG, "Saved: " + subFile.getAbsolutePath());
            return subFile.getAbsolutePath();

        } catch (Exception e) {
            Log.e(TAG, "Download error: " + e.getMessage());
            return null;
        }
    }

    /**
     * Parse subtitleMeta (base64url JSON)
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
            Log.d(TAG, "JSON: " + json);

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
     * Mở MX Player
     */
    private void launchMxPlayer(String videoUrl, String title, ArrayList<String[]> subs) {
        try {
            Intent intent = new Intent(Intent.ACTION_VIEW);
            intent.setDataAndType(Uri.parse(videoUrl), "video/*");
            intent.putExtra(Intent.EXTRA_TITLE, title);
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);

            if (!subs.isEmpty()) {
                ArrayList<String> paths = new ArrayList<>();
                ArrayList<String> names = new ArrayList<>();
                for (String[] sub : subs) {
                    paths.add(sub[0]);
                    names.add(sub[1]);
                }
                intent.putExtra("subs", paths);
                intent.putExtra("subs.name", names);
            }

            // Thử MX Player Pro
            intent.setPackage(MX_PLAYER_PRO);
            try { startActivity(intent); return; } catch (Exception e) {}

            // MX Player thường
            intent.setPackage(MX_PLAYER);
            try { startActivity(intent); return; } catch (Exception e) {}

            // Fallback
            intent.setPackage(null);
            startActivity(intent);

        } catch (Exception e) {
            toast("Lỗi mở player: " + e.getMessage());
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
