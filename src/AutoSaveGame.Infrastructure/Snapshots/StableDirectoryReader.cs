namespace AutoSaveGame.Infrastructure.Snapshots;

internal sealed class StableDirectoryReader(TimeSpan stabilityDelay)
{
    public async Task<IReadOnlyList<StableFile>> CaptureAsync(
        string sourceDirectory,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Save directory does not exist: {sourceDirectory}");
        }

        var first = Enumerate(sourceDirectory);
        if (stabilityDelay > TimeSpan.Zero)
        {
            await Task.Delay(stabilityDelay, cancellationToken).ConfigureAwait(false);
        }

        var second = Enumerate(sourceDirectory);
        if (first.Count != second.Count
            || first.Where((file, index) => file != second[index]).Any())
        {
            throw new SnapshotNotStableException(
                "The save directory changed while preparing the snapshot.");
        }

        return second;
    }

    private static IReadOnlyList<StableFile> Enumerate(string sourceDirectory)
    {
        var root = Path.GetFullPath(sourceDirectory);
        var files = new List<StableFile>();
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(root);

        while (pendingDirectories.TryPop(out var directory))
        {
            var directoryInfo = new DirectoryInfo(directory);
            RejectReparsePoint(directoryInfo);

            foreach (var entry in directoryInfo.EnumerateFileSystemInfos())
            {
                RejectReparsePoint(entry);
                if (entry is DirectoryInfo childDirectory)
                {
                    pendingDirectories.Push(childDirectory.FullName);
                    continue;
                }

                if (entry is FileInfo file)
                {
                    files.Add(new StableFile(
                        file.FullName,
                        Path.GetRelativePath(root, file.FullName).Replace('\\', '/'),
                        file.Length,
                        file.LastWriteTimeUtc));
                }
            }
        }

        return files
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static void RejectReparsePoint(FileSystemInfo info)
    {
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                $"Reparse points are not allowed in save snapshots: {info.FullName}");
        }
    }
}

internal sealed record StableFile(
    string FullPath,
    string RelativePath,
    long Length,
    DateTime LastWriteTimeUtc);

