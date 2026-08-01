using System.IO;
using AutoSaveGame.Core.Services;
using AutoSaveGame.Infrastructure.GoogleDrive;
using AutoSaveGame.Infrastructure.Restore;
using AutoSaveGame.Infrastructure.Snapshots;
using AutoSaveGame.Infrastructure.Watching;
using System.Reflection;

namespace AutoSaveGame.App.Services;

internal static class ApplicationRuntimeFactory
{
    public static IApplicationRuntime Create()
    {
        var pathTemplates = new PathTemplateService(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["USERPROFILE"] = Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile),
                ["APPDATA"] = Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                ["LOCALAPPDATA"] = Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                ["PROGRAMDATA"] = Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonApplicationData),
            });

        GoogleOAuthOptions options;
        try
        {
            options = GoogleOAuthOptions.Resolve(
                Environment.GetEnvironmentVariable,
                () => Assembly.GetExecutingAssembly().GetManifestResourceStream(
                    "AutoSaveGame.GoogleOAuthClient.json"));
        }
        catch (InvalidOperationException exception)
        {
            return new UnavailableApplicationRuntime(
                $"{exception.Message} See docs/google-oauth-setup.md.");
        }

        var session = new GoogleUserSession(options);
        var gateway = new GoogleDriveGateway(() => session.CurrentDriveService);
        var cloud = new GoogleDriveObjectStore(gateway);
        var codec = new CatalogCodec();
        var catalogs = new CatalogRepository(
            cloud,
            codec,
            new CatalogSelector(codec),
            TimeProvider.System);
        var snapshotArchive = new ZipSnapshotArchive();
        var restore = new RestoreService(
            snapshotArchive,
            new RestoreFileOperations());
        var backup = new BackupService(
            snapshotArchive,
            catalogs,
            pathTemplates,
            Guid.NewGuid(),
            Path.Combine(
                Path.GetTempPath(),
                "AutoSaveGame",
                $"session-{Environment.ProcessId}"));
        var bridge = new RuntimeBackupBridge();
        var scheduler = new DebouncedBackupScheduler(
            bridge.InvokeAsync,
            TimeProvider.System,
            TimeSpan.FromSeconds(3));
        var watcher = new GameDirectoryWatcher(
            scheduler,
            pathTemplates,
            TimeProvider.System);
        var runtime = new ApplicationRuntime(
            session,
            catalogs,
            cloud,
            restore,
            scheduler,
            watcher,
            pathTemplates,
            backup.BackupAsync);
        bridge.Runtime = runtime;
        return runtime;
    }
}
