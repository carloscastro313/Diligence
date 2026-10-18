using Diligencia.BuildingBlocks.Diagnostics;
using Diligencia.BuildingBlocks.Http;
using Diligencia.Modules.Identidad.Contracts;
using Hangfire;

namespace Diligencia.Api.Diagnostics;

// Verifica la correlación API -> Hangfire -> worker (US3). Solo existe con Diagnostics:Enabled=true.
internal static class DiagnosticsEndpoints
{
    public static ApiSurfaces MapDiagnosticsEndpoints(this ApiSurfaces surfaces)
    {
        surfaces.Backoffice.MapPost("/diagnostics/jobs", (IBackgroundJobClient jobs) =>
            Results.Accepted(value: new { jobId = jobs.Enqueue<IDiagnosticJob>(j => j.RunAsync()) }))
            .RequireAuthorization(AuthPolicies.Administrator);
        return surfaces;
    }
}
