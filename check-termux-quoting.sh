#!/bin/sh
# check-termux-quoting.sh — bắt một loại lỗi mà `bash -n` KHÔNG bắt được.
#
# Bối cảnh: trong setup-termux.sh, phần lớn script guest được nhúng dạng
#     proot-distro login ubuntu -- bash -c "
#         ...nhiều dòng...
#     "
# Tức là một chuỗi double-quote. Bên trong chuỗi đó `#` KHÔNG phải comment,
# còn backtick và $() thì VẪN là command substitution của host. Một cặp backtick
# trong comment vì thế khiến host chạy nhầm "command not found" và NUỐT toàn bộ
# text giữa hai backtick — payload gửi vào Ubuntu bị xén im lặng, không có lỗi
# cú pháp nào cả (đã xảy ra thật với comment `disableEng` ở dòng 695).
#
# Cách dùng:
#   sh check-termux-quoting.sh [file...]      # mặc định: các *.sh ở gốc repo
# Exit 0 = sạch, 1 = có lỗi.

set -u

files=$*
[ -n "$files" ] || files=$(ls ./*.sh 2>/dev/null)

status=0
for f in $files; do
    [ -f "$f" ] || continue

    out=$(awk '
        function count_quotes(line,   i, n, c) {
            n = 0
            for (i = 1; i <= length(line); i++) {
                c = substr(line, i, 1)
                if (c == "\"" && (i == 1 || substr(line, i - 1, 1) != "\\"))
                    n++
            }
            return n
        }
        {
            line = $0

            if (!inregion) {
                if (line ~ /(bash|sh)[[:space:]]+-c[[:space:]]+"[[:space:]]*$/) {
                    inregion = 1
                    openline = NR
                    total = count_quotes(line)
                }
                next
            }

            total += count_quotes(line)

            # backtick không được escape
            t = line
            gsub(/\\`/, "", t)
            if (index(t, "`") > 0)
                printf "  %s:%d: backtick trần trong chuỗi double-quote (mở ở dòng %d): %s\n", FILENAME, NR, openline, substr(line, 1, 90)

            # $() không được escape
            t = line
            gsub(/\\\$\(/, "", t)
            if (index(t, "$(") > 0)
                printf "  %s:%d: $( trần trong chuỗi double-quote (mở ở dòng %d): %s\n", FILENAME, NR, openline, substr(line, 1, 90)

            # dấu nháy kép chưa escape với số LẺ bên trong vùng guest = đóng
            # chuỗi sớm => phần còn lại của script guest bị host chạy. Dòng
            # đóng (" đứng một mình) là hợp lệ nên được loại trừ.
            t = line
            gsub(/\\"/, "", t)
            odd = 0
            n2 = split(t, c2, "")
            q = 0
            for (k = 1; k <= n2; k++)
                if (c2[k] == "\"") q++
            if (q % 2 == 1 && $0 !~ /^[[:space:]]*"\\?[[:space:]]*$/ && $0 !~ /^[[:space:]]*#/)
                printf "  %s:%d: số lẻ dấu \" chưa escape -> đóng chuỗi bash -c sớm: %s\n", FILENAME, NR, substr(line, 1, 90)

            if (total % 2 == 0)
                inregion = 0
        }
        END {
            if (inregion)
                printf "  %s: chuỗi double-quote mở ở dòng %d mà không đóng\n", FILENAME, openline
        }
    ' "$f")

    if [ -n "$out" ]; then
        printf '%s\n' "$out"
        status=1
    fi
done

if [ "$status" = 0 ]; then
    echo "✓ quoting OK: không có backtick / \$( trần bên trong chuỗi bash -c \"…\""
fi
exit $status
