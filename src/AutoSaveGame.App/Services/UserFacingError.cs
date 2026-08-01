using AutoSaveGame.Infrastructure.GoogleDrive;

namespace AutoSaveGame.App.Services;

public sealed record UserFacingError(string Title, string Message)
{
    public static UserFacingError From(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is not UserAuthenticationException authentication)
        {
            return new UserFacingError(
                "AutoSaveGame chưa thể hoàn tất thao tác",
                "Hãy thử lại. Nếu lỗi tiếp tục, hãy sao chép mã chẩn đoán.");
        }

        var message = authentication.Kind switch
        {
            AuthenticationFailureKind.Canceled =>
                "Đăng nhập Google đã bị hủy. Bạn có thể thử lại khi sẵn sàng.",
            AuthenticationFailureKind.TimedOut =>
                "Google chưa phản hồi AutoSaveGame kịp thời. Hãy thử lại.",
            AuthenticationFailureKind.Network =>
                "Không thể kết nối tới Google. Hãy kiểm tra mạng và thử lại.",
            AuthenticationFailureKind.Rejected =>
                "Google đã từ chối yêu cầu đăng nhập. Hãy kiểm tra tài khoản và thử lại.",
            AuthenticationFailureKind.InvalidBuild =>
                "Đây không phải bản phát hành chính thức có thể sử dụng. Hãy tải bản GitHub Release mới nhất.",
            _ =>
                "Trình duyệt không thể trả kết quả đăng nhập về AutoSaveGame. Hãy thử lại.",
        };
        return new UserFacingError("Không thể đăng nhập Google", message);
    }
}
