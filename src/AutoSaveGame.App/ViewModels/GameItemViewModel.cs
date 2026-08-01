using System.ComponentModel;
using System.Runtime.CompilerServices;
using AutoSaveGame.App.Services;
using AutoSaveGame.Core.Models;

namespace AutoSaveGame.App.ViewModels;

public sealed class GameItemViewModel : INotifyPropertyChanged
{
    private readonly RuntimeGame runtimeGame;
    private readonly IUiDispatcher uiDispatcher;

    public GameItemViewModel(
        RuntimeGame runtimeGame,
        IUiDispatcher? uiDispatcher = null)
    {
        this.runtimeGame = runtimeGame;
        this.uiDispatcher = uiDispatcher ?? new ImmediateUiDispatcher();
        runtimeGame.StateMachine.StatusChanged += OnStatusChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid GameId => runtimeGame.Config.GameId;

    public string DisplayName => runtimeGame.Config.DisplayName;

    public string PathTemplate => runtimeGame.Config.PathTemplate;

    public string LocalPath => runtimeGame.LocalPath;

    public bool WatchEnabled => runtimeGame.Config.WatchEnabled;

    public GameSyncStatus Status => runtimeGame.StateMachine.Status;

    public string StatusText => VietnameseText.GameStatus(Status, CanRestore);

    public bool CanRestore => runtimeGame.Config.Snapshot is not null;

    public bool NeedsSaveFolder => Status == GameSyncStatus.NotConfigured;

    public DateTimeOffset? LastBackupUtc =>
        runtimeGame.Config.Snapshot?.LastBackupUtc;

    public long ArchiveSize => runtimeGame.Config.Snapshot?.ArchiveSize ?? 0;

    public string? ArchiveFileId => runtimeGame.Config.Snapshot?.ArchiveFileId;

    public string? ArchiveSha256 => runtimeGame.Config.Snapshot?.ArchiveSha256;

    public string ArchiveSizeText => VietnameseText.FormatBytes(ArchiveSize);

    public string ArchiveFileIdDisplay => ArchiveFileId ?? "Chưa có file Drive";

    public string ArchiveSha256Display => ArchiveSha256 is null
        ? "Chưa có checksum"
        : ArchiveSha256.Length <= 16
            ? ArchiveSha256
            : $"{ArchiveSha256[..16]}...";

    public string LastBackupText => LastBackupUtc is null
        ? "Chưa sao lưu"
        : VietnameseText.FormatDateTime(LastBackupUtc.Value);

    public string LastBackupDisplayText => $"Lần sao lưu gần nhất: {LastBackupText}";

    private void OnStatusChanged(object? sender, GameSyncStatus status)
    {
        if (!uiDispatcher.CheckAccess())
        {
            uiDispatcher.Post(() => NotifyStatusChanged());
            return;
        }

        NotifyStatusChanged();
    }

    private void NotifyStatusChanged()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(NeedsSaveFolder));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
