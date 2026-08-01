using AutoSaveGame.App.Services;
using AutoSaveGame.Core.Models;

namespace AutoSaveGame.App.Tests.Services;

public sealed class VietnameseTextTests
{
    [Fact]
    public void FormatBytes_UsesVietnameseDecimalSeparator()
    {
        Assert.Equal("1,5 MB", VietnameseText.FormatBytes(1_572_864));
    }

    [Fact]
    public void OperationStage_UsesVietnameseCopy()
    {
        Assert.Equal(
            "Đang tải lên Google Drive",
            VietnameseText.OperationStage(OperationStage.UploadingArchive));
    }
}
