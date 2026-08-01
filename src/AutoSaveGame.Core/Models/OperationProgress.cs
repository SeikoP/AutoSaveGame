namespace AutoSaveGame.Core.Models;

public enum OperationKind
{
    Backup,
    Restore,
    RefreshStorage,
    CleanStorage,
}

public enum OperationStage
{
    Scanning,
    BuildingArchive,
    Hashing,
    CheckingCloud,
    UploadingArchive,
    CommittingCatalog,
    CleaningUp,
    DownloadingArchive,
    VerifyingArchive,
    RestoringFiles,
    Completed,
}

public enum OperationOutcome
{
    Running,
    Succeeded,
    Failed,
    Canceled,
    Conflict,
}

public sealed record OperationProgress(
    Guid OperationId,
    Guid? GameId,
    OperationKind Kind,
    OperationStage Stage,
    long BytesCompleted,
    long? TotalBytes,
    TimeSpan Elapsed,
    string? Detail,
    OperationOutcome Outcome)
{
    public double? Percent => TotalBytes is > 0
        ? Math.Clamp(BytesCompleted * 100d / TotalBytes.Value, 0d, 100d)
        : null;
}
