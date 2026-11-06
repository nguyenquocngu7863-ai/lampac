#!/data/data/com.termux/files/usr/bin/bash
# play.sh — nhập 1 link -> in CHUỖI GIẢI + HƯỚNG GIẢI để agent tự viết module.
# Nhận: trang detail, trang player/embed, thẳng .m3u8/.mp4, URL lite của Lampac,
#       hoặc file HTML đã lưu. Xem đầy đủ: play.sh --help
exec python3 "$(dirname "$(readlink -f "$0")")/play.py" "$@"
