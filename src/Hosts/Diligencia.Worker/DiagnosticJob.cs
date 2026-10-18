using Diligencia.BuildingBlocks.Diagnostics;

namespace Diligencia.Worker;

/// <summary>Job idempotente y sin estado: solo registra su ejecución.</summary>
internal sealed class DiagnosticJob(ILogger<DiagnosticJob> logger) : IDiagnosticJob
{
    public Task RunAsync()
    {
        logger.LogInformation("Diagnostic job executed");
        return Task.CompletedTask;
    }
}
