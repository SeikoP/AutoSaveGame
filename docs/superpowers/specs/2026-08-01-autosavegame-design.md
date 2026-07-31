# AutoSaveGame Design

## 1. Mục tiêu

AutoSaveGame là ứng dụng Windows portable dành cho người chơi game tại quán net. Ứng dụng sao lưu save game lên Google Drive mà không phụ thuộc Steam Cloud, đồng thời có thể khôi phục danh sách game và save sau khi tải lại ứng dụng hoặc chuyển sang máy khác.

MVP ưu tiên không làm mất bản cloud đang dùng được. Ứng dụng không cần quyền administrator và không cài Windows Service.

## 2. Phạm vi MVP

Người dùng có thể:

- Đăng nhập bằng một tài khoản Google qua trình duyệt hệ thống.
- Thêm, sửa và xóa cấu hình game gồm tên game và đường dẫn thư mục save.
- Xem lại danh sách game sau khi cài lại ứng dụng.
- Khôi phục bản save hiện hành của một game vào đúng thư mục local.
- Bật hoặc tắt theo dõi cho từng game.
- Tự động sao lưu khi file trong thư mục save thay đổi.
- Xem trạng thái đồng bộ và thời điểm backup thành công gần nhất.
- Chủ động bấm `Backup now` trước khi rời máy.

MVP không bao gồm chia sẻ save, dashboard quản trị, cấu hình game cộng đồng, nhiều phiên bản lịch sử, đồng bộ đồng thời trên nhiều máy hoặc tự động nhận diện toàn bộ game Steam.

## 3. Nền tảng và đóng gói

- Ngôn ngữ: C#.
- Runtime: .NET 10.
- UI: WPF.
- Package manager: NuGet.
- Hệ điều hành đích: Windows 10/11 x64.
- Phân phối: executable self-contained, portable, không yêu cầu .NET được cài trên máy quán net.

Ứng dụng chạy trong system tray trong suốt phiên chơi. Việc tự khởi động cùng Windows không thuộc MVP vì máy quán net có thể xóa dữ liệu sau khi khởi động lại và người dùng không có quyền administrator.

## 4. Lưu trữ và xác thực

Google Drive là nguồn lưu trữ cloud duy nhất của MVP. Supabase không được sử dụng.

Ứng dụng dùng OAuth 2.0 dành cho desktop app và mở trình duyệt hệ thống để đăng nhập. OAuth client của desktop app là public client nên không coi client secret nhúng trong executable là bí mật.

Token chỉ tồn tại trong bộ nhớ của tiến trình. Ứng dụng không ghi refresh token xuống máy quán net; người dùng đăng nhập lại ở mỗi lần mở app. Mục tiêu là không để phiên Google có thể tái sử dụng trên máy công cộng sau khi app đóng hoặc máy mất điện.

App không thể xóa cookie đăng nhập do trình duyệt hệ thống quản lý. UI phải cảnh báo người dùng sử dụng cửa sổ Guest/Private nếu máy là máy công cộng và đóng cửa sổ đó sau khi OAuth hoàn tất. App luôn yêu cầu Google hiển thị bước chọn tài khoản, không giả định tài khoản đang đăng nhập trong trình duyệt là tài khoản cần dùng.

Ứng dụng chỉ yêu cầu scope tối thiểu cho `appDataFolder`. Dữ liệu trong thư mục này thuộc Drive của người dùng, tính vào quota Drive và không hiển thị như file thông thường trong giao diện Drive.

## 5. Mô hình dữ liệu cloud

`appDataFolder` chứa:

- Các catalog bất biến mang generation, ví dụ `catalog-00000042-<uuid>.json`.
- Một archive hiện hành cho mỗi game, được đặt bằng ID ổn định thay vì tên game.

Khi khởi động, app liệt kê catalog, kiểm tra nội dung và chọn generation hợp lệ cao nhất. Catalog mới được tạo thành file mới; app không ghi đè catalog đang dùng. Sau khi xác nhận catalog mới đọc lại được, catalog cũ được xóa theo best effort. Nếu có nhiều catalog khác nội dung ở cùng generation cao nhất, app coi đó là xung đột giữa hai phiên và không tự chọn một bản.

Mỗi game có metadata:

- `gameId`: UUID do app tạo.
- `displayName`: tên hiển thị.
- `pathTemplate`: đường dẫn portable.
- `archiveFileId`: ID file Drive của snapshot hiện hành.
- `archiveSha256`: hash của archive hiện hành.
- `contentSha256`: hash xác định nội dung snapshot.
- `archiveSize`: kích thước byte.
- `lastBackupUtc`: thời điểm backup được cloud xác nhận.
- `sourceMachineId`: ID ngẫu nhiên của phiên cài đặt tạo snapshot.
- `generation`: số nguyên tăng đơn điệu của catalog chứa metadata này.

