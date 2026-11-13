# Sổ site hỏng / đã xoá module

Mỗi mục: site, ngày xoá, bằng chứng đo được, kết luận. Không xoá mục cũ (để khỏi làm lại).

---

## Tazzly — XOÁ 2026-11-09

- Domain lúc xoá: `https://sweetievulgar.com` (trước đó đã đổi liên tục:
  `tazzly.com` → `phimsexmoi1.com` → `singapore-packaging.com` → `okuracc.com` → `sweetievulgar.com`).
- Embed: `https://embed.sweetievulgar.com/embed/<code>/p1`
- Playlist: `https://embed.sweetievulgar.com/phim-sex/<code>/cdn.m3u8`

### Lý do xoá: web trả stream lung tung, phần lớn phim không phát được

Đo bằng `play.sh` (ffprobe + ffmpeg decode 5s đầu/cuối qua route Lampac) và curl_cffi trực tiếp:

| Phim | Playlist | Segment đầu | Kết quả |
|---|---|---|---|
| `thay-oi-em-suong` | 377 segment | `ibyteimg` 3.4 MB, TS hợp lệ | **PASS** |
| `cau-hoc-tro-...` | — | — | **PASS** |
| `phai-long-ban-than-cua-nguoi-yeu` | 744 segment (716 `p16-sg.tiktokcdn.com`) | tiktokcdn **403** `{"code":3403}`, ibyteimg trả **PNG 95 byte** | **FAIL** (timeout 30s) |
| `nguyen-thao-ngan-body-...` | 26 segment, toàn `ibyteimg` | **PNG 81 byte** | **FAIL** (decode rc=234) |

Tức 2/4 phim test hỏng, và hỏng theo kiểu **không sửa được từ phía module**:

1. **Segment không phải video.** CDN `p16-oec-sg.ibyteimg.com` trả `image/png` 81–95 byte
   (ảnh PNG 1×1) thay cho segment TS. Đôi lúc trả đúng TS (1.3–3.4 MB) cùng URL — tuỳ request.
2. **`p16-sg.tiktokcdn.com` chặn 403 toàn bộ.** Thử đủ: `Referer` embed/tiktok, `Origin`,
   `User-Agent` Chrome, `Range`, cookie phiên sau khi mở embed, Chrome headful (`x.sh`) —
   vẫn 403 `{"code":3403,"error":"fail to get resource"}`. Không có header nào cứu được.
3. **Cùng URL, khác kết quả mỗi lần.** `seg1` trả 1.35 MB (TS) / 81 B (PNG) / timeout
   25s ở 3 lần liên tiếp. Site không ổn định ở tầng CDN.
4. Trang embed cũng có lúc trả `403 Forbidden` cho chính Chrome headful.

### Kết luận

Nguồn phát (TikTok/ByteDance CDN) bị chủ site cấu hình sai hoặc bị giới hạn theo vùng/phiên;
Lampac chỉ proxy lại nên **không thể vá**. Giữ module chỉ tổ ong thêm lỗi `stream_links`.

Trạng thái: **đã xoá** `Modules/Adult/Tazzly/` (source) và `~/lampac-run/module/Adult/Tazzly/`
(runtime). Route `/tazzly` giờ 404. Ảnh bìa (`static.sweetievulgar.com`) vẫn tải được bình thường —
lỗi nằm ở CDN video, không phải ở ảnh.

---

## Topsex — XOÁ 2026-11-13

- Domain lúc xoá: `https://s1.topsex.blog` (trong `manifest.json`, `ModInit.cs`, `Service.cs`).
- Module được add ở commit `4e5162b4` nhưng chưa từng verify (vẫn `[ ]` trong `todo.md`).

### Lý do xoá: tên miền chết (không phải lỗi code)

Triệu chứng: `GET /topsex` trả `503 {"msg":"playlists"}` (0 item). Module vẫn compile sạch
(`check.sh` errors: 0), vẫn `loaded module: Topsex`, source = runtime (identical).

Đo tầng T1 (mạng/DNS) — tất cả chỉ ra domain không còn resolve:

| Phép đo | Kết quả |
|---|---|
| `python3 socket.gethostbyname('s1.topsex.blog')` | `[Errno 7] No address associated with hostname` |
| DoH Google + Cloudflare, `s1.topsex.blog` type A | `Status 3` (NXDOMAIN), không có Answer |
| DoH, `topsex.blog` type A/AAAA/CNAME/MX | `Status 0` nhưng **không có bản ghi nào**; chỉ còn NS `launch1/launch2.spaceship.net` |
| Probe 17 subdomain (`s1..s10`, `www`, `m`, `api`, `cdn`, `img`…) | không cái nào có A record |
| `curl_cffi` (chrome124) trực tiếp `https://s1.topsex.blog/` | `DNSError (6) Could not resolve host` |
| `curl_cffi` qua proxy Oracle `168.107.66.134:3128` | Timeout 25s |
| `r.jina.ai/https://s1.topsex.blog/` | HTTP 422 (jina cũng không fetch được) |
| Đối chứng `sexvietop.com` / `google.com` / `vnjav.net` | resolve bình thường → DNS máy còn tốt |

Kiểu chết giống các site blog hay đổi subdomain `s1/s2/...`: apex còn NS (Spaceship) nhưng
mọi A record đã bị gỡ, `s1` thành NXDOMAIN. Không phải Cloudflare chặn, không phải lỗi parser,
không phải lỗi proxy — **không còn host để gọi**. Sửa `SiteHost` cũng vô nghĩa vì không có
subdomain nào sống.

### Kết luận

Web die hẳn, không vá được từ phía module. User chốt: xoá + push.

Trạng thái: **đã xoá** `Modules/Adult/Topsex/` (source) và `~/lampac-run/module/Adult/Topsex/`
(runtime). Route `/topsex` giờ 404. `detail2` (HTML capture cũ, untracked) và comment trong
`VietSexBlog/Service.cs` chỉ còn giá trị tham khảo, không ảnh hưởng chạy.
