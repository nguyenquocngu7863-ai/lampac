# Termux, GitHub và Lampac

Tài liệu này ghi lại quy trình làm việc khi sửa mã nguồn trên GitHub, cập nhật bản cài Lampac trong Termux và khởi động lại server.

> **Lưu ý:** Termux có hai nơi khác nhau:
>
> - Repository mã nguồn: thường là `~/lampac` ở phía Termux.
> - Bản Lampac đang chạy: nằm bên trong Ubuntu proot tại `/root/lampac`.
>
> `git pull` chỉ cập nhật mã nguồn trong repository; nó **không tự cập nhật** bản Lampac đang chạy. Muốn cập nhật bản đang chạy, cần chạy `setup-termux.sh --sync` hoặc `--sync-all`.

## 1. Cài mới từ GitHub

```bash
pkg update -y
pkg install -y git curl

git clone --depth 1 --branch main https://github.com/nguyenquocngu7863-ai/lampac.git
cd ~/lampac
bash setup-termux.sh --install
```

Nếu thư mục đã tồn tại:

```bash
cd ~/lampac
git status
git pull --ff-only origin main
```

## 2. Các lệnh Git thường dùng

```bash
cd ~/lampac

git status                         # xem branch và file đang thay đổi
git branch -a                      # xem branch local/remote
git log --oneline -10              # xem commit gần đây
git fetch origin                   # lấy thông tin mới từ GitHub
git diff                           # xem thay đổi chưa commit
git diff --check                   # kiểm tra lỗi khoảng trắng
git remote -v                     # kiểm tra repository GitHub đang dùng
```

Không dùng `git reset --hard` nếu chưa chắc chắn, vì lệnh này có thể xoá thay đổi local.

## 3. Đưa branch agent lên GitHub

Trong phiên làm việc của agent, branch cố định là:

```text
arena/01a04e63-lampac
```

Quy trình chuẩn sau khi có commit:

```bash
cd ~/lampac
git add -A
git commit -m "mo-ta-ngan-gon-thay-doi"
git push origin arena/01a04e63-lampac
```

Nếu branch đã được agent push lên GitHub rồi thì chỉ cần lấy branch mới nhất:

```bash
git fetch origin arena/01a04e63-lampac
git log --oneline origin/arena/01a04e63-lampac -5
```

## 4. Đưa branch agent vào `main`

### Cách nhanh, không switch và không ảnh hưởng thay đổi local

Dùng đúng **hai dòng** dưới đây. Remote branch của agent là nguồn; `main` trên GitHub là đích. Không cần có local branch `main`, không cần checkout/switch, và thay đổi chưa commit trong `setup-termux.sh` không bị đụng tới:

```bash
git fetch origin
git push --force-with-lease origin origin/arena/01a04e63-lampac:main
```

`origin/arena/01a04e63-lampac` là dữ liệu nguồn, còn `:main` là branch đích. `--force-with-lease` chỉ cho phép cập nhật nếu `main` trên remote vẫn đúng phiên bản đã fetch; nó an toàn hơn `--force`. Sau lệnh này, `main` sẽ nhận commit mới nhất của branch agent, ví dụ `11a24fde`.

Không dùng các hướng dẫn `git switch main`, `git checkout main` hoặc merge qua local `main` cho quy trình này; chúng dễ bị chặn khi `setup-termux.sh` đang có thay đổi local hoặc khi clone chưa tạo local branch `main`.

Nếu GitHub từ chối do branch protection, không dùng force tiếp; cần xử lý theo chính sách bảo vệ branch của repository.

## 5. Cập nhật bản Lampac đang chạy

### Trường hợp repository local sạch

Không cần switch branch để cập nhật bản Lampac đang chạy. Nếu cần lấy `setup-termux.sh` mới, tải riêng rồi chạy:

```bash
cd ~/lampac
curl -fL "https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/main/setup-termux.sh" -o "$HOME/setup-termux-latest.sh"

lampac stop || true
bash "$HOME/setup-termux-latest.sh" --sync-all
lampac start
```

`--sync-all` dùng khi cần đồng bộ đầy đủ module tuỳ biến, AdminPanel, GStreamer và xoá module cũ. Với bản vá nhỏ chỉ cần các file trong danh sách sync:

