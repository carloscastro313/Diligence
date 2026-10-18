using Diligencia.Api.Diagnostics;
using Diligencia.BuildingBlocks.Correlation;
using Diligencia.BuildingBlocks.Http;
using Diligencia.BuildingBlocks.Persistence;
using Microsoft.AspNetCore.HttpLogging;
using Diligencia.Modules.Aprobaciones;
using Diligencia.Modules.Auditoria;
using Diligencia.Modules.Casos;
using Diligencia.Modules.Documentos;
using Diligencia.Modules.Identidad;
using Diligencia.Modules.Identidad.Contracts;
using Diligencia.Modules.Riesgo;
using Diligencia.Modules.Terceros;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var config = builder.Configuration;
builder.Services
    .AddTercerosModule(config)
    .AddCasosModule(config)
    .AddRiesgoModule(config)
    .AddAprobacionesModule(config)
    .AddDocumentosModule(config)
    .AddAuditoriaModule(config)
    .AddIdentidadModule(config)
    .AddIdentidadAuthentication(config)
    .AddDiligenciaHangfire(config);

// Solo método, ruta, status y duración: nunca headers, query string ni cuerpos (FR-014).
builder.Services.AddHttpLogging(o =>
{
    o.LoggingFields = HttpLoggingFields.RequestMethod | HttpLoggingFields.RequestPath
        | HttpLoggingFields.ResponseStatusCode | HttpLoggingFields.Duration;
    o.CombineLogs = true;
});
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
    ctx.ProblemDetails.Extensions["correlationId"] = CorrelationContext.Current);

var app = builder.Build();

// La correlación va primero para que hasta los logs de errores lleven el id (FR-013, SC-004).
app.UseMiddleware<CorrelationMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseHttpLogging();

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

var surfaces = new ApiSurfaces(
    app.MapGroup("/api/backoffice").RequireAuthorization(AuthPolicies.Backoffice),
    app.MapGroup("/api/portal").RequireAuthorization(AuthPolicies.Portal));

surfaces
    .MapTercerosEndpoints()
    .MapCasosEndpoints()
    .MapRiesgoEndpoints()
    .MapAprobacionesEndpoints()
    .MapDocumentosEndpoints()
    .MapAuditoriaEndpoints()
    .MapIdentidadEndpoints();

if (config.GetValue<bool>("Diagnostics:Enabled"))
    surfaces.MapDiagnosticsEndpoints();

app.MapDefaultEndpoints();
app.Run();

public partial class Program;
