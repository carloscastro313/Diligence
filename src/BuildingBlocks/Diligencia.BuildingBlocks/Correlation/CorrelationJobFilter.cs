using Hangfire.Client;
using Hangfire.Server;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Diligencia.BuildingBlocks.Correlation;

/// <summary>
/// Propaga el id de correlación API → Hangfire → worker como parámetro del job (R11).
/// No hereda de JobFilterAttribute: con AllowMultiple=false Hangfire descarta instancias repetidas del tipo
/// (global y estático), lo que en un mismo proceso con dos hosts (tests) dejaría el scope en el logger equivocado.
/// </summary>
public sealed class CorrelationJobFilter(ILogger<CorrelationJobFilter> logger, IHostEnvironment env)
    : IClientFilter, IServerFilter
{
    public const string ParameterName = "CorrelationId";
    private const string ScopeKey = "Diligencia.CorrelationScope";

    public void OnCreating(CreatingContext context) =>
        context.SetJobParameter(ParameterName, CorrelationContext.Current);

    public void OnCreated(CreatedContext context) { }

    public void OnPerforming(PerformingContext context)
    {
        var id = context.GetJobParameter<string>(ParameterName);
        if (CorrelationContext.IsValid(id)) CorrelationContext.Set(id!);
        context.Items[ScopeKey] = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = CorrelationContext.Current,
            ["Service"] = env.ApplicationName,
        });
    }

    public void OnPerformed(PerformedContext context)
    {
        (context.Items[ScopeKey] as IDisposable)?.Dispose();
        CorrelationContext.Clear();
    }
}
