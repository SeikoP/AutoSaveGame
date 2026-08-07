namespace AutoSaveGame.App.Services;

public sealed class ApplicationExceptionHandler
{
    private readonly Action<Exception, string> writeDiagnostic;

    public ApplicationExceptionHandler(SessionDiagnosticLog diagnosticLog)
        : this((exception, operation) => diagnosticLog.Write(exception, operation))
    {
        ArgumentNullException.ThrowIfNull(diagnosticLog);
    }

    public ApplicationExceptionHandler(Action<Exception, string> writeDiagnostic)
    {
        this.writeDiagnostic = writeDiagnostic
            ?? throw new ArgumentNullException(nameof(writeDiagnostic));
    }

    public bool Handle(Exception exception, string operation)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        try
        {
            writeDiagnostic(exception, operation);
        }
        catch
        {
            // Diagnostics must never make an original application failure fatal.
        }

        return true;
    }
}
