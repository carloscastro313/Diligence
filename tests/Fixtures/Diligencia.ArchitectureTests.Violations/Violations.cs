// Violaciones deliberadas para probar que las reglas de arquitectura las detectan (SC-003).
// Este ensamblado NO se referencia desde ningún proyecto de producción.
#pragma warning disable CA1822
namespace Violations.Modules.Alpha
{
    public sealed class AlphaModule;

    // R1: depende del interior de otro módulo.
    internal sealed class AlphaHandler
    {
        public object Use() => new Beta.BetaHandler();
    }

    // R3: tipo público fuera de Contracts y que no es <Módulo>Module.
    public sealed class AlphaLeak;

    // R6: entidad mapeada que no implementa ITimestamped.
    internal sealed class AlphaThing;

    internal sealed class AlphaDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<AlphaThing> Things => Set<AlphaThing>();
    }
}

namespace Violations.Modules.Beta
{
    public sealed class BetaModule;

    internal sealed class BetaHandler;

    // R4: usa el DbContext de otro módulo.
    internal sealed class BetaUsesAlphaDb
    {
        public object Use() => new Alpha.AlphaDbContext();
    }
}

namespace Violations.Modules.Beta.Contracts
{
    // R2: un Contracts depende de internos y de EF Core.
    public sealed class BetaDto
    {
        public object Use() => new Beta.BetaHandler();
        public Type Ef() => typeof(Microsoft.EntityFrameworkCore.DbContext);
    }
}

namespace Violations.Hosts.Api
{
    // R5: el API referencia al worker.
    internal sealed class ApiThing
    {
        public object Use() => new Worker.WorkerThing();
    }
}

namespace Violations.Hosts.Worker
{
    internal sealed class WorkerThing;
}

namespace Violations.BuildingBlocks
{
    // R5: BuildingBlocks referencia un módulo.
    internal sealed class BbThing
    {
        public object Use() => new Modules.Alpha.AlphaModule();
    }
}
