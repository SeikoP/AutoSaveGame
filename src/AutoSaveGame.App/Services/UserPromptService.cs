using System.Windows;
using WpfMessageBox = System.Windows.MessageBox;

namespace AutoSaveGame.App.Services;

public sealed class UserPromptService : IUserPromptService
{
    public Task ShowPublicComputerWarningAsync()
    {
        WpfMessageBox.Show(
            "This is a public computer. Use a Guest/Private browser window for Google sign-in, then close that browser window when authorization finishes.",
            "Public computer safety",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return Task.CompletedTask;
    }

    public Task<bool> ConfirmGameClosedAsync(string displayName) =>
        Task.FromResult(
            WpfMessageBox.Show(
                $"Close {displayName} before restoring. Continue?",
                "Restore save",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes);

    public Task<bool> ConfirmDeleteAsync(string displayName) =>
        Task.FromResult(
            WpfMessageBox.Show(
                $"Remove {displayName} from AutoSaveGame? The current Drive snapshot is kept until a later cleanup.",
                "Remove game",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes);

    public Task<ExitChoice> ConfirmExitAsync()
    {
        var result = WpfMessageBox.Show(
            "Some games are not safely backed up.\n\nYes: backup and exit\nNo: exit anyway\nCancel: stay open",
            "Unsafe changes",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
        return Task.FromResult(result switch
        {
            MessageBoxResult.Yes => ExitChoice.BackupAndExit,
            MessageBoxResult.No => ExitChoice.ExitAnyway,
            _ => ExitChoice.Cancel,
        });
    }

    public void ShowError(string title, string message, string? correlationId)
    {
        var dialog = new Views.ErrorDialog(title, message, correlationId);
        dialog.ShowDialog();
    }
}
