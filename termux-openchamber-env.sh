#!/data/data/com.termux/files/usr/bin/bash
# oc-env-fix — vá môi trường Termux sau khi update OpenChamber (`npm i -g @openchamber/web`).
#
# Vì sao cần: bin `openchamber` là symlink vào build/bin/cli.js, shebang npm ghi là
# `#!/usr/bin/env node`. Termux KHÔNG có /usr → báo "bad interpreter". Mỗi lần npm
# install ghi đè cli.js nên phải vá lại.
#
# Chạy: oc-env-fix   (idempotent, exit 0 = env dùng được)
set -u

PKG="${PREFIX:-/data/data/com.termux/files/usr}/lib/node_modules/@openchamber/web"
CLI="$PKG/build/bin/cli.js"
ENV_BIN="${PREFIX:-/data/data/com.termux/files/usr}/bin/env"
rc=0

if [ ! -f "$CLI" ]; then
  echo "FAIL: không thấy $CLI (đã cài @openchamber/web chưa?)" >&2
  exit 2
fi

# 1. shebang
if head -1 "$CLI" | grep -qx '#!/usr/bin/env node'; then
  sed -i "1s|^#!/usr/bin/env node\$|#!$ENV_BIN node|" "$CLI"
  echo "fixed: shebang cli.js → #!$ENV_BIN node"
elif head -1 "$CLI" | grep -q "^#!$ENV_BIN node"; then
  echo "ok: shebang đã đúng"
else
  echo "WARN: shebang lạ: $(head -1 "$CLI")" >&2
  rc=1
fi

# 2. CLI chạy được
ver=$(openchamber --version 2>&1 | head -1)
case "$ver" in
  [0-9]*) echo "ok: openchamber $ver" ;;
  *) echo "FAIL: openchamber --version → $ver" >&2; rc=1 ;;
esac

# 3. module native mà OpenChamber cần (node-pty cho terminal panel)
if node -e "require('$PKG/node_modules/node-pty')" 2>/dev/null; then
  echo "ok: node-pty load"
else
  echo "FAIL: node-pty không load → terminal panel sẽ hỏng (cần build lại cho Android)" >&2
  rc=1
fi

# 4. ripgrep: package optional @vscode/ripgrep-android-arm64 không tồn tại trên npm,
#    nhưng server tự fallback sang `rg` trên PATH — chỉ cần rg có mặt.
if command -v rg >/dev/null 2>&1; then
  echo "ok: rg trên PATH ($(rg --version | head -1))"
else
  echo "WARN: không có rg trên PATH → file search trong UI sẽ trống (pkg install ripgrep)" >&2
  rc=1
fi

echo
echo "xong (rc=$rc). Nhớ restart để chạy bản mới: openchamber restart"
exit "$rc"
