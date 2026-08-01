using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using AutoSaveGame.App.Services;
using AutoSaveGame.App.ViewModels;
using AutoSaveGame.App.Views;

namespace AutoSaveGame.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private readonly TrayIconService trayIcon;
    private bool allowClose;

    public MainWindow(
        IApplicationRuntime runtime,
        IUserPromptService prompts)
    {
        InitializeComponent();
        viewModel = new MainViewModel(
            runtime,
            prompts,
            uiDispatcher: new WpfUiDispatcher(Dispatcher));
        DataContext = viewModel;
        trayIcon = new TrayIconService(this, ExitAsync);
    }

    public void RestoreFromTray()
    {
        AnchorToTray();
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private async void AddGame_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new GameEditorDialog { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            await viewModel.AddOrUpdateGameAsync(
                null,
                dialog.GameName,
                dialog.SavePath);
        }
    }

    private async void EditGame_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GameItemViewModel game)
        {
            return;
        }

        var dialog = new GameEditorDialog(game.DisplayName, game.LocalPath)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() == true)
        {
            await viewModel.AddOrUpdateGameAsync(
                game.GameId,
                dialog.GameName,
                dialog.SavePath);
        }
    }

    private async void Watch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox checkBox
            && checkBox.DataContext is GameItemViewModel game
            && checkBox.IsChecked is bool enabled)
        {
            await viewModel.SetWatchingAsync(game, enabled);
        }
    }

    private async void Exit_Click(object sender, RoutedEventArgs e) =>
        await ExitAsync();

    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();

    private void Window_Loaded(object sender, RoutedEventArgs e) => AnchorToTray();

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!OwnedWindows.Cast<Window>().Any(item => item.IsVisible))
        {
            Hide();
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private async Task ExitAsync()
    {
        if (!await viewModel.RequestExitAsync())
        {
            return;
        }

        allowClose = true;
        trayIcon.Dispose();
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            trayIcon.HideToTray();
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (allowClose)
        {
            return;
        }

        e.Cancel = true;
        trayIcon.HideToTray();
    }

    private void AnchorToTray()
    {
        var workArea = SystemParameters.WorkArea;
        Left = Math.Max(workArea.Left, workArea.Right - Width - 12);
        Top = Math.Max(workArea.Top, workArea.Bottom - Height - 12);
    }
}
