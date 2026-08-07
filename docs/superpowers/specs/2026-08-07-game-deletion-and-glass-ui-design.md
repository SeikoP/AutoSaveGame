# Xóa game và làm mới giao diện glassmorphism

## Mục tiêu

Khi người dùng xóa dữ liệu của một game, ứng dụng phải xóa cả cấu hình game trong catalog Google Drive, các archive backup liên quan, và card game khỏi giao diện. Giao diện WPF được làm mới theo phong cách glassmorphism đậm nhưng giữ nguyên phạm vi và luồng thao tác hiện có.

## Phạm vi xóa

Một thao tác nguy hiểm duy nhất, đặt tại trang chi tiết game với nhãn **Xóa game và dữ liệu Drive**, sẽ:

1. Yêu cầu người dùng xác nhận.
2. Dừng watcher và hủy đăng ký lịch backup của game.
3. Xóa archive backup của game trên Google Drive.
4. Xóa game khỏi catalog Google Drive.
5. Cập nhật runtime và collection `Games`; nếu game đang được chọn, quay về tổng quan để card không còn hiển thị.

Không xóa thư mục save của game trên máy. Cũng không xóa dữ liệu vận hành của ứng dụng, như phiên OAuth trong bộ nhớ hoặc log chẩn đoán theo phiên. Khi việc xóa Drive thất bại hoặc bị xung đột, giữ card và lựa chọn game hiện tại, hiển thị lỗi an toàn, không báo thành công một phần.

## Kiến trúc và luồng dữ liệu

`MainViewModel` sẽ điều phối xác nhận, gọi một API runtime có ngữ nghĩa xóa trọn vẹn, sau đó phản ánh thay đổi qua sự kiện `GamesChanged`. `ApplicationRuntime` chịu trách nhiệm tuần tự hóa thao tác, dừng watcher/scheduler, xóa cloud data và cập nhật catalog/runtime games. View chỉ binding command/trạng thái; không chứa logic xóa.

Các test ViewModel kiểm tra: từ chối xác nhận không gọi runtime; xóa thành công gọi đúng luồng, bỏ chọn game và card biến mất; lỗi giữ nguyên card. Các test runtime kiểm tra catalog/archive và đăng ký watcher/scheduler được xử lý theo luồng đã chọn.

## Hệ thống giao diện

- Nền: gradient xanh đen–tím, điểm sáng mờ có độ tương phản vừa phải.
- Surface: các panel/card là kính tối bán trong suốt, viền mảnh, bo góc nhất quán và bóng nhẹ.
- Header: nhận diện AutoSaveGame, trạng thái đăng nhập và nút thêm game rõ ràng.
- Tổng quan: card game có tên, đường dẫn, badge trạng thái, thông tin backup và hành động chính dễ quét.
- Chi tiết: thông tin local save, trạng thái Drive và hành động được nhóm rõ ràng; thao tác xóa mang màu đỏ và icon/copy cảnh báo.
- Các trạng thái empty, loading, progress và error dùng cùng token màu/chữ/khoảng cách để không tạo vùng trống hoặc nội dung chồng lấn.

UI không thêm dashboard số liệu, tìm kiếm hay lọc game. Luồng thêm, sửa, backup, restore, bật/tắt theo dõi và sign-in/out được giữ nguyên.

## Tiêu chí chấp nhận

- Sau xác nhận xóa thành công, archive Drive và mục catalog của game không còn; card biến mất không cần khởi động lại.
- Thất bại/xung đột không che giấu game khỏi người dùng và thông báo lỗi không làm lộ chi tiết nhạy cảm.
- Không thao tác lên thư mục save cục bộ của game hoặc dữ liệu vận hành của ứng dụng.
- Giao diện tổng quan, chi tiết và mọi trạng thái phụ đều dùng hệ glassmorphism tối nhất quán, vẫn dễ đọc và sử dụng bằng chuột/bàn phím.
- Test mới được viết theo red-green và toàn bộ test/build liên quan chạy thành công.
