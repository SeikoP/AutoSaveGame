using AutoSaveGame.Core.Models;

namespace AutoSaveGame.Core.Abstractions;

public interface IRestoreService
{
    Task<RestoreResult> RestoreAsync(
        Stream cloudArchive,
        string expectedArchiveSha256,
        string expectedContentSha256,
        string targetDirectory,
        CancellationToken cancellationToken);
}

