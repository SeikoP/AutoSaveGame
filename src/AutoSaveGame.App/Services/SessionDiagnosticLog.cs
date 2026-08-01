using System.IO;
using System.Text.RegularExpressions;

namespace AutoSaveGame.App.Services;

public sealed partial class SessionDiagnosticLog
{
    private readonly string rootDirectory;

    public SessionDiagnosticLog(string? rootDirectory = null)
    {
        this.rootDirectory = rootDirectory ?? Path.Combine(
            Path.GetTempPath(),
            "AutoSaveGame",
            "logs");
    }

    public string Write(Exception exception, string operation)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        Directory.CreateDirectory(rootDirectory);
        var correlationId = Guid.NewGuid().ToString("N")[..12];
        var content = string.Join(
            Environment.NewLine,
            $"TimestampUtc: {DateTimeOffset.UtcNow:O}",
            $"CorrelationId: {correlationId}",
            $"Operation: {operation}",
            $"ExceptionType: {exception.GetType().FullName}",
            $"Details: {Redact(exception.ToString())}");
        File.WriteAllText(
            Path.Combine(rootDirectory, $"error-{correlationId}.log"),
            content);
        return correlationId;
    }

    private static string Redact(string value)
    {
        var redacted = SecretValuePattern().Replace(value, "$1=[REDACTED]");
        return AuthorizationPattern().Replace(redacted, "$1 [REDACTED]");
    }

    [GeneratedRegex(
        "(?i)(access_token|refresh_token|client_secret|code)\\s*[=:]\\s*[^&\\s,\\\"']+")]
    private static partial Regex SecretValuePattern();

    [GeneratedRegex("(?i)(authorization|bearer)\\s+[^\\s,\\\"']+")]
    private static partial Regex AuthorizationPattern();
}
