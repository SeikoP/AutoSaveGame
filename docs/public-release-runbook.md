# Runbook phát hành public

## 1. Chuẩn bị Google OAuth production

1. Tạo một Google Cloud project riêng cho production và bật Google Drive API.
2. Trong Google Auth Platform, đặt audience là **External** và khai báo tên app, email hỗ trợ, email liên hệ nhà phát triển.
3. Chỉ thêm scope `https://www.googleapis.com/auth/drive.appdata`. Đây là scope non-sensitive và chỉ truy cập vùng dữ liệu ẩn riêng của ứng dụng.
4. Chuyển Publishing status sang **In production**. Nếu vẫn để **Testing**, chỉ test users được đăng nhập và quyền đã cấp hết hạn sau 7 ngày.
5. Tạo OAuth client loại **Desktop app**. Lưu Client ID và Client secret vào nơi quản lý bí mật; không commit vào Git.
6. Hoàn thiện basic OAuth app verification/branding nếu Google Cloud Console yêu cầu. `drive.appdata` không yêu cầu quy trình sensitive/restricted scope, nhưng app public vẫn phải tuân thủ Google API Services User Data Policy.

## 2. Cấu hình GitHub

Tạo environment tên `release` trong `Settings > Environments`. Thêm hai environment secret:

- `AUTOSAVEGAME_GOOGLE_CLIENT_ID`
- `AUTOSAVEGAME_GOOGLE_CLIENT_SECRET`

Workflow CI không đọc hai giá trị này. Chỉ job phát hành trong protected environment mới dùng chúng để sinh file JSON tạm trên runner và nhúng vào binary chính thức. Log không in nội dung JSON.

Nếu repository có nhiều maintainer, bật required reviewer cho environment `release` để tag không thể tự phát hành khi chưa duyệt.

## 3. Phát hành

Chạy toàn bộ kiểm tra cục bộ trước:

```powershell
dotnet test AutoSaveGame.sln -c Release
dotnet build AutoSaveGame.sln -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Test-Install.ps1
```

Tạo và đẩy tag mới, không tái sử dụng tag đã phát hành:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

Workflow `Release` sẽ xác minh tag, chạy test/build, smoke test portable, build và cài thử setup, tạo checksum rồi phát hành ba asset:

- `AutoSaveGame-Setup.exe`
- `AutoSaveGame-win-x64.zip`
- `SHA256SUMS.txt`

Có thể chạy lại bằng `workflow_dispatch` với một tag đã tồn tại chỉ khi GitHub Release cho tag đó chưa tồn tại. Workflow cố ý không ghi đè release hoặc asset cũ.

## 4. Kiểm thử chấp nhận thật

CI dùng cloud giả lập nên chưa chứng minh OAuth/Drive thật. Trên một Windows user sạch:

1. Tải `AutoSaveGame-Setup.exe` từ GitHub Release và đối chiếu SHA-256.
2. Cài app, đăng nhập bằng một Google Account không nằm trong danh sách test users.
3. Thêm một thư mục save mẫu và chờ trạng thái cloud đã xác nhận.
4. Đóng app, xóa thư mục local mẫu, mở lại app và đăng nhập lại.
5. Restore, rồi đối chiếu nội dung/hash với dữ liệu ban đầu.
6. Sign out/Exit và xác nhận phiên sau bắt buộc đăng nhập lại.

Chỉ công bố đường dẫn download rộng rãi sau khi vòng này qua. Nếu tài khoản ngoài danh sách test không đăng nhập được, kiểm tra lại Audience và Publishing status trước khi sửa code.

## 5. Rollback và cảnh báo Windows

Không thay asset của release cũ. Nếu có lỗi, đánh dấu release bị lỗi là prerelease hoặc ghi cảnh báo, sửa trên commit mới và phát hành version cao hơn.

Installer chưa có chữ ký code-signing nên Windows SmartScreen có thể cảnh báo vì publisher/reputation chưa được xác nhận. SHA-256 chỉ chứng minh file tải về khớp asset phát hành; nó không thay thế chữ ký số. Khi có chứng thư code-signing, ký cả app và setup trước bước tạo checksum.
# Chạy nhanh khi phát triển

Để chạy bản Debug có hot reload, tạo `.env` (hoặc dùng file `env` cũ) ở thư
mục gốc với hai biến OAuth như hướng dẫn trong `docs/google-oauth-setup.md`,
sau đó chạy:

```powershell
.\scripts\Run-Dev.ps1
```

`dotnet watch` sẽ hot reload khi thay đổi được .NET hỗ trợ và tự khởi động lại
app cho các thay đổi còn lại. Dừng bằng `Ctrl+C`. Đây là luồng test cục bộ,
không tạo installer.
