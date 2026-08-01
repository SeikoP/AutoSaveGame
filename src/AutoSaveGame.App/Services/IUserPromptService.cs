namespace AutoSaveGame.App.Services;

public enum ExitChoice
{
    BackupAndExit,
    ExitAnyway,
    Cancel,
}

public interface IUserPromptService
{
    Task ShowPublicComputerWarningAsync();

    Task<bool> ConfirmGameClosedAsync(string displayName);

    Task<bool> ConfirmDeleteAsync(string displayName);

    Task<ExitChoice> ConfirmExitAsync();

    void ShowError(string title, string message, string? correlationId);
}