```bash
cd ~/lampac
bash setup-termux.sh --sync
lampac stop && lampac start
```

### Trường hợp `setup-termux.sh` đang có thay đổi local

Không checkout đè file local. Tải một bản script mới vào thư mục Home, không dùng `/tmp`:

```bash
curl -fL "https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/main/setup-termux.sh" -o "$HOME/setup-termux-latest.sh"
ls -l "$HOME/setup-termux-latest.sh"
chmod +x "$HOME/setup-termux-latest.sh"

lampac stop || true
bash "$HOME/setup-termux-latest.sh" --sync-all
lampac start
```

> Gõ từng dòng riêng trong Termux. Khi copy/paste, không tách lệnh `curl` thành nhiều dòng; lỗi này có thể làm `curl` không ghi được file. Nếu gặp `curl: (23)`, dùng `$HOME` như ví dụ trên thay cho `/tmp`.

## 6. Xoá ngay module cũ trên bản đang chạy

Dùng khi cần xử lý nhanh các module đã xoá khỏi GitHub nhưng vẫn còn trong bản cài cũ:

```bash
lampac stop || true

proot-distro login ubuntu -- bash -c '
for root in /root/lampac/module /root/lampac/mods; do
    rm -rf "$root/OnlineENG/CineWave"
    rm -rf "$root/OnlineENG/Mapple4K"
    rm -rf "$root/OnlineENG/OpenDirectory"
    rm -f "$root/NextHUB/sites/85po.yaml"
done
'

lampac start
```

Kiểm tra bốn nguồn đã biến mất:

```bash
proot-distro login ubuntu -- bash -c '
for root in /root/lampac/module /root/lampac/mods; do
    for path in \
        OnlineENG/CineWave \
        OnlineENG/Mapple4K \
        OnlineENG/OpenDirectory \
        NextHUB/sites/85po.yaml; do
        if [ -e "$root/$path" ]; then
            echo "CON: $root/$path"
        else
            echo "OK:  $root/$path"
        fi
    done
done
'
```

Sau khi server đã cập nhật, hãy thoát hẳn ứng dụng Lampa rồi mở lại. Danh sách nguồn có thể đang được cache trong WebView.

## 7. Các lệnh quản lý Lampac

```bash
lampac start       # chạy Lampac ở terminal hiện tại; Ctrl+C để dừng
lampac stop        # dừng Lampac
lampac status      # xem trạng thái
lampac info        # xem IP, port và vị trí cấu hình
lampac config      # mở init.conf bằng nano
lampac update      # cập nhật release rồi đồng bộ module
```

Các lệnh dịch vụ phụ:

```bash
aio status
aio start
aio stop

jackett status
jackett start
jackett stop
```

Nếu lỡ gõ thừa khoảng trắng trước `aio`, gõ lại không có khoảng trắng đầu dòng.

## 8. Lỗi thường gặp

### `Your local changes ... would be overwritten by checkout`

Có file local chưa commit. Cất lại trước khi checkout:

```bash
git stash push -u -m "backup-local-changes"
```

Hoặc dùng cách push trực tiếp remote branch ở mục 4, không cần checkout branch.

### `src refspec main does not match any`

Lỗi này xảy ra khi clone chưa có local branch `main`. Không cần tạo branch local; dùng remote branch agent làm nguồn:

```bash
git fetch origin
git push --force-with-lease origin origin/arena/01a04e63-lampac:main
```

### `bash: ...setup-termux-latest.sh: No such file or directory`

Lệnh tải script đã thất bại. Kiểm tra lại bằng:

```bash
ls -l "$HOME/setup-termux-latest.sh"
```

Nếu không có file, tải lại bằng lệnh `curl -fL ... -o "$HOME/setup-termux-latest.sh"` ở mục 5.

### Vẫn thấy nguồn cũ sau khi restart

Kiểm tra theo thứ tự:

1. Đảm bảo commit đã được đưa vào `main` trên GitHub.
2. Chạy `setup-termux.sh --sync-all` hoặc xoá thủ công theo mục 6.
3. Chạy `lampac stop` rồi `lampac start`.
4. Thoát hẳn và mở lại ứng dụng Lampa để xoá cache WebView.
