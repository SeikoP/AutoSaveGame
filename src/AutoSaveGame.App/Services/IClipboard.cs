namespace AutoSaveGame.App.Services;

public interface IClipboard
{
    void SetText(string text);
}

public sealed class WindowsClipboard : IClipboard
{
    public void SetText(string text) => System.Windows.Clipboard.SetText(text);
}
