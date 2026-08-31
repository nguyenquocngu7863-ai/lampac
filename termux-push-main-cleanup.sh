#!/data/data/com.termux/files/usr/bin/bash
# Lampac: gộp toàn bộ công việc lên `main`, rồi xoá các nhánh arena/* thừa.
#
# Cách chạy ở Termux:
#   pkg update -y && pkg install -y git gh
#   git clone --depth 1 --branch main https://github.com/nguyenquocngu7863-ai/lampac.git
#   (script nằm trên nhánh nào thì clone đúng nhánh đó; sau khi gộp vào main thì dùng main)
#   cd lampac
#   DRY_RUN=1 bash termux-push-main-cleanup.sh     # xem trước, không đụng gì
#   bash termux-push-main-cleanup.sh               # làm thật (an toàn: backup + merge main)
#   FAST=1 bash termux-push-main-cleanup.sh        # ghi đè main bằng bản này + xoá nhánh cũ
#
# Cờ (đặt trước lệnh):
#   DRY_RUN=1          chỉ in ra những gì sẽ làm
#   ONLY_MAIN=1        xoá luôn cả nhánh hiện tại, GitHub chỉ còn main
#                      (LƯU Ý: làm vậy là xoá nhánh mà phiên Arena đang theo dõi)
#   SKIP_MAIN_MERGE=1  không merge origin/main -> mất lampa-en.js, buộc force-push
#   DO_BACKUP=0        bỏ bước sao lưu (không nên)
#   FAST=1             = DO_BACKUP=0 + SKIP_MAIN_MERGE=1 + FORCE=1: ghi đè main bằng
#                      bản hiện tại và xoá sạch nhánh cũ, không vòng vo (khi main
#                      chỉ để trưng và mấy nhánh cũ bỏ đi)
set -uo pipefail

REPO="nguyenquocngu7863-ai/lampac"
ONLY_MAIN="${ONLY_MAIN:-0}"
SKIP_MAIN_MERGE="${SKIP_MAIN_MERGE:-0}"
DO_BACKUP="${DO_BACKUP:-1}"
DRY_RUN="${DRY_RUN:-0}"
FORCE="${FORCE:-0}"

if [ "${FAST:-0}" = "1" ]; then DO_BACKUP=0; SKIP_MAIN_MERGE=1; FORCE=1; fi

run() { if [ "$DRY_RUN" = "1" ]; then echo "  [dry-run] $*"; else "$@"; fi; }

# ---------------------------------------------------------------- 0. vào repo
ROOT="$(git rev-parse --show-toplevel 2>/dev/null || true)"
[ -n "$ROOT" ] || { echo "LỖI: chạy từ trong thư mục repo (cd ~/lampac)."; exit 1; }
cd "$ROOT"
CUR="$(git branch --show-current)"
[ -n "$CUR" ] || { echo "LỖI: đang ở detached HEAD -> git checkout $CUR rồi chạy lại."; exit 1; }
echo "Repo :  $ROOT"
echo "Nhánh:  $CUR"
[ "$DRY_RUN" = "1" ] && echo "*** DRY_RUN: không push, không xoá gì cả ***"

# ---------------------------------------------------- 1. gh + git credential
command -v gh >/dev/null 2>&1 || { echo "Cài gh..."; pkg install -y gh; }
if ! gh auth status >/dev/null 2>&1; then
  echo "Chưa đăng nhập gh. Chạy 1 lần:"
  echo "  gh auth login -h github.com -p https -w"
  echo "rồi chạy lại script."
  exit 1
fi
gh auth setup-git

# ------------------------------------------------- 2. lịch sử đầy đủ + mới nhất
if [ "$(git rev-parse --is-shallow-repository)" = "true" ]; then
  echo "Repo là shallow clone (README dùng --depth 1) -> lấy lại toàn bộ lịch sử..."
  git fetch --unshallow origin 2>/dev/null || git fetch --depth=3000 origin
fi
git fetch origin --prune --tags
git remote set-head origin -a

