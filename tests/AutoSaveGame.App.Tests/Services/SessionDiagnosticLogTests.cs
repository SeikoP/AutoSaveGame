using AutoSaveGame.App.Services;

namespace AutoSaveGame.App.Tests.Services;

public sealed class SessionDiagnosticLogTests
{
    [Fact]
    public void Write_RedactsOAuthValues()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"AutoSaveGame-LogTest-{Guid.NewGuid():N}");
        try
        {
            var log = new SessionDiagnosticLog(root);
            var id = log.Write(
                new InvalidOperationException(
                    "access_token=value-abc&code=value-xyz client_secret=value-hidden refresh_token=value-refresh"),
                "Google sign-in");

            var text = File.ReadAllText(Directory.GetFiles(root).Single());
            Assert.Contains(id, text);
            Assert.Contains("Google sign-in", text);
            Assert.Contains(nameof(InvalidOperationException), text);
            Assert.DoesNotContain("value-abc", text);
            Assert.DoesNotContain("value-xyz", text);
            Assert.DoesNotContain("value-hidden", text);
            Assert.DoesNotContain("value-refresh", text);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
