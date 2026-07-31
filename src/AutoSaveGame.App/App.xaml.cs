using AutoSaveGame.App.Services;

namespace AutoSaveGame.App;

public partial class App : System.Windows.Application
{
    private IApplicationRuntime? runtime;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        runtime = ApplicationRuntimeFactory.Create();
        var window = new MainWindow(runtime, new UserPromptService());
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        runtime?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnExit(e);
    }
}
