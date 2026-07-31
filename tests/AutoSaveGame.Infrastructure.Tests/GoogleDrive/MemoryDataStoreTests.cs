using AutoSaveGame.Infrastructure.GoogleDrive;
using Google.Apis.Auth.OAuth2.Responses;

namespace AutoSaveGame.Infrastructure.Tests.GoogleDrive;

public sealed class MemoryDataStoreTests
{
    [Fact]
    public async Task ClearAsync_RemovesRefreshTokenFromMemory()
    {
        var store = new MemoryDataStore();
        await store.StoreAsync(
            "user",
            new TokenResponse
            {
                AccessToken = "access",
                RefreshToken = "sensitive",
            });

        await store.ClearAsync();

        Assert.Null(await store.GetAsync<TokenResponse>("user"));
    }

    [Fact]
    public async Task DeleteAsync_RemovesOnlyTheRequestedTypeAndKey()
    {
        var store = new MemoryDataStore();
        await store.StoreAsync("same-key", new TokenResponse { AccessToken = "token" });
        await store.StoreAsync("same-key", "other-value");

        await store.DeleteAsync<TokenResponse>("same-key");

        Assert.Null(await store.GetAsync<TokenResponse>("same-key"));
        Assert.Equal("other-value", await store.GetAsync<string>("same-key"));
    }
}