MVP chỉ giữ một snapshot đã commit cho mỗi game. Trong lúc upload có thể tồn tại tạm snapshot cũ và file upload mới. File cũ chỉ bị xóa sau khi snapshot mới và catalog generation mới đã được xác nhận.

## 6. Quy tắc đường dẫn

Ứng dụng chuẩn hóa đường dẫn người dùng chọn thành biến môi trường khi có thể:

- `%USERPROFILE%`
- `%APPDATA%`
- `%LOCALAPPDATA%`
- `%PROGRAMDATA%`

Ví dụ `C:\Users\Admin\Documents\My Games\Example` được lưu thành `%USERPROFILE%\Documents\My Games\Example`.

Khi mở trên máy mới, app mở rộng biến theo tài khoản Windows hiện tại. Nếu đường dẫn không tồn tại, người dùng có thể xác nhận tạo thư mục hoặc chọn lại đường dẫn. Việc đổi đường dẫn local không làm đổi `gameId` hoặc snapshot cloud.

## 7. Luồng khởi động và restore

1. Người dùng mở executable và đăng nhập Google.
2. App đọc catalog generation hợp lệ cao nhất; nếu chưa có thì tạo catalog generation đầu tiên.
3. Danh sách game được hiển thị cùng trạng thái đường dẫn local.
4. Người dùng chọn game và bấm `Restore`.
5. App dừng watcher của game và khóa thao tác backup/restore cho game đó.
6. App tải archive vào thư mục tạm thuộc phiên chạy.
7. App kiểm tra kích thước và SHA-256 của archive trước khi giải nén.
8. App giải nén vào một thư mục staging và chặn mọi entry thoát khỏi staging để tránh Zip Slip.
9. Nếu thư mục local đang có dữ liệu, app di chuyển dữ liệu đó vào rollback folder tạm.
10. App thay thế nội dung save bằng staging, xác minh lại nội dung và chỉ sau đó xóa rollback folder.
11. Nếu có lỗi, app khôi phục rollback folder và giữ nguyên snapshot cloud.
12. App bật lại watcher sau khi restore thành công.

Restore không tự động ghi đè âm thầm khi game có khả năng đang chạy. Nếu không xác định chắc chắn tiến trình game, app hiển thị cảnh báo yêu cầu người dùng đóng game trước khi tiếp tục.

## 8. Phát hiện thay đổi

Mỗi game đang theo dõi dùng hai cơ chế:

- `FileSystemWatcher` theo dõi đệ quy để phản ứng nhanh với create, change, rename và delete.
- Quét định kỳ 30 giây để tính fingerprint và bù các sự kiện watcher bị bỏ lỡ hoặc buffer overflow.

Sự kiện thay đổi chỉ đánh dấu game là dirty. App không upload ngay trong callback watcher.

Snapshot được lên lịch khi thư mục không nhận sự kiện mới trong 3 giây. Trước khi đọc, app kiểm tra hai lần kích thước và thời gian sửa file, cách nhau 1 giây. Nếu file còn thay đổi hoặc đang bị khóa, app retry với backoff cho tới tối đa 60 giây. Sau đó trạng thái chuyển thành `Pending` và lần quét kế tiếp sẽ thử lại.

Người dùng có thể bấm `Backup now`; thao tác này vẫn phải chờ file ổn định nhưng bỏ qua debounce thông thường.

## 9. Tạo và commit snapshot

Snapshot bao gồm toàn bộ file trong thư mục save, không đồng bộ từng file. Trình tự:

1. Enumerate file theo thứ tự đường dẫn chuẩn hóa.
2. Bỏ qua symlink/reparse point để không đọc ngoài thư mục save.
3. Tạo archive ZIP trong thư mục tạm, không tạo bên trong thư mục đang theo dõi.
4. Tính SHA-256 cho nội dung logic và archive.
5. Nếu `contentSha256` giống snapshot cloud hiện hành thì bỏ upload.
6. Upload archive với tên tạm duy nhất.
7. Đọc metadata Drive của file vừa upload và xác nhận kích thước.
8. Tạo catalog generation mới trỏ tới file vừa upload.
9. Đọc lại catalog mới để xác nhận commit.
10. Xóa archive và catalog cũ theo best effort.

Nếu mất điện hoặc mạng lỗi trước bước 8, catalog mới chưa tồn tại và generation cũ vẫn trỏ tới archive cũ. Nếu mất điện sau bước 8 nhưng trước bước 10, cả hai generation và archive có thể tồn tại nhưng restore dùng generation hợp lệ cao nhất. Lần đăng nhập sau sẽ dọn file cũ hoặc orphan sau khi xác định chắc chắn file đó không được catalog hiện hành tham chiếu.

## 10. Đồng thời và xung đột

MVP giả định một phiên app hoạt động tại một thời điểm cho mỗi tài khoản Google. `sourceMachineId` và phiên bản catalog được kiểm tra trước khi commit.

