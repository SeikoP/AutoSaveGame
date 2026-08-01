using System.Reflection;
using System.Windows;

namespace AutoSaveGame.App.Views;

public partial class ErrorDialog : Window
{
    private readonly string diagnosticDetails;

    public ErrorDialog(string title, string message, string? correlationId)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        CorrelationText.Text = string.IsNullOrWhiteSpace(correlationId)
            ? string.Empty
            : $"Diagnostic ID: {correlationId}";
        CopyButton.Visibility = string.IsNullOrWhiteSpace(correlationId)
            ? Visibility.Collapsed
            : Visibility.Visible;
        diagnosticDetails = string.Join(
            Environment.NewLine,
            $"Application: AutoSaveGame {Assembly.GetExecutingAssembly().GetName().Version}",
            $"Category: {title}",
            $"CorrelationId: {correlationId}");
    }

    private void Copy_Click(object sender, RoutedEventArgs e) =>
        System.Windows.Clipboard.SetText(diagnosticDetails);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
