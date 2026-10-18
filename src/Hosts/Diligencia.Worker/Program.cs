using Hangfire;
using Diligencia.BuildingBlocks.Diagnostics;
using Diligencia.BuildingBlocks.Persistence;
using Diligencia.Worker;
using Diligencia.Modules.Aprobaciones;
using Diligencia.Modules.Auditoria;
using Diligencia.Modules.Casos;
using Diligencia.Modules.Documentos;
using Diligencia.Modules.Identidad;
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
    .AddDiligenciaHangfire(config);

builder.Services.AddScoped<IDiagnosticJob, DiagnosticJob>();
builder.Services.AddHangfireServer();

var app = builder.Build();
app.MapDefaultEndpoints();
app.Run();

public partial class Program;