Trước khi commit, app đọc lại generation cloud. Nếu generation đã thay đổi kể từ lúc app tải catalog, app không tạo catalog mới. App dừng backup của game, tải catalog mới và báo xung đột để người dùng chọn dùng cloud hoặc tạo lại snapshot từ local. Nếu hai phiên vẫn đồng thời tạo hai catalog khác nhau ở cùng generation, lần đọc kế tiếp phát hiện fork và chuyển sang `Conflict`. Không dùng chiến lược last-write-wins âm thầm.

Trong một tiến trình, mỗi game có một hàng đợi tuần tự; không thể chạy backup và restore đồng thời cho cùng game.

## 11. Trạng thái UI

Mỗi game có một trong các trạng thái:

- `Not configured`: path local chưa hợp lệ.
- `Watching`: đang theo dõi và local khớp snapshot gần nhất.
- `Dirty`: đã thấy thay đổi, đang chờ file ổn định.
- `Backing up`: đang tạo hoặc upload snapshot.
- `Pending`: chưa backup được, sẽ retry.
- `Restoring`: đang tải hoặc thay thế save local.
- `Conflict`: cloud đã thay đổi từ phiên khác.
- `Error`: cần người dùng xử lý.

System tray hiển thị tiến trình đang chạy và cảnh báo nếu còn game `Dirty`, `Pending`, `Conflict` hoặc `Error`. Khi người dùng chọn Exit, app đề nghị backup các game dirty và chỉ thoát sau khi người dùng xác nhận bỏ qua hoặc backup hoàn tất.

## 12. Xử lý lỗi và giới hạn bảo vệ

- Mạng mất: giữ trạng thái dirty/pending và retry có backoff khi app còn chạy.
- Drive hết quota: không xóa snapshot cũ; hiển thị lỗi rõ ràng.
- File bị khóa: retry, không tạo snapshot thiếu file.
- Archive hỏng hoặc sai hash: không restore và không thay đổi local.
- Local path không ghi được: dừng trước khi thay đổi dữ liệu.
- Watcher overflow: đánh dấu dirty và quét toàn bộ thư mục.
- App crash: file tạm nằm trong thư mục phiên và được dọn ở lần chạy sau.

Không thể bảo đảm giữ mọi thay đổi ngay tại khoảnh khắc mất điện. Cửa sổ mất dữ liệu tối thiểu gồm thời gian debounce, thời gian chờ file ổn định và thời gian upload. UI phải hiển thị rõ thời điểm backup cloud thành công gần nhất; chỉ trạng thái đó mới được coi là đã an toàn.

## 13. Kiến trúc mã nguồn

- `AutoSaveGame.App`: WPF views, view models, tray và composition root.
- `AutoSaveGame.Core`: domain models, state machine, interfaces và use cases.
- `AutoSaveGame.Infrastructure`: Google OAuth/Drive, filesystem, ZIP, hashing và local runtime state.
- `AutoSaveGame.Tests`: unit tests cho Core và các adapter filesystem có thể chạy bằng thư mục tạm.

Core không phụ thuộc WPF hoặc Google SDK. Mọi thao tác phá hủy local/cloud đi qua interface có kết quả tường minh để có thể kiểm thử các điểm lỗi giữa chừng.

## 14. Kiểm thử và tiêu chí hoàn thành MVP

Unit tests phải bao phủ:

- Chuẩn hóa và mở rộng path template.
- Debounce, retry và state transition.
- Hash ổn định, không phụ thuộc thứ tự enumerate.
- Không upload khi nội dung không đổi.
- Commit snapshot không xóa bản cũ khi upload/catalog thất bại.
- Phát hiện catalog conflict.
- Chặn archive path traversal.
- Rollback restore khi copy hoặc verify thất bại.

Integration tests dùng fake Drive và thư mục tạm phải chứng minh:

- Thay đổi nhiều file liên tiếp chỉ tạo một snapshot hoàn chỉnh.
- Sự kiện watcher bị bỏ lỡ được periodic scan phát hiện.
- Restore trên username/path khác vẫn dùng đúng path template.
- Mất kết nối tại từng bước commit không làm mất snapshot cloud cuối cùng đã xác nhận.

Smoke test Windows phải chứng minh executable self-contained mở được trên máy không cần .NET SDK, đăng nhập Google bằng OAuth client cấu hình cho môi trường test, backup một thư mục mẫu, xóa local và restore đúng hash.

## 15. Ngoài phạm vi và hướng mở rộng

Sau MVP có thể bổ sung incremental chunking cho save rất lớn, nhận diện save path theo Steam App ID, nút `Restore & Play`, mã hóa đầu cuối bằng passphrase, lịch sử nhiều phiên bản và backend metadata riêng. Các tính năng này không được đưa vào MVP khi chưa có bằng chứng cần thiết.
