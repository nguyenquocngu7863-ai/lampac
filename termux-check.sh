#!/data/data/com.termux/files/usr/bin/bash
# csc shim — kiểm tra compile nhanh module Lampac (native, KHÔNG proot, KHÔNG MSBuild, KHÔNG bin/obj)
#
#   ~/lampac-native/csc/check.sh ~/lampac-clean/Modules/Adult/<Module>
#   SHARED_DLL=/path/Shared.dll ~/lampac-native/csc/check.sh <module-dir>
#   REFS_EXTRA=/duong/dan/them1:/duong/dan/them2 ~/lampac-native/csc/check.sh <module-dir>
#
# Bắt lỗi CS trong *.cs trước khi deploy => tránh "restart xong mới lòi error CS / app không lên".
# Không đọc .csproj: lỗi csproj (vd CS5001 thiếu OutputType=Library) phải dùng proot build.
#
# Ref: Ref pack NETCore/AspNetCore + Shared.dll của RUNTIME + mọi *.dll trong
# ~/lampac-run/runtimes/references (Playwright, Bcl.AsyncInterfaces, HtmlAgilityPack, Jint…).
# Nhờ vậy module dùng thư viện ngoài không còn báo oan CS0234/CS0012.
# Tên trùng với ref pack bị bỏ qua để tránh CS1704 (assembly cùng simple name).
set -uo pipefail

dir="${1:-}"
if [ -z "$dir" ] || [ ! -d "$dir" ]; then
    echo "usage: check.sh <module-dir>" >&2
    exit 2
fi
dir="$(cd "$dir" && pwd)"

HOST="$HOME/lampac-native/dotnet-host"
CSC="$HOME/lampac-native/csc/csc"
SHARED="${SHARED_DLL:-$HOME/lampac-run/Shared.dll}"
REFS_DIR="$HOME/lampac-run/runtimes/references"

[ -x "$CSC" ]    || { echo "check.sh: không thấy csc shim: $CSC" >&2; exit 2; }
[ -f "$SHARED" ] || { echo "check.sh: không thấy Shared.dll: $SHARED (đặt SHARED_DLL=...)" >&2; exit 2; }

NETREF="$(ls -d "$HOST"/packs/Microsoft.NETCore.App.Ref/*/ref/net10.0 2>/dev/null | sort -V | tail -1)"
ASPREF="$(ls -d "$HOST"/packs/Microsoft.AspNetCore.App.Ref/*/ref/net10.0 2>/dev/null | sort -V | tail -1)"
if [ -z "$NETREF" ] || [ -z "$ASPREF" ]; then
    echo "check.sh: không thấy ref pack net10.0 trong $HOST/packs" >&2
    exit 2
fi

srcs=()
while IFS= read -r f; do srcs+=("$f"); done < <(find "$dir" -maxdepth 1 -name '*.cs' | sort)
if [ ${#srcs[@]} -eq 0 ]; then
    echo "check.sh: không có file .cs trong $dir" >&2
    exit 2
fi

refs=()
declare -A seen=()
for f in "$NETREF"/*.dll "$ASPREF"/*.dll; do
    refs+=("-r:$f")
    seen["$(basename "$f")"]=1
done

# Thư viện bổ sung: runtime references (và REFS_EXTRA nếu có), bỏ qua tên đã có trong ref pack
extra_dirs=("$REFS_DIR")
if [ -n "${REFS_EXTRA:-}" ]; then
    IFS=':' read -r -a _more <<< "$REFS_EXTRA"
    for d in "${_more[@]}"; do [ -n "$d" ] && extra_dirs+=("$d"); done
fi
for d in "${extra_dirs[@]}"; do
    [ -d "$d" ] || continue
    while IFS= read -r f; do
        b="$(basename "$f")"
        [ -n "${seen[$b]:-}" ] && continue
        seen["$b"]=1
        refs+=("-r:$f")
    done < <(find "$d" -maxdepth 1 -name '*.dll' | sort)
done

name="$(basename "$dir")"
outdir="${TMPDIR:-$HOME/.cache}"
out="$outdir/csc-check-$name.dll"
log="$out.log"

start=$(date +%s%N)
env -u LD_PRELOAD -u LD_LIBRARY_PATH "$CSC" \
    -nologo -target:library -nullable:enable -langversion:latest \
    "${refs[@]}" -r:"$SHARED" "${srcs[@]}" -out:"$out" > "$log" 2>&1
rc=$?
ms=$(( ($(date +%s%N) - start) / 1000000 ))

errs=$(grep -c 'error CS' "$log" 2>/dev/null)
warns=$(grep -c 'warning CS' "$log" 2>/dev/null)
errs=${errs:-0}; warns=${warns:-0}

if [ "$errs" -gt 0 ]; then
    grep -E 'error CS' "$log" | head -20
elif [ "$rc" -ne 0 ]; then
    tail -5 "$log"
fi
echo "---"
echo "module: $name  files: ${#srcs[@]}  errors: $errs  warnings: $warns  time: ${ms}ms"
if [ "$errs" -eq 0 ] && [ "$rc" -eq 0 ]; then
    echo "OK"
    exit 0
fi
echo "FAILED — log: $log"
exit 1
