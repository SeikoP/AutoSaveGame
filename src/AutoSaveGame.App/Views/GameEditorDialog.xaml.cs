using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;
using WpfMessageBox = System.Windows.MessageBox;

namespace AutoSaveGame.App.Views;

public partial class GameEditorDialog : Window
{
    public GameEditorDialog(string? gameName = null, string? savePath = null)
    {
        InitializeComponent();
        GameNameTextBox.Text = gameName ?? string.Empty;
        SavePathTextBox.Text = savePath ?? string.Empty;
    }

    public string GameName => GameNameTextBox.Text.Trim();

    public string SavePath => SavePathTextBox.Text.Trim();

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Select the game's save folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            SavePathTextBox.Text = dialog.SelectedPath;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GameName)
            || string.IsNullOrWhiteSpace(SavePath)
            || !Path.IsPathFullyQualified(SavePath))
        {
            WpfMessageBox.Show(
                "Enter a game name and an absolute save-folder path.",
                "Invalid configuration",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }
}
