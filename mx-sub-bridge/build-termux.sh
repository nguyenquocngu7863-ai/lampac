#!/data/data/com.termux/files/usr/bin/bash
# Build MX Sub Bridge APK trên Termux
# Dựa trên quy trình đã test thành công từ README-torrshelf-player-termux.md
set -e

echo "=== MX Sub Bridge APK Builder ==="
echo ""

# 1. Cài dependencies
echo "[1/5] Cài dependencies..."
pkg install -y wget unzip openjdk-17 aapt2 2>/dev/null || true

# 2. Setup Android SDK (nếu chưa có)
export ANDROID_HOME="$HOME/android-sdk"
if [ ! -d "$ANDROID_HOME/cmdline-tools/latest/bin" ]; then
    echo "[2/5] Tải Android SDK..."
    mkdir -p "$ANDROID_HOME/cmdline-tools"
    cd "$ANDROID_HOME/cmdline-tools"
    TOOLS_URL=$(curl -s https://developer.android.com/studio | grep -o "https://dl.google.com/android/repository/commandlinetools-linux-[0-9]*_latest.zip" | head -1)
    wget -q "$TOOLS_URL" -O tools.zip
    unzip -q tools.zip
    mv cmdline-tools latest
    rm tools.zip
else
    echo "[2/5] Android SDK đã có"
fi

export PATH="$ANDROID_HOME/cmdline-tools/latest/bin:$ANDROID_HOME/platform-tools:$PATH"

# 3. Cài SDK packages
echo "[3/5] Cài build tools..."
yes | sdkmanager --licenses > /dev/null 2>&1
sdkmanager "platform-tools" "platforms;android-34" "build-tools;34.0.0" > /dev/null 2>&1

# 4. Setup project
echo "[4/5] Setup project..."
PROJ="$HOME/mx-sub-bridge"
if [ ! -d "$PROJ" ]; then
    # Copy từ repo
    SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
    cp -r "$SCRIPT_DIR" "$PROJ"
fi

cd "$PROJ"
echo "sdk.dir=$ANDROID_HOME" > local.properties

# FIX QUAN TRỌNG: Dùng aapt2 của hệ thống (fix lỗi AAPT2 trên Termux)
echo "android.aapt2FromMavenOverride=$(which aapt2)" >> gradle.properties

# Tạo Gradle wrapper (nếu chưa có)
if [ ! -f "gradlew" ]; then
    echo "Tạo Gradle wrapper..."
    cat > gradlew << 'WRAPPER'
#!/bin/sh
# Gradle wrapper script
GRADLE_HOME="$HOME/.gradle/wrapper/dists/gradle-8.5-bin"
if [ ! -d "$GRADLE_HOME" ]; then
    echo "Downloading Gradle 8.5..."
    mkdir -p "$GRADLE_HOME"
    cd "$GRADLE_HOME"
    wget -q "https://services.gradle.org/distributions/gradle-8.5-bin.zip" -O gradle.zip
    unzip -q gradle.zip
    rm gradle.zip
    cd - > /dev/null
fi
GRADLE_BIN=$(find "$GRADLE_HOME" -name "gradle" -path "*/bin/gradle" | head -1)
exec "$GRADLE_BIN" "$@"
WRAPPER
    chmod +x gradlew
fi

# 5. Build
echo "[5/5] Build APK (có thể mất vài phút)..."
export JAVA_HOME="$PREFIX/lib/jvm/java-17-openjdk"
rm -rf build .gradle app/build

./gradlew --no-daemon --max-workers=1 clean assembleDebug

APK="app/build/outputs/apk/debug/app-debug.apk"
if [ -f "$APK" ]; then
    cp "$APK" "$HOME/mx-sub-bridge.apk"
    echo ""
    echo "=== BUILD THÀNH CÔNG! ==="
    echo "APK: $HOME/mx-sub-bridge.apk"
    echo "Size: $(du -h "$HOME/mx-sub-bridge.apk" | cut -f1)"
    echo ""
    echo "Cài đặt:"
    echo "  termux-open $HOME/mx-sub-bridge.apk"
else
    echo "=== BUILD THẤT BẠI ==="
    exit 1
fi
