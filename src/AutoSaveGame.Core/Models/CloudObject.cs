namespace AutoSaveGame.Core.Models;

public sealed record CloudObject(
    string FileId,
    string Name,
    long Size,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ModifiedUtc,
    string? Sha256Checksum = null,
    string? Md5Checksum = null);
