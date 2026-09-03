#!/data/data/com.termux/files/usr/bin/bash
# Build the ARM64 hdrtonemap GStreamer plugin inside the Ubuntu proot used by Lampac.
# This is optional: HDR -> SDR is CPU/GPU intensive and the build needs about 1 GB
# of temporary disk space plus a few minutes on a phone.
set -euo pipefail

LAMPAC_ROOT="${LAMPAC_ROOT:-/root/lampac}"
WORK_ROOT="${GSTREAMER_HDR_WORK_ROOT:-/root/gst-hdrtonemap-build}"
FFMPEG_VERSION="${FFMPEG_VERSION:-8.0.3}"
ZIMG_VERSION="${ZIMG_VERSION:-3.0.6}"
JOBS="${JOBS:-2}"
SOURCE_BASE="${LAMPAC_CUSTOM_SOURCE_BASE:-https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/arena/01a06710-lampac}"

printf '\n==> Installing GStreamer HDR build dependencies in Ubuntu\n'

proot-distro login ubuntu -- bash -s -- "$LAMPAC_ROOT" "$WORK_ROOT" "$FFMPEG_VERSION" "$ZIMG_VERSION" "$JOBS" "$SOURCE_BASE" <<'UBUNTU'
set -euo pipefail

LAMPAC_ROOT="$1"
WORK_ROOT="$2"
FFMPEG_VERSION="$3"
ZIMG_VERSION="$4"
JOBS="$5"
SOURCE_BASE="$6"
NATIVE_ROOT="$LAMPAC_ROOT/module/GStreamer/native"

GST_SCANNER=$(find /usr/lib /usr/libexec /usr/local/lib /usr/local/libexec \
    -type f -name gst-plugin-scanner -perm -111 2>/dev/null | head -n 1 || true)
if [ -n "$GST_SCANNER" ]; then
    export GST_PLUGIN_SCANNER="$GST_SCANNER"
    export GST_PLUGIN_SCANNER_1_0="$GST_SCANNER"
fi

# Release archives do not always contain the native build helpers. Keep the
# small, auditable native source tree synchronized with the selected
# repository. The version stamp also makes an existing install pick up a
# native plugin fix after a subsequent run without downloading on every run.
NATIVE_SOURCE_VERSION="3"
NATIVE_SOURCE_STAMP="$WORK_ROOT/.native-source-version"
if [ ! -x "$NATIVE_ROOT/build-linux.sh" ] ||
   [ ! -f "$NATIVE_SOURCE_STAMP" ] ||
   [ "$(cat "$NATIVE_SOURCE_STAMP" 2>/dev/null || true)" != "$NATIVE_SOURCE_VERSION" ]; then
    echo "Synchronizing native GStreamer HDR sources..."
    mkdir -p "$NATIVE_ROOT/src" "$WORK_ROOT"
    for file in build-linux.sh meson.build meson_options.txt; do
        curl -fL --retry 3 \
            "$SOURCE_BASE/Modules/GStreamer/native/$file" \
            -o "$NATIVE_ROOT/$file"
    done
    curl -fL --retry 3 \
        "$SOURCE_BASE/Modules/GStreamer/native/src/gsthdrtonemap.c" \
        -o "$NATIVE_ROOT/src/gsthdrtonemap.c"
    chmod +x "$NATIVE_ROOT/build-linux.sh"
    printf '%s\n' "$NATIVE_SOURCE_VERSION" > "$NATIVE_SOURCE_STAMP"
fi

arch=$(dpkg --print-architecture)
case "$arch" in
    arm64|amd64) ;;
    *)
        echo "Kiến trúc $arch chưa được hỗ trợ bởi script này." >&2
        exit 1
        ;;
esac

export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq \
    autoconf \
    automake \
    build-essential \
    ca-certificates \
    curl \
    gstreamer1.0-tools \
    libgstreamer1.0-dev \
    libgstreamer-plugins-base1.0-dev \
    libgstreamer-plugins-base1.0-0 \
    libgstreamer1.0-0 \
    libtool \
    meson \
    nasm \
    ninja-build \
    ocl-icd-libopencl1 \
    ocl-icd-opencl-dev \
    pkg-config \
    unzip \
    xz-utils \
    yasm

mkdir -p "$WORK_ROOT/sources" "$WORK_ROOT/deps" "$WORK_ROOT/build"

FFMPEG_TARBALL="$WORK_ROOT/sources/ffmpeg-${FFMPEG_VERSION}.tar.xz"
ZIMG_TARBALL="$WORK_ROOT/sources/zimg-${ZIMG_VERSION}.tar.gz"

if [ ! -f "$FFMPEG_TARBALL" ]; then
    curl -fL --retry 3 \
        "https://ffmpeg.org/releases/ffmpeg-${FFMPEG_VERSION}.tar.xz" \
        -o "$FFMPEG_TARBALL"
fi

if [ ! -f "$ZIMG_TARBALL" ]; then
    curl -fL --retry 3 \
        "https://github.com/sekrit-twc/zimg/archive/refs/tags/release-${ZIMG_VERSION}.tar.gz" \
        -o "$ZIMG_TARBALL"
fi

FFMPEG_SOURCE="$WORK_ROOT/sources/ffmpeg-${FFMPEG_VERSION}"
ZIMG_SOURCE="$WORK_ROOT/sources/zimg-release-${ZIMG_VERSION}"

if [ ! -d "$FFMPEG_SOURCE" ]; then
    tar -xf "$FFMPEG_TARBALL" -C "$WORK_ROOT/sources"
fi

if [ ! -d "$ZIMG_SOURCE" ]; then
    tar -xf "$ZIMG_TARBALL" -C "$WORK_ROOT/sources"
fi

# Build against the GStreamer module copied into the running Lampac tree.
cd "$NATIVE_ROOT"
ZIMG_SOURCE="$ZIMG_SOURCE" \
FFMPEG_SOURCE="$FFMPEG_SOURCE" \
DEPS_PREFIX="$WORK_ROOT/deps" \
BUILD_DIR="$WORK_ROOT/build" \
JOBS="$JOBS" \
bash ./build-linux.sh

PLUGIN="$NATIVE_ROOT/runtimes/linux-${arch}/native/gstreamer-1.0/libgsthdrtonemap.so"
if [ ! -f "$PLUGIN" ]; then
    echo "Không tạo được $PLUGIN" >&2
    exit 1
fi

printf '\nHDR tone-mapping plugin đã build: %s\n' "$PLUGIN"
GST_PLUGIN_PATH="$(dirname "$PLUGIN")${GST_PLUGIN_PATH:+:$GST_PLUGIN_PATH}" \
    gst-inspect-1.0 hdrtonemap | sed -n '1,35p'
UBUNTU

printf '\n==> Build xong. Cấu hình trong /root/lampac/init.conf:\n'
printf '"gst": { "enable": true, "hdr_to_sdr": true, "useGpu": true, "hardwareAcceleration": false, "x264Ultrafast": true, "segment_seconds": 2, "segment_buffer": 4 }\n'
printf 'useGpu chỉ thử OpenCL; nếu không có driver thì tự động dùng CPU.\n'
printf 'Sau đó restart: lampac stop && lampac start\n'
