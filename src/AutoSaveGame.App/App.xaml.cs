using AutoSaveGame.App.Services;
using AutoSaveGame.App.Smoke;

namespace AutoSaveGame.App;

public partial class App : System.Windows.Application
{
    private IApplicationRuntime? runtime;

    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        if (e.Args.Length > 0
            && string.Equals(e.Args[0], "--smoke-test", StringComparison.Ordinal))
        {
            try
            {
                if (e.Args.Length != 2)
                {
                    throw new ArgumentException(
                        "Usage: AutoSaveGame.exe --smoke-test <temp-root>");
                }

                Environment.ExitCode = await SmokeTestRunner.RunAsync(
                    e.Args[1],
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                Environment.ExitCode = 1;
            }

            Shutdown(Environment.ExitCode);
            return;
        }

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
