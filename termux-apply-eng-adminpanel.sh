#!/usr/bin/env bash
# Áp dụng 8 nguồn ENG stock (1.50.1) + nhóm AdminPanel vào Lampac Termux.
#
# Chạy trong Termux (host):
#   curl -fsSL https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/arena/01a04b83-lampac/termux-apply-eng-adminpanel.sh -o eng.sh && bash eng.sh
#
# Tùy chọn: nếu đã merge vào main, set LAMPAC_SOURCE_REF=main
set -u

LAMPAC_SOURCE_REF="${LAMPAC_SOURCE_REF:-arena/01a04b83-lampac}"
SOURCE_BASE="https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/${LAMPAC_SOURCE_REF}"

info() { echo "[ENG] $1"; }
ok()   { echo "[ENG] ✓ $1"; }

info "Đang áp dụng từ: ${SOURCE_BASE}"
info "Chạy trong Ubuntu proot..."

proot-distro login ubuntu -- bash -c "
    set -euo pipefail
    base='${SOURCE_BASE}'
    stamp=\$(date +%s)

    pull() {
        local src=\$1 dest=\$2
        local dir
        dir=\$(dirname \"\$dest\")
        if [ ! -d \"\$dir\" ]; then
            echo \"  [skip] thiếu thư mục: \$dest\"
            return 0
        fi
        curl -fSL --retry 3 \"\$base/\$src?cb=\$stamp\" -o \"\$dest.tmp\"
        mv \"\$dest.tmp\" \"\$dest\"
        echo \"  [OK] \$dest\"
    }

    # 0) Backup bản cũ TRƯỚC khi ghi đè (để lùi lại nếu cần)
    bak=/root/lampac/module/eng-apply-20260829.bak
    mkdir -p \"\$bak\"
    for f in \
        OnlineENG/HydraFlix/ModInit.cs \
        OnlineENG/VidLink/Controller.cs \
        OnlineENG/VidLink/ModInit.cs \
        OnlineENG/VidLink/README.md \
        AdminPanel/ConfigSectionGroups.cs; do
        if [ -f \"/root/lampac/module/\$f\" ]; then
            mkdir -p \"\$bak/\$(dirname \$f)\"
            cp -f \"/root/lampac/module/\$f\" \"\$bak/\$f\"
        fi
    done
    echo \"  [bak] \$bak\"

    # 1) 8 nguồn ENG stock (HydraFlix host fix + VidLink stock resolver)
    pull Modules/OnlineENG/HydraFlix/ModInit.cs /root/lampac/module/OnlineENG/HydraFlix/ModInit.cs
    pull Modules/OnlineENG/VidLink/Controller.cs /root/lampac/module/OnlineENG/VidLink/Controller.cs
    pull Modules/OnlineENG/VidLink/ModInit.cs /root/lampac/module/OnlineENG/VidLink/ModInit.cs
    pull Modules/OnlineENG/VidLink/README.md /root/lampac/module/OnlineENG/VidLink/README.md

    # 2) Nhóm AdminPanel: nhóm ENG (10 nguồn) theo bản gốc
    pull Modules/AdminPanel/ConfigSectionGroups.cs /root/lampac/module/AdminPanel/ConfigSectionGroups.cs
"

ok "Xong. Khởi động lại để nạp module mới:"
echo
echo "  lampac stop && lampac start"
echo
echo "Sau đó mở AdminPanel → mục 'Nguồn · ENG (10 nguồn gốc)' để xem nhóm mới."
echo "Nếu muốn 10 nguồn hiện trong Lampa: đặt \"disableEng\": false trong init.conf."
