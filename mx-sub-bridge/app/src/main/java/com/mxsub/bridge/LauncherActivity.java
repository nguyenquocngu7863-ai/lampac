package com.mxsub.bridge;

import android.app.Activity;
import android.os.Bundle;
import android.widget.LinearLayout;
import android.widget.TextView;

/**
 * A small setup screen. Downloads are started by the Lampa plugin through the
 * custom mxsub:// URI; this activity is only useful for displaying setup notes.
 */
public class LauncherActivity extends Activity {
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        LinearLayout layout = new LinearLayout(this);
        layout.setOrientation(LinearLayout.VERTICAL);
        layout.setPadding(48, 48, 48, 48);

        TextView title = new TextView(this);
        title.setText("SubSense Termux Bridge");
        title.setTextSize(24);
        title.setPadding(0, 0, 0, 20);
        layout.addView(title);

        TextView description = new TextView(this);
        description.setText(
                "Cài đặt một lần:\n\n"
                        + "1. Cài Termux và Termux:API từ cùng một nguồn.\n"
                        + "2. Trong Termux chạy: termux-setup-storage\n"
                        + "3. Cài công cụ: pkg install termux-api curl unzip\n"
                        + "4. Bật allow-external-apps=true trong ~/.termux/termux.properties.\n"
                        + "5. Vào App info của ứng dụng này → Permissions → bật Run commands in Termux.\n\n"
                        + "Sau đó mở plugin SubSense download trong Lampa và bấm Tải phụ đề.\n"
                        + "File được lưu vào thư mục Downloads của Android."
        );
        description.setTextSize(15);
        layout.addView(description);

        setContentView(layout);
    }
}
