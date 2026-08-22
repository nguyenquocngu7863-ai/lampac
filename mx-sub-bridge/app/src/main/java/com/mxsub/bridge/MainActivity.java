package com.mxsub.bridge;

import android.app.Activity;
import android.content.ComponentName;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Environment;
import android.util.Base64;
import android.util.Log;
import android.widget.Toast;

import java.nio.charset.StandardCharsets;

/**
 * SubSense Termux Bridge.
 *
 * This small activity receives a custom mxsub:// URI from the Lampa plugin and
 * starts Termux's RUN_COMMAND service. Direct SRT files use termux-download;
 * VTT/ZIP files are normalized by a short bash/curl script in Termux.
 */
public class MainActivity extends Activity {
    private static final String TAG = "SubSenseBridge";

    private static final String BRIDGE_SCHEME = "mxsub";
    private static final String BRIDGE_HOST = "download";

    private static final String TERMUX_PACKAGE = "com.termux";
    private static final String TERMUX_RUN_COMMAND_SERVICE = "com.termux.app.RunCommandService";
    private static final String TERMUX_RUN_COMMAND_ACTION = "com.termux.RUN_COMMAND";
    private static final String TERMUX_RUN_COMMAND_PERMISSION = "com.termux.permission.RUN_COMMAND";
    private static final String TERMUX_PREFIX = "/data/data/com.termux/files/usr";
    private static final String TERMUX_HOME = "/data/data/com.termux/files/home";

