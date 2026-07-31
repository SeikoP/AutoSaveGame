using AutoSaveGame.Infrastructure.Snapshots;

namespace AutoSaveGame.Infrastructure.Watching;

internal static class DirectoryFingerprint
{
    public static Task<string> ComputeAsync(
        string directory,
        CancellationToken cancellationToken) =>
        ContentHasher.ComputeDirectoryAsync(directory, cancellationToken);
}

