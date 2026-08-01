using System.Windows;
using WpfMessageBox = System.Windows.MessageBox;

namespace AutoSaveGame.App.Services;

public sealed class UserPromptService : IUserPromptService
{
    public Task ShowPublicComputerWarningAsync()
    {
        WpfMessageBox.Show(
            "Đây là máy tính công cộng. Hãy dùng cửa sổ Khách/Riêng tư để đăng nhập Google, sau đó đóng cửa sổ trình duyệt khi cấp quyền xong.",
            "An toàn trên máy tính công cộng",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return Task.CompletedTask;
    }

    public Task<bool> ConfirmGameClosedAsync(string displayName) =>
        Task.FromResult(
            WpfMessageBox.Show(
                $"Hãy đóng {displayName} trước khi khôi phục. Tiếp tục?",
                "Khôi phục save",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes);

    public Task<bool> ConfirmDeleteAsync(string displayName) =>
        Task.FromResult(
            WpfMessageBox.Show(
                $"Xóa {displayName} khỏi AutoSaveGame? Bản sao trên Drive sẽ được giữ lại cho tới lần dọn dẹp sau.",
                "Xóa game",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes);

    public Task<ExitChoice> ConfirmExitAsync()
    {
        var result = WpfMessageBox.Show(
            "Một số game chưa được sao lưu an toàn.\n\nCó: sao lưu rồi thoát\nKhông: vẫn thoát\nHủy: tiếp tục sử dụng",
            "Có thay đổi chưa an toàn",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
        return Task.FromResult(result switch
        {
            MessageBoxResult.Yes => ExitChoice.BackupAndExit,
            MessageBoxResult.No => ExitChoice.ExitAnyway,
            _ => ExitChoice.Cancel,
        });
    }

    public void ShowError(string title, string message, string? correlationId)
    {
        var dialog = new Views.ErrorDialog(title, message, correlationId);
        dialog.ShowDialog();
    }
}
