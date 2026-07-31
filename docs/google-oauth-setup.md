# Cấu hình Google OAuth

AutoSaveGame dùng OAuth desktop app và scope https://www.googleapis.com/auth/drive.appdata.

## Tạo OAuth client

1. Mở Google Cloud Console tại https://console.cloud.google.com/ và chọn hoặc tạo một project.
2. Vào APIs & Services → Library, tìm Google Drive API và chọn Enable.
3. Vào Google Auth Platform:
   - Điền thông tin branding bắt buộc.
   - Chọn audience phù hợp. Với app cá nhân, dùng External và để trạng thái Testing.
   - Thêm tài khoản Google sẽ dùng vào danh sách test users.
4. Vào Clients → Create client.
5. Chọn application type Desktop app và tạo client.
6. Ghi lại Client ID và Client secret.

Desktop OAuth client là public client: giá trị client secret không thể được xem như bí mật máy chủ. Dự án vẫn không commit hai giá trị này để tránh gắn source code với một Google project cụ thể.

## Chạy trong PowerShell

Chỉ đặt hai giá trị trong process PowerShell hiện tại:

    $env:AUTOSAVEGAME_GOOGLE_CLIENT_ID = 'your desktop client id'
    $env:AUTOSAVEGAME_GOOGLE_CLIENT_SECRET = 'your desktop client secret'
    .\AutoSaveGame.exe

Khi đóng PowerShell, hai process environment variables này không còn. Không dùng setx, không ghi chúng vào .env, source code hoặc Git.

## Máy quán net

Google OAuth mở trình duyệt mặc định và dùng loopback callback trên 127.0.0.1. Ứng dụng đặt prompt=select_account, nhưng không kiểm soát cookie của trình duyệt.

Ưu tiên Guest/Private browser profile. Sau khi cấp quyền:

1. Đóng cửa sổ xác nhận OAuth.
2. Đăng xuất tài khoản Google khỏi trình duyệt nếu phiên không phải Guest/Private.
3. Khi rời máy, chọn Exit trong AutoSaveGame để app revoke token và xóa token trong RAM.

Mất điện có thể ngăn bước revoke, nhưng token không được ghi xuống đĩa bởi AutoSaveGame.

