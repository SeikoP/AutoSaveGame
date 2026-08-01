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

    public string StatusText => Status switch
    {
        GameSyncStatus.NotConfigured => "Choose save folder",
        GameSyncStatus.Watching when runtimeGame.Config.Snapshot is null =>
            "Waiting for first backup",
        GameSyncStatus.Watching => "Safe in Google Drive",
        GameSyncStatus.Dirty => "Changes detected",
        GameSyncStatus.BackingUp or GameSyncStatus.Pending => "Backing up",
        GameSyncStatus.Restoring => "Restoring",
        GameSyncStatus.Conflict or GameSyncStatus.Error => "Action required",
        _ => Status.ToString(),
    };

    public bool CanRestore => runtimeGame.Config.Snapshot is not null;

    public bool NeedsSaveFolder => Status == GameSyncStatus.NotConfigured;

    public DateTimeOffset? LastBackupUtc =>
        runtimeGame.Config.Snapshot?.LastBackupUtc;

    public string LastBackupText => LastBackupUtc is null
        ? "Never backed up"
        : LastBackupUtc.Value.ToLocalTime().ToString("g");

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
