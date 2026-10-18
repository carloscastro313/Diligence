using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Diligencia.BuildingBlocks.Correlation;

/// <summary>
/// Primer middleware: toma <c>X-Correlation-Id</c> si es válido (si no genera uno), lo devuelve en toda respuesta
/// y abre un scope de log <c>{ CorrelationId, Service }</c> (FR-013, R11).
/// </summary>
public sealed class CorrelationMiddleware(RequestDelegate next, ILogger<CorrelationMiddleware> logger, IHostEnvironment env)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].FirstOrDefault();
        var id = CorrelationContext.IsValid(incoming)
            ? incoming!
            : Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");

        CorrelationContext.Set(id);
        // OnStarting: el manejador de excepciones limpia los headers de la respuesta, esto corre después.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = id;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id, ["Service"] = env.ApplicationName }))
            await next(context);
    }
}
