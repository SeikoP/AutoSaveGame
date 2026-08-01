using AutoSaveGame.App.Services;

namespace AutoSaveGame.App.Tests.Services;

public sealed class SingleInstanceCoordinatorTests
{
    [Fact]
    public void TryAcquire_AllowsOnlyOneOwnerForTheSameName()
    {
        var name = $"AutoSaveGame.Tests.{Guid.NewGuid():N}";
        using var first = new SingleInstanceCoordinator(name);
        using var second = new SingleInstanceCoordinator(name);

        Assert.True(first.TryAcquire());
        Assert.False(second.TryAcquire());
    }
}
