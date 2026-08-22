package com.mxsub.bridge;

import android.app.Activity;
import android.os.Bundle;
import android.widget.LinearLayout;
import android.widget.TextView;
import android.widget.Button;
import android.content.Intent;
import android.net.Uri;

/**
 * Launcher activity - hiện trong danh sách app
 * Dùng để test hoặc mở video trực tiếp
 */
public class LauncherActivity extends Activity {
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        LinearLayout layout = new LinearLayout(this);
        layout.setOrientation(LinearLayout.VERTICAL);
        layout.setPadding(48, 48, 48, 48);

        // Title
        TextView title = new TextView(this);
        title.setText("MX Sub Bridge");
        title.setTextSize(24);
        title.setPadding(0, 0, 0, 16);
        layout.addView(title);

        // Description
        TextView desc = new TextView(this);
        desc.setText("Bridge app: Lampa → SubSense → MX Player\n\n" +
                "Cách dùng:\n" +
                "1. Mở Lampa\n" +
                "2. Phát video\n" +
                "3. Chọn 'Mở bằng...' → MX Sub Bridge\n" +
                "4. App sẽ tự fetch phụ đề và mở MX Player\n\n" +
                "Hoặc test bằng cách mở link video trực tiếp:");
        desc.setTextSize(14);
        desc.setPadding(0, 0, 0, 24);
        layout.addView(desc);

        // Test button
        Button testBtn = new Button(this);
        testBtn.setText("Test với link mẫu");
        testBtn.setOnClickListener(v -> {
            Intent intent = new Intent(Intent.ACTION_VIEW);
            intent.setDataAndType(
                Uri.parse("http://example.com/test.mkv"),
                "video/*"
            );
            intent.setPackage(getPackageName());
            try {
                startActivity(intent);
            } catch (Exception e) {
                android.widget.Toast.makeText(this, "Error: " + e.getMessage(), android.widget.Toast.LENGTH_LONG).show();
            }
        });
        layout.addView(testBtn);

        // Info
        TextView info = new TextView(this);
        info.setText("\n\nPackage: com.mxsub.bridge\nVersion: 1.0.0\n\n" +
                "App này không có giao diện riêng.\n" +
                "Chỉ hoạt động khi nhận video intent từ Lampa.");
        info.setTextSize(12);
        info.setPadding(0, 32, 0, 0);
        layout.addView(info);

        setContentView(layout);
    }
}