if git rev-parse --verify -q "origin/$CUR" >/dev/null; then
  BEHIND="$(git rev-list --count "HEAD..origin/$CUR")"
  if [ "$BEHIND" != "0" ]; then
    echo "$CUR ở máy đang sau GitHub $BEHIND commit -> fast-forward..."
    run git pull --ff-only origin "$CUR" || { echo "Không ff được; tự gộp rồi chạy lại."; exit 1; }
  fi
fi

# ------------------------------------------------------------ 3. cây bẩn -> stash
if [ -n "$(git status --porcelain)" ]; then
  echo "Cây làm việc bẩn -> stash (xem lại: git stash list)."
  run git stash push -u -m "pre-cleanup $(date -Iseconds)" || exit 1
fi

# ------------------------------ 4. trỏ setup-termux.sh / README về main
# 45 URL raw.githubusercontent có thể đang hard-code một nhánh làm việc tạm (không phải
# main). Nếu xoá nhánh đó mà không sửa, `setup-termux.sh --sync` và `lampac update`
# sẽ 404 hàng loạt.
NEEDS_FIX="$(git grep -l -E 'arena/01a0[0-9a-f]+-lampac' -- setup-termux.sh setup-gstreamer-hdr.sh README.md 2>/dev/null || true)"
if [ -n "$NEEDS_FIX" ]; then
  echo "Sửa tham chiếu nhánh hard-code -> main: $NEEDS_FIX"
  if [ "$DRY_RUN" != "1" ]; then
    for f in $NEEDS_FIX; do
      sed -i -E \
        -e 's|(nguyenquocngu7863-ai/lampac)/arena/01a0[0-9a-f]+-lampac|\1/main|g' \
        -e 's|--branch[[:space:]]+arena/01a0[0-9a-f]+-lampac|--branch main|g' \
        "$f"
    done
    LEFT="$(git grep -c -E 'arena/01a0[0-9a-f]+-lampac' -- setup-termux.sh setup-gstreamer-hdr.sh README.md 2>/dev/null || true)"
    [ -n "$LEFT" ] && echo "  CÒN SÓT, kiểm tra tay: $LEFT"
    git add setup-termux.sh setup-gstreamer-hdr.sh README.md
    git commit -q -m "chore(repo): point termux sync sources at main after branch cleanup" || true
  else
    echo "  [dry-run] sed -E 's|/arena/01a0*-lampac|/main|g' $NEEDS_FIX && git commit"
  fi
fi

# -------------------------------------------------------- 5. sao lưu (nên làm)
# 23 commit trên các nhánh arena/* CHƯA có ở nhánh này (MX Sub Bridge APK,
# subsense-download.js, Jackett trong setup-termux, fix VidLink, tiếng Việt...).
if [ "$DO_BACKUP" = "1" ]; then
  BK="$HOME/lampac-backup-$(date +%Y%m%d-%H%M%S)"
  mkdir -p "$BK"
  echo "Sao lưu mọi ref -> $BK/all-refs.bundle"
  run git bundle create "$BK/all-refs.bundle" --all
  echo "Gắn tag archive/<nhánh> để commit còn sống sau khi xoá nhánh:"
  while read -r sha ref; do
    b="${ref#refs/heads/}"
    echo "  archive/$b -> ${sha:0:8}"
    run git update-ref "refs/tags/archive/$b" "$sha"
  done < <(git ls-remote --heads origin 'refs/heads/arena/*')
  run git push origin 'refs/tags/archive/*:refs/tags/archive/*'
  echo "  Khôi phục: git fetch $BK/all-refs.bundle 'refs/tags/archive/*:refs/tags/archive/*'"
fi

# --------------------------------------------- 6. merge main để không mất gì
# origin/main có 1 commit chỉ ở đó: thêm lampa-en.js (1360 dòng). Merge trước để
# push lên main là fast-forward -> không cần force, không mất file.
if [ "$SKIP_MAIN_MERGE" != "1" ]; then
  echo "Merge origin/main vào $CUR..."
  run git merge --no-edit origin/main || {
    echo "Xung đột khi merge origin/main -> sửa, git add -A && git commit, rồi chạy lại script."
    exit 1
  }
