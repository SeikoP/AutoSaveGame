namespace AutoSaveGame.App.Tests.Views;

public sealed class VietnameseCompactUiTests
{
    [Fact]
    public void MainWindow_IsCompactTrayPopupWithVietnamesePrimaryCopy()
    {
        var markup = ReadRepositoryFile("src/AutoSaveGame.App/MainWindow.xaml");

        Assert.Contains("Width=\"360\"", markup);
        Assert.Contains("Height=\"480\"", markup);
        Assert.Contains("WindowStyle=\"None\"", markup);
        Assert.Contains("Đăng nhập bằng Google", markup);
        Assert.Contains("Dữ liệu ứng dụng trên Drive", markup);
        Assert.Contains(
            "Value=\"{Binding OperationPercent, Mode=OneWay}\"",
            markup);
        Assert.DoesNotContain("Sign in with Google", markup);
        Assert.DoesNotContain("Protected games", markup);
    }

    [Theory]
    [InlineData("src/AutoSaveGame.App/Views/GameEditorDialog.xaml", "Thư mục save")]
    [InlineData("src/AutoSaveGame.App/Views/ErrorDialog.xaml", "Sao chép mã chẩn đoán")]
    public void Dialogs_UseVietnameseCopy(string relativePath, string expected)
    {
        Assert.Contains(expected, ReadRepositoryFile(relativePath));
    }

    [Fact]
    public void WindowsBuild_UsesGeneratedApplicationIcon()
    {
        var project = ReadRepositoryFile(
            "src/AutoSaveGame.App/AutoSaveGame.App.csproj");
        var installer = ReadRepositoryFile("installer/AutoSaveGame.iss");

        Assert.Contains("<ApplicationIcon>Assets\\AutoSaveGame.ico</ApplicationIcon>", project);
        Assert.Contains("SetupIconFile=", installer);
        Assert.Contains("Languages\\Vietnamese.isl", installer);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "AutoSaveGame.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory.FullName, relativePath));
    }
}
