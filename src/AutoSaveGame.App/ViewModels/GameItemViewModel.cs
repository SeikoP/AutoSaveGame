using System.ComponentModel;
using System.Runtime.CompilerServices;
using AutoSaveGame.App.Services;
using AutoSaveGame.Core.Models;

namespace AutoSaveGame.App.ViewModels;

public sealed class GameItemViewModel : INotifyPropertyChanged
{
    private readonly RuntimeGame runtimeGame;

    public GameItemViewModel(RuntimeGame runtimeGame)
    {
        this.runtimeGame = runtimeGame;
        runtimeGame.StateMachine.StatusChanged += OnStatusChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid GameId => runtimeGame.Config.GameId;

    public string DisplayName => runtimeGame.Config.DisplayName;

    public string PathTemplate => runtimeGame.Config.PathTemplate;

    public string LocalPath => runtimeGame.LocalPath;

    public bool WatchEnabled => runtimeGame.Config.WatchEnabled;

    public GameSyncStatus Status => runtimeGame.StateMachine.Status;

    public DateTimeOffset? LastBackupUtc =>
        runtimeGame.Config.Snapshot?.LastBackupUtc;

    public string LastBackupText => LastBackupUtc is null
        ? "Never backed up"
        : LastBackupUtc.Value.ToLocalTime().ToString("g");

    private void OnStatusChanged(object? sender, GameSyncStatus status)
    {
        OnPropertyChanged(nameof(Status));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
