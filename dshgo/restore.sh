#!/usr/bin/bash
# Khoi phuc moi truong dsh-go cho Lampac (Ubuntu 24.04 glibc, KHONG phai Termux).
# Chay sau khi "Delete and reset" hoac cai lai app.
set -euo pipefail

echo "== 1/6 apt packages =="
apt-get update -qq
apt-get install -y --no-install-recommends \
  ffmpeg curl patchelf openssh-client gh \
  libnspr4 libnss3 libatk1.0-0t64 libatk-bridge2.0-0t64 libcups2t64 \
  libdrm2 libxkbcommon0 libxcomposite1 libxdamage1 libxfixes3 libxrandr2 \
  libgbm1 libasound2t64 libpango-1.0-0 libcairo2 libatspi2.0-0t64 \
  gstreamer1.0-tools gstreamer1.0-plugins-base gstreamer1.0-plugins-good \
  gstreamer1.0-plugins-bad gstreamer1.0-plugins-ugly gstreamer1.0-libav \
  gstreamer1.0-plugins-base-apps libgnutls30

echo "== 2/6 curl_cffi (bypass Cloudflare TLS fingerprint) =="
pip install --index-url https://mirrors.aliyun.com/pypi/simple/ curl_cffi

echo "== 3/6 symlink node/npm vao /usr/bin (ModInit Po85 doi hoi) =="
ln -sf /usr/local/bin/node /usr/bin/node
ln -sf /usr/local/bin/npm  /usr/bin/npm

echo "== 4/6 curl shim (curl_cffi impersonate) =="
NATIVE=/root/lampac/lampac-native
mkdir -p "$NATIVE/bin"
cp "$(dirname "$0")/curl-shim" "$NATIVE/bin/curl"
chmod +x "$NATIVE/bin/curl"

echo "== 5/6 Po85 uhd resolver deps =="
MOD=/root/lampac/lampac-run/module/Adult/Po85/uhd
if [ -d "$MOD" ]; then
  cd "$MOD" && npm i playwright-core --no-audit --no-fund
else
  echo "  (bo qua: chua co $MOD)"
fi

echo "== 6/6 Chrome (neu chua co) =="
if [ ! -x /root/lampac-test/chrome/chrome ]; then
  echo "  CAN: copy chrome da patch vao /root/lampac-test/chrome/"
  echo "  Hoac chay: bash dshgo/setup-chrome.sh"
fi

echo "XONG. Kiem tra: lampac-native/run-lampac.sh"
