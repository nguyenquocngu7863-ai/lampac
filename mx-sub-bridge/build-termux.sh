#!/data/data/com.termux/files/usr/bin/bash
# Build SubSense Termux Bridge APK directly in Termux.
set -eu

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR"
export ANDROID_HOME="${ANDROID_HOME:-$HOME/android-sdk}"
GRADLE_VERSION="8.5"

say() {
    printf '\n==> %s\n' "$1"
}

command_exists() {
    command -v "$1" >/dev/null 2>&1
}

say "Installing Termux build dependencies"
pkg install -y wget curl unzip openjdk-17 aapt2 >/dev/null 2>&1 || true

export PATH="$ANDROID_HOME/cmdline-tools/latest/bin:$ANDROID_HOME/platform-tools:$PATH"

if [ ! -x "$ANDROID_HOME/cmdline-tools/latest/bin/sdkmanager" ]; then
    say "Downloading Android command-line tools"
    mkdir -p "$ANDROID_HOME/cmdline-tools"
    cd "$ANDROID_HOME/cmdline-tools"

    TOOLS_URL="${ANDROID_CMDLINE_TOOLS_URL:-}"
    if [ -z "$TOOLS_URL" ]; then
        TOOLS_URL="$(curl -fsSL https://developer.android.com/studio 2>/dev/null \
            | grep -o 'https://dl.google.com/android/repository/commandlinetools-linux-[0-9]*_latest.zip' \
            | head -n 1 || true)"
    fi

    if [ -z "$TOOLS_URL" ]; then
        echo "Không tìm thấy Android command-line tools URL."
        echo "Đặt ANDROID_CMDLINE_TOOLS_URL rồi chạy lại script."
        exit 1
    fi

    rm -rf latest cmdline-tools tools.zip
    wget -q "$TOOLS_URL" -O tools.zip
    unzip -q tools.zip
    mv cmdline-tools latest
    rm -f tools.zip
fi

export PATH="$ANDROID_HOME/cmdline-tools/latest/bin:$ANDROID_HOME/platform-tools:$PATH"

say "Installing Android SDK packages"
yes | sdkmanager --licenses >/dev/null 2>&1 || true
sdkmanager "platform-tools" "platforms;android-34" "build-tools;34.0.0" >/dev/null

cd "$PROJECT_DIR"
printf 'sdk.dir=%s\n' "$ANDROID_HOME" > local.properties
printf 'android.aapt2FromMavenOverride=%s\n' "$(command -v aapt2)" >> gradle.properties

GRADLE_ROOT="$HOME/.gradle/wrapper/dists/gradle-${GRADLE_VERSION}-bin"
GRADLE_BIN=""
if [ -d "$GRADLE_ROOT" ]; then
    GRADLE_BIN="$(find "$GRADLE_ROOT" -type f -path '*/bin/gradle' | head -n 1 || true)"
fi

if [ -z "$GRADLE_BIN" ]; then
    say "Downloading Gradle $GRADLE_VERSION"
    mkdir -p "$GRADLE_ROOT"
    cd "$GRADLE_ROOT"
    wget -q "https://services.gradle.org/distributions/gradle-${GRADLE_VERSION}-bin.zip" -O gradle.zip
    unzip -q gradle.zip
    rm -f gradle.zip
    GRADLE_BIN="$(find "$GRADLE_ROOT" -type f -path '*/bin/gradle' | head -n 1)"
fi

if [ -z "$GRADLE_BIN" ] || [ ! -x "$GRADLE_BIN" ]; then
    echo "Không tìm thấy Gradle executable."
    exit 1
fi

say "Building APK"
cd "$PROJECT_DIR"
export JAVA_HOME="${JAVA_HOME:-$PREFIX/lib/jvm/java-17-openjdk}"
rm -rf build .gradle app/build
"$GRADLE_BIN" --no-daemon --max-workers=1 clean assembleDebug

APK="$PROJECT_DIR/app/build/outputs/apk/debug/app-debug.apk"
OUTPUT="$HOME/subsense-termux-bridge.apk"
if [ ! -f "$APK" ]; then
    echo "Build thất bại: không tìm thấy $APK"
    exit 1
fi

cp "$APK" "$OUTPUT"
printf '\nBuild thành công: %s\n' "$OUTPUT"
printf 'Cài đặt: termux-open "%s"\n' "$OUTPUT"