fi

# --------------------------------------------- 7. push lên main (+ nhánh hiện tại)
# FORCE=1 (FAST=1 bật tự động) -> --force-with-lease: main cũ bị ghi đè, MẤT lampa-en.js
# của commit 16d8bc6 mà chỉ main có. Muốn giữ thì chạy chế độ thường (merge, ff-push).
[ "$SKIP_MAIN_MERGE" = "1" ] && FORCE=1
PUSH_OPTS=""
[ "$FORCE" = "1" ] && PUSH_OPTS="--force-with-lease"
echo "Push HEAD -> main ${PUSH_OPTS:-(fast-forward)}"
run git push $PUSH_OPTS origin "HEAD:refs/heads/main" || {
  echo "Bị từ chối (non-fast-forward). Kiểm tra: git log --oneline HEAD..origin/main"
  echo "Nếu chắc chắn ghi đè (MẤT lampa-en.js): git push --force-with-lease origin HEAD:main"
  exit 1
}
if [ "$ONLY_MAIN" != "1" ]; then
  echo "Push $CUR (điểm phục hồi cho phiên Arena)"
  run git push -u origin "$CUR:$CUR"
fi

# ------------------------------------ 8. liệt kê nhánh thừa, hỏi 1 lần rồi xoá
DEL="$(git ls-remote --heads origin | awk '{print $2}' | sed 's|refs/heads/||' \
      | { grep -vx -e main -e "$CUR" || true; })"
if [ -z "$DEL" ] && [ "$ONLY_MAIN" != "1" ]; then
  echo "Không còn nhánh thừa."
else
  echo "Sẽ XOÁ nhánh remote:"
  [ -n "$DEL" ] && echo "$DEL" | sed 's/^/  - /'
  [ "$ONLY_MAIN" = "1" ] && echo "  - $CUR  (ONLY_MAIN=1)"
  if [ "$DRY_RUN" = "1" ]; then
    echo "  [dry-run] git push origin --delete <từng nhánh ở trên>"
  else
    printf 'Xoá thật? [y/N] '
    read -r ans
    case "$ans" in
      [yY]*)
        for b in $DEL $([ "$ONLY_MAIN" = "1" ] && echo "$CUR"); do
          printf '  xoá %-32s' "$b"
          if git push origin --delete "$b" >/dev/null 2>&1; then echo "ok"; else echo "THẤT BẠI"; fi
        done
        ;;
      *) echo "  Bỏ qua bước xoá." ;;
    esac
  fi
fi

# ---------------------------------------------------- 9. dọn nhánh trong máy
if [ "$DRY_RUN" != "1" ]; then
  git branch --list | sed 's/^\**[[:space:]]*//' | grep -vx -e main -e "$CUR" | while read -r b; do
    [ -n "$b" ] && { echo "  xoá local: $b"; git branch -D "$b" >/dev/null 2>&1; }
  done
fi
git fetch origin --prune --prune-tags
if [ "$ONLY_MAIN" = "1" ]; then
  git remote set-branches origin main
else
  git remote set-branches origin main "$CUR"
fi
git fetch origin --prune

# -------------------------------------------------------------- 10. kiểm tra lại
echo "=== default branch ==="; gh api "repos/$REPO" -q .default_branch
echo "=== main bây giờ ==="; gh api "repos/$REPO/commits/main" -q '.sha[0:8] + "  " + (.commit.message | split("\n")[0])'
echo "=== nhánh còn trên GitHub ==="; git ls-remote --heads origin | sed 's/^/  /'
echo "=== tag sao lưu ==="; git ls-remote --tags origin | sed 's/^/  /' | head -20
[ "$DRY_RUN" = "1" ] && echo "(dry-run nên số liệu trên vẫn là hiện tại)"
echo
echo "Đồng bộ lại cài đặt trên điện thoại:"
echo "  cd ~/lampac && git pull --ff-only origin main && bash setup-termux.sh --sync && lampac stop && lampac start"
