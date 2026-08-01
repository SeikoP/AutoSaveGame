using System.Globalization;
using AutoSaveGame.Core.Models;

namespace AutoSaveGame.App.Services;

public static class VietnameseText
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("vi-VN");

    public static string FormatBytes(long bytes)
    {
        string unit;
        double value;
        if (bytes >= 1024L * 1024 * 1024)
        {
            unit = "GB";
            value = bytes / (1024d * 1024 * 1024);
        }
        else if (bytes >= 1024L * 1024)
        {
            unit = "MB";
            value = bytes / (1024d * 1024);
        }
        else if (bytes >= 1024)
        {
            unit = "KB";
            value = bytes / 1024d;
        }
        else
        {
            return $"{bytes.ToString("N0", Culture)} B";
        }

        return $"{value.ToString("0.#", Culture)} {unit}";
    }

    public static string FormatDateTime(DateTimeOffset value) =>
        value.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Culture);

    public static string OperationStage(OperationStage stage) => stage switch
    {
        Core.Models.OperationStage.Scanning => "Đang quét dữ liệu save",
        Core.Models.OperationStage.BuildingArchive => "Đang tạo bản sao lưu",
        Core.Models.OperationStage.Hashing => "Đang kiểm tra dữ liệu",
        Core.Models.OperationStage.CheckingCloud => "Đang kiểm tra Google Drive",
        Core.Models.OperationStage.UploadingArchive => "Đang tải lên Google Drive",
        Core.Models.OperationStage.CommittingCatalog => "Đang xác nhận bản sao lưu",
        Core.Models.OperationStage.CleaningUp => "Đang dọn dữ liệu cũ",
        Core.Models.OperationStage.DownloadingArchive => "Đang tải bản sao lưu",
        Core.Models.OperationStage.VerifyingArchive => "Đang xác minh bản sao lưu",
        Core.Models.OperationStage.RestoringFiles => "Đang khôi phục file save",
        Core.Models.OperationStage.Completed => "Hoàn tất",
        _ => "Đang xử lý",
    };

    public static string GameStatus(GameSyncStatus status, bool hasSnapshot) => status switch
    {
        GameSyncStatus.NotConfigured => "Chọn thư mục save",
        GameSyncStatus.Watching when !hasSnapshot => "Đang chờ bản sao lưu đầu tiên",
        GameSyncStatus.Watching => "Đã an toàn trên Google Drive",
        GameSyncStatus.Dirty => "Đã phát hiện thay đổi",
        GameSyncStatus.BackingUp => "Đang sao lưu",
        GameSyncStatus.Pending => "Đang chờ sao lưu",
        GameSyncStatus.Restoring => "Đang khôi phục",
        GameSyncStatus.Conflict => "Cần xử lý xung đột",
        GameSyncStatus.Error => "Cần kiểm tra",
        _ => "Không xác định",
    };
}
