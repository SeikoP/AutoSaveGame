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
        notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "AutoSaveGame",
            Visible = true,
        };
        notifyIcon.DoubleClick += (_, _) => ShowWindow();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowWindow());
        menu.Items.Add("Exit", null, async (_, _) => await exit());
        notifyIcon.ContextMenuStrip = menu;
    }

    public void HideToTray()
    {
        window.Hide();
        notifyIcon.ShowBalloonTip(
            1500,
            "AutoSaveGame is watching",
            "Keep the app running while you play.",
            Forms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
    }

    private void ShowWindow()
    {
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
    }
}

