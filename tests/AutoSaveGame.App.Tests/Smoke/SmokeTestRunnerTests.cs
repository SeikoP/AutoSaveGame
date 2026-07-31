using AutoSaveGame.App.Smoke;

namespace AutoSaveGame.App.Tests.Smoke;

public sealed class SmokeTestRunnerTests
{
    [Fact]
    public async Task RunAsync_BacksUpDeletesAndRestoresMatchingSave()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"AutoSaveGame-Smoke-{Guid.NewGuid():N}");
        try
        {
            var exitCode = await SmokeTestRunner.RunAsync(
                root,
                TestContext.Current.CancellationToken);

            Assert.Equal(0, exitCode);
            Assert.Equal(
                "PASS",
                File.ReadAllText(Path.Combine(root, "smoke-result.txt")));
            Assert.Equal(
                "smoke-save-v1",
                File.ReadAllText(Path.Combine(root, "save", "slot.dat")));
            Assert.Equal(
                Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        "smoke-save-v1"u8.ToArray())),
                File.ReadAllText(Path.Combine(root, "expected-save.sha256")));
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
