namespace AutoSaveGame.Core.Models;

public sealed record CloudTransferProgress(
    long BytesTransferred,
    long? TotalBytes);