    private static final String EXTRA_COMMAND_PATH = "com.termux.RUN_COMMAND_PATH";
    private static final String EXTRA_ARGUMENTS = "com.termux.RUN_COMMAND_ARGUMENTS";
    private static final String EXTRA_WORKDIR = "com.termux.RUN_COMMAND_WORKDIR";
    private static final String EXTRA_BACKGROUND = "com.termux.RUN_COMMAND_BACKGROUND";
    private static final String EXTRA_RUNNER = "com.termux.RUN_COMMAND_RUNNER";
    private static final String EXTRA_SESSION_ACTION = "com.termux.RUN_COMMAND_SESSION_ACTION";
    private static final String EXTRA_COMMAND_LABEL = "com.termux.RUN_COMMAND_COMMAND_LABEL";
    private static final String EXTRA_COMMAND_DESCRIPTION = "com.termux.RUN_COMMAND_COMMAND_DESCRIPTION";

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        handleDownloadIntent(getIntent());
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        handleDownloadIntent(intent);
    }

    private void handleDownloadIntent(Intent intent) {
        Uri data = intent == null ? null : intent.getData();

        if (data == null || !BRIDGE_SCHEME.equalsIgnoreCase(data.getScheme())
                || !BRIDGE_HOST.equalsIgnoreCase(data.getHost())) {
            toast("SubSense Bridge: thiếu dữ liệu tải xuống");
            finish();
            return;
        }

        String url = data.getQueryParameter("url");
        String requestedName = data.getQueryParameter("filename");
        String format = normalizeFormat(data.getQueryParameter("format"));
        String title = data.getQueryParameter("title");

        if (!isHttpUrl(url)) {
            toast("SubSense Bridge: URL phụ đề không hợp lệ");
            finish();
            return;
        }

        if (!hasRunCommandPermission()) {
            toast("Hãy cấp quyền 'Run commands in Termux' cho SubSense Bridge");
            finish();
            return;
        }

        String filename = safeFilename(requestedName, format);
        String outputPath = new java.io.File(
                Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS),
                filename
        ).getAbsolutePath();
        String displayTitle = safeTitle(title, filename);

        boolean started;
        if ("srt".equals(format)) {
            started = startTermuxDownload(url, outputPath, displayTitle, filename);
        } else if ("rar".equals(format)) {
            // RAR is not converted, but can still be handed to Termux's download manager.
            started = startTermuxDownload(url, outputPath, displayTitle, filename);
        } else {
            started = startNormalizedDownload(url, outputPath, format, displayTitle);
        }

        if (started) {
            toast("Đã gửi tải phụ đề qua Termux: " + filename);
        } else {
            toast("Không thể gọi Termux. Kiểm tra Termux và quyền RUN_COMMAND");
        }

        finish();
    }

    private boolean hasRunCommandPermission() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.M) return true;
        return checkSelfPermission(TERMUX_RUN_COMMAND_PERMISSION)
                == PackageManager.PERMISSION_GRANTED;
    }

    private boolean startTermuxDownload(String url, String outputPath, String title, String filename) {
        String[] arguments = new String[] {
                "-t", title,
                "-d", "SubSense subtitle: " + filename,
                "-p", outputPath,
                url
        };

        return startTermuxCommand(
                TERMUX_PREFIX + "/bin/termux-download",
                arguments,
                "SubSense subtitle download",
                "Download a subtitle with Termux:API"
        );
    }

    private boolean startNormalizedDownload(String url, String outputPath, String format, String title) {
        String script = buildNormalizeScript(url, outputPath, format, title);

        return startTermuxCommand(
                TERMUX_PREFIX + "/bin/bash",
                new String[] { "-c", script },
                "SubSense subtitle download",
                "Download and normalize a SubSense subtitle"
        );
    }

    private boolean startTermuxCommand(
            String commandPath,
            String[] arguments,
            String label,
            String description
    ) {
        Intent command = new Intent(TERMUX_RUN_COMMAND_ACTION);
        command.setComponent(new ComponentName(TERMUX_PACKAGE, TERMUX_RUN_COMMAND_SERVICE));
        command.putExtra(EXTRA_COMMAND_PATH, commandPath);
        command.putExtra(EXTRA_ARGUMENTS, arguments);
        command.putExtra(EXTRA_WORKDIR, TERMUX_HOME);
        command.putExtra(EXTRA_BACKGROUND, true);
        command.putExtra(EXTRA_RUNNER, "app-shell");
        command.putExtra(EXTRA_SESSION_ACTION, "0");
        command.putExtra(EXTRA_COMMAND_LABEL, label);
        command.putExtra(EXTRA_COMMAND_DESCRIPTION, description);

        try {
            // The bridge activity is in the foreground, so the normal service API
            // is the most compatible option across Termux versions.
            startService(command);
            return true;
        } catch (SecurityException error) {
            Log.e(TAG, "Termux RUN_COMMAND permission denied", error);
        } catch (IllegalStateException error) {
            Log.e(TAG, "Termux service could not be started", error);
        } catch (Exception error) {
            Log.e(TAG, "Termux command failed", error);
        }

        return false;
    }

    /**
     * Build a shell command without interpolating raw user data. URLs, paths and
     * titles are base64 encoded before they enter the shell script.
     */
    private String buildNormalizeScript(String url, String outputPath, String format, String title) {
        String encodedUrl = base64(url);
        String encodedOutput = base64(outputPath);
        String encodedFormat = base64(format);
        String encodedTitle = base64(title);

        return "set -eu\n"
                + "URL=$(printf '%s' '" + encodedUrl + "' | base64 -d)\n"
                + "OUT=$(printf '%s' '" + encodedOutput + "' | base64 -d)\n"
                + "MODE=$(printf '%s' '" + encodedFormat + "' | base64 -d)\n"
                + "TITLE=$(printf '%s' '" + encodedTitle + "' | base64 -d)\n"
                + "TMP=\"$OUT.part.$$\"\n"
                + "trap 'rm -f \"$TMP\"' EXIT\n"
                + "mkdir -p \"$(dirname \"$OUT\")\"\n"
                + "curl --fail --location --silent --show-error --connect-timeout 15 --max-time 120 \"$URL\" -o \"$TMP\"\n"
                + "convert_vtt() {\n"
                + "  awk 'BEGIN { cue=0; active=0 } { sub(/\\r$/, \"\"); if ($0 == \"WEBVTT\" && NR <= 2) next; if ($0 ~ /-->/) { cue++; gsub(/\\./, \",\", $0); print cue; print $0; active=1; next } if ($0 == \"\") { if (active) print \"\"; active=0; next } if (active) print $0 }'\n"
                + "}\n"
                + "if [ \"$MODE\" = \"zip\" ]; then\n"
                + "  ENTRY=$(unzip -Z1 \"$TMP\" 2>/dev/null | grep -iE '\\.(srt|vtt)$' | head -n 1 || true)\n"
                + "  [ -n \"$ENTRY\" ] || { echo 'No SRT/VTT subtitle in archive' >&2; exit 1; }\n"
                + "  case \"$ENTRY\" in\n"
                + "    *.vtt|*.VTT) unzip -p \"$TMP\" \"$ENTRY\" | convert_vtt > \"$OUT\" ;;\n"
                + "    *) unzip -p \"$TMP\" \"$ENTRY\" > \"$OUT\" ;;\n"
                + "  esac\n"
                + "elif [ \"$MODE\" = \"vtt\" ]; then\n"
                + "  convert_vtt < \"$TMP\" > \"$OUT\"\n"
                + "else\n"
                + "  if head -c 256 \"$TMP\" | grep -qi 'WEBVTT'; then convert_vtt < \"$TMP\" > \"$OUT\"; else mv -f \"$TMP\" \"$OUT\"; fi\n"
                + "fi\n"
                + "rm -f \"$TMP\"\n"
                + "if command -v termux-toast >/dev/null 2>&1; then termux-toast -s \"SubSense: $TITLE\" >/dev/null 2>&1 || true; fi\n";
    }

    private String base64(String value) {
        return Base64.encodeToString(value.getBytes(StandardCharsets.UTF_8), Base64.NO_WRAP);
    }

    private boolean isHttpUrl(String value) {
        if (value == null) return false;
        Uri uri = Uri.parse(value);
        String scheme = uri.getScheme();
        return ("http".equalsIgnoreCase(scheme) || "https".equalsIgnoreCase(scheme))
                && uri.getHost() != null;
    }

    private String normalizeFormat(String value) {
        String format = value == null ? "unknown" : value.trim().toLowerCase();
        if (format.startsWith(".")) format = format.substring(1);
        if ("srt".equals(format) || "vtt".equals(format)
                || "zip".equals(format) || "rar".equals(format)) return format;
        return "unknown";
    }

    private String safeFilename(String value, String format) {
        String filename = value == null ? "subtitle" : value.trim();
        filename = filename.replaceAll("[\\\\/:*?\"<>|\\p{Cntrl}]", " ");
        filename = filename.replaceAll("\\s+", " ").replaceAll("[. ]+$", "").trim();
        if (filename.isEmpty()) filename = "subtitle";

        String extension;
        if ("rar".equals(format)) extension = ".rar";
        else extension = ".srt";

        if (!filename.toLowerCase().endsWith(extension)) {
            filename += extension;
        }

        int maxLength = 120;
        if (filename.length() > maxLength) {
            String stem = filename.substring(0, filename.length() - extension.length());
            stem = stem.substring(0, Math.max(1, maxLength - extension.length()))
                    .replaceAll("[. ]+$", "");
            filename = stem + extension;
        }

        return filename;
    }

    private String safeTitle(String value, String fallback) {
        String title = value == null ? fallback : value.trim();
        if (title.isEmpty()) title = fallback;
        if (title.length() > 120) title = title.substring(0, 120);
        return title;
    }

    private void toast(String message) {
        Toast.makeText(this, message, Toast.LENGTH_LONG).show();
    }
}
