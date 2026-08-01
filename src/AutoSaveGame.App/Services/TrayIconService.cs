using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace AutoSaveGame.App.Services;

internal sealed class TrayIconService : IDisposable
{
    private readonly Window window;
    private readonly Forms.NotifyIcon notifyIcon;

    public TrayIconService(Window window, Func<Task> exit)
    {
        this.window = window;
        var executableIcon = Environment.ProcessPath is { } processPath
            ? Icon.ExtractAssociatedIcon(processPath)
            : null;
        notifyIcon = new Forms.NotifyIcon
        {
            Icon = executableIcon ?? SystemIcons.Application,
            Text = "AutoSaveGame",
            Visible = true,
        };
        notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
            {
                ToggleWindow();
            }
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Mở AutoSaveGame", null, (_, _) => ShowWindow());
        menu.Items.Add("Thoát", null, async (_, _) => await exit());
        notifyIcon.ContextMenuStrip = menu;
    }

    public void HideToTray()
    {
        window.Hide();
        notifyIcon.ShowBalloonTip(
            1500,
            "AutoSaveGame đang theo dõi",
            "Hãy để ứng dụng chạy trong khi bạn chơi.",
            Forms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        notifyIcon.Visible = false;
        if (!ReferenceEquals(notifyIcon.Icon, SystemIcons.Application))
        {
            notifyIcon.Icon?.Dispose();
        }
        notifyIcon.Dispose();
    }

    private void ShowWindow()
    {
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
    }

    private void ToggleWindow()
    {
        if (window.IsVisible)
        {
            window.Hide();
            return;
        }

        ShowWindow();
    }
}
