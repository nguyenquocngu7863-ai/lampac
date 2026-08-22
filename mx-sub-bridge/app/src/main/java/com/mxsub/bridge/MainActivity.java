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
 * Sub Downloader v4
 * Chỉ tải sub về Downloads/subs/ - không mở player
 */
public class MainActivity extends Activity {
    private static final String TAG = "SubDownloader";

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
        String subtitleMeta = videoUri.getQueryParameter("subtitleMeta");

        if (subtitleMeta == null || subtitleMeta.isEmpty()) {
            toast("Không tìm thấy phụ đề");
            finish();
            return;
        }

        ArrayList<String[]> subs = parseSubtitleMeta(subtitleMeta);

        if (subs.isEmpty()) {
            toast("Không parse được phụ đề");
            finish();
            return;
        }

        toast("Đang tải " + subs.size() + " phụ đề...");

        // Tải sub trong background
        new Thread(() -> {
            int downloaded = 0;
            for (String[] sub : subs) {
                if (downloadSubtitle(sub[0], sub[1])) {
                    downloaded++;
                }
            }

            final int count = downloaded;
            runOnUiThread(() -> {
                toast("Đã tải " + count + "/" + subs.size() + " phụ đề vào Downloads/subs/");
                finish();
            });
        }).start();
    }

    private void toast(String msg) {
        android.widget.Toast.makeText(this, msg, android.widget.Toast.LENGTH_LONG).show();
    }

    private boolean downloadSubtitle(String subUrl, String label) {
        try {
            URL url = new URL(subUrl);
            HttpURLConnection conn = (HttpURLConnection) url.openConnection();
            conn.setConnectTimeout(15000);
            conn.setReadTimeout(15000);

            if (conn.getResponseCode() != 200) {
                Log.e(TAG, "Failed: " + conn.getResponseCode());
                return false;
            }

            File subDir = new File(Environment.getExternalStoragePublicDirectory(
                Environment.DIRECTORY_DOWNLOADS), "subs");
            if (!subDir.exists()) subDir.mkdirs();

            String ext = subUrl.contains(".vtt") ? ".vtt" : ".srt";
            String safeName = label.replaceAll("[^a-zA-Z0-9\\-_ ]", "").trim();
            if (safeName.length() > 50) safeName = safeName.substring(0, 50);
            File subFile = new File(subDir, safeName + ext);

            FileOutputStream fos = new FileOutputStream(subFile);
            byte[] buf = new byte[8192];
            int n;
            while ((n = conn.getInputStream().read(buf)) != -1) {
                fos.write(buf, 0, n);
            }
            fos.close();
            conn.disconnect();

            Log.d(TAG, "Saved: " + subFile.getAbsolutePath());
            return true;

        } catch (Exception e) {
            Log.e(TAG, "Error: " + e.getMessage());
            return false;
        }
    }

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
}
