package com.mxsub.bridge;

import android.app.Activity;
import android.os.Bundle;
import android.widget.LinearLayout;
import android.widget.TextView;

/**
 * Launcher - chỉ hiện thông tin
 */
public class LauncherActivity extends Activity {
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        LinearLayout layout = new LinearLayout(this);
        layout.setOrientation(LinearLayout.VERTICAL);
        layout.setPadding(48, 48, 48, 48);

        TextView title = new TextView(this);
        title.setText("Sub Downloader");
        title.setTextSize(24);
        title.setPadding(0, 0, 0, 16);
        layout.addView(title);

        TextView desc = new TextView(this);
        desc.setText("App tự động tải phụ đề từ SubSense về máy.\n\n" +
                "Cách dùng:\n" +
                "1. Cài plugin SubSense trong Lampa\n" +
                "2. Phát video → chọn 'Mở bằng Sub Downloader'\n" +
                "3. Phụ đề sẽ được tải về Downloads/subs/\n\n" +
                "Package: com.mxsub.bridge");
        desc.setTextSize(14);
        layout.addView(desc);

        setContentView(layout);
    }
}
