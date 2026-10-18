namespace Diligencia.BuildingBlocks.Diagnostics;

/// <summary>Job de diagnóstico que Hangfire resuelve desde DI.</summary>
public interface IDiagnosticJob
{
    Task RunAsync();
}
