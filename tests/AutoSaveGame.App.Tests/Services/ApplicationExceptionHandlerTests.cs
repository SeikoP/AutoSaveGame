using AutoSaveGame.App.Services;

namespace AutoSaveGame.App.Tests.Services;

public sealed class ApplicationExceptionHandlerTests
{
    [Fact]
    public void Handle_ReturnsHandledWhenDiagnosticWriterFails()
    {
        var handler = new ApplicationExceptionHandler(
            (_, _) => throw new IOException("Temp folder is blocked."));

        var handled = handler.Handle(
            new InvalidOperationException("UI callback failed."),
            "Lỗi giao diện không xử lý");

        Assert.True(handled);
    }
}
