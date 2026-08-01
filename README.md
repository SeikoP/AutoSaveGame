# AutoSaveGame

AutoSaveGame là ứng dụng Windows dùng Google Drive để giữ một snapshot save hiện hành cho mỗi game. Ứng dụng không cần quyền administrator, Steam Cloud, Supabase hoặc Google Drive Desktop.

## Cài đặt

Cách dễ nhất: mở [bản phát hành mới nhất](https://github.com/SeikoP/AutoSaveGame/releases/latest), tải `AutoSaveGame-Setup.exe` và chạy. Ứng dụng được cài cho tài khoản Windows hiện tại tại `%LOCALAPPDATA%\Programs\AutoSaveGame` và xuất hiện trong Start Menu.

Cài nhanh từ PowerShell:

```powershell
irm https://raw.githubusercontent.com/SeikoP/AutoSaveGame/main/scripts/Install.ps1 | iex
```

Script tải đúng asset chính thức, kiểm tra SHA-256 rồi mới cài. Nếu quán net chặn PowerShell hoặc GitHub raw content, hãy dùng trực tiếp file setup ở trang release.

## Cách sử dụng

1. Mở AutoSaveGame và đăng nhập Google. Bản phát hành chính thức đã chứa cấu hình OAuth của ứng dụng; người dùng không phải tự nhập client ID hoặc secret.
2. Thêm game và chọn đúng thư mục save. App tạo bản backup đầu tiên ngay sau khi thêm.
3. Khi sang máy mới, đăng nhập, chọn game và bấm Restore trước khi mở game.
4. Để ứng dụng chạy ở system tray trong lúc chơi.
5. Trước khi rời máy, chỉ thoát khi trạng thái cho biết backup cloud đã được xác nhận. Có thể bấm Backup now để đẩy ngay thay đổi đang chờ.

## An toàn trên máy công cộng

- Ứng dụng chỉ giữ OAuth token trong RAM và yêu cầu đăng nhập lại ở phiên sau.
- Ứng dụng không thể xóa cookie đăng nhập trong trình duyệt. Hãy dùng Guest/Private mode nếu có thể, đăng xuất Google và đóng toàn bộ cửa sổ trình duyệt sau khi cấp quyền.
- Scope duy nhất là Google Drive appDataFolder; app không đọc các file Drive thông thường.
- Nếu mất điện trong lúc save đang được ghi hoặc upload chưa hoàn tất, cloud vẫn giữ snapshot đã xác nhận trước đó. Thay đổi mới nhất có thể chưa được lưu.
- Khi Google Drive hết quota hoặc gặp lỗi mạng, trạng thái không được báo đã xác nhận; hãy giữ app mở và thử Backup now lại trước khi rời máy.

## Phát triển

Yêu cầu .NET 10 SDK:

    dotnet test AutoSaveGame.sln
    dotnet build AutoSaveGame.sln -c Release
    dotnet publish src/AutoSaveGame.App/AutoSaveGame.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/win-x64

Smoke test không dùng Google credentials:

    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/SmokeTest.ps1 -Executable artifacts/win-x64/AutoSaveGame.exe

Output portable nằm tại artifacts/win-x64/AutoSaveGame.exe.

Developer build không nhúng OAuth credentials. Xem `docs/google-oauth-setup.md` để cấu hình biến môi trường cục bộ; thông tin này không được commit vào repository.

GitHub Actions CI chạy test, Release build và smoke test trên chính executable
đã publish. Smoke test dùng cloud giả lập để kiểm tra backup → xóa local →
restore và đối chiếu hash; kết quả này không thay thế kiểm chứng đăng nhập OAuth
và Google Drive thật trước khi phát hành.

## Giới hạn MVP

- Chỉ giữ một snapshot đã commit cho mỗi game.
- Giả định một phiên app hoạt động cho mỗi tài khoản Google.
- Chưa tự nhận diện save path hoặc Steam App ID.
- Chưa tự mở game sau restore.
- Live Google Drive test cần OAuth client riêng và không chạy trong test tự động.
