# AutoSaveGame

AutoSaveGame là ứng dụng Windows portable dùng Google Drive để giữ một snapshot save hiện hành cho mỗi game. Ứng dụng không cần quyền administrator, Steam Cloud, Supabase hoặc Google Drive Desktop.

## Cách sử dụng

1. Cấu hình Google OAuth theo hướng dẫn tại docs/google-oauth-setup.md.
2. Mở AutoSaveGame.exe và đăng nhập Google.
3. Thêm game, chọn đúng thư mục save.
4. Khi sang máy mới, đăng nhập, chọn game và bấm Restore trước khi mở game.
5. Để ứng dụng chạy ở system tray trong lúc chơi.
6. Trước khi rời máy, chỉ thoát khi trạng thái cho biết backup cloud đã được xác nhận. Có thể bấm Backup now để đẩy ngay thay đổi đang chờ.

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
