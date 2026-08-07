using AutoSaveGame.App.Services;
using AutoSaveGame.App.Smoke;
using System.Security.Principal;
using System.Windows.Threading;

namespace AutoSaveGame.App;

public partial class App : System.Windows.Application
{
    private IApplicationRuntime? runtime;
    private SingleInstanceCoordinator? singleInstance;
    private readonly ApplicationExceptionHandler exceptionHandler = new(
        new SessionDiagnosticLog());

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

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

        var userIdentity = WindowsIdentity.GetCurrent().User?.Value
            ?? Environment.UserName;
        singleInstance = new SingleInstanceCoordinator(
            $"Local\\AutoSaveGame.{userIdentity}");
        if (!singleInstance.TryAcquire())
        {
            singleInstance.SignalPrimary();
            Shutdown();
            return;
        }

        runtime = ApplicationRuntimeFactory.Create();
        var window = new MainWindow(runtime, new UserPromptService());
        MainWindow = window;
        singleInstance.StartListening(() =>
            Dispatcher.BeginInvoke(window.RestoreFromTray));
        window.Show();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        try
        {
            runtime?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        finally
        {
            singleInstance?.Dispose();
            base.OnExit(e);
        }
    }

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = exceptionHandler.Handle(
            e.Exception,
            "Lỗi giao diện không xử lý");
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            exceptionHandler.Handle(exception, "Lỗi ứng dụng không xử lý");
        }
    }

    private void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        exceptionHandler.Handle(e.Exception, "Tác vụ nền không xử lý");
        e.SetObserved();
    }
}
