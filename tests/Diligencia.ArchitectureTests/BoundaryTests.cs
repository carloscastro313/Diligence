using System.Reflection;
using Diligencia.BuildingBlocks.Correlation;

namespace Diligencia.ArchitectureTests;

public class BoundaryTests
{
    private static readonly string[] ModuleNames = ["Terceros", "Casos", "Riesgo", "Aprobaciones", "Documentos", "Auditoria", "Identidad"];

    private static Assembly Load(string name) => Assembly.Load(name);

    // Los hosts no se referencian desde tests; se cargan por nombre si están en el directorio de salida.
    private static CodeGroup Host(string name)
    {
        var asm = Assembly.Load(name);
        return new CodeGroup(name, asm, null);
    }

    private static Scope Real() => new(
        ModuleNames.Select(n => new ModuleInfo(n,
            Load($"Diligencia.Modules.{n}"), $"Diligencia.Modules.{n}",
            Load($"Diligencia.Modules.{n}.Contracts"), $"Diligencia.Modules.{n}.Contracts")).ToList(),
        Host("Diligencia.Api"), Host("Diligencia.Worker"),
        new CodeGroup("BuildingBlocks", typeof(CorrelationContext).Assembly, null));

    private static Scope Violations()
    {
        var asm = typeof(Violations.Modules.Alpha.AlphaModule).Assembly;
        ModuleInfo M(string n) => new(n, asm, $"Violations.Modules.{n}", asm, $"Violations.Modules.{n}.Contracts");
        return new Scope([M("Alpha"), M("Beta")],
            new CodeGroup("Api", asm, "Violations.Hosts.Api"),
            new CodeGroup("Worker", asm, "Violations.Hosts.Worker"),
            new CodeGroup("BuildingBlocks", asm, "Violations.BuildingBlocks"));
    }

    // AuditEvent es inmutable (su fecha es OccurredAtUtc). UserRole es una fila de relación; Session y PasswordToken
    // son registros de eventos con CreatedAtUtc propio y se cierran con RevokedAtUtc/UsedAtUtc, no se "actualizan".
    private static readonly HashSet<string> Exempt = ["AuditEvent", "UserRole", "Session", "PasswordToken"];

    private static readonly (string Rule, Func<Scope, IReadOnlyList<string>> Run)[] Rules =
    [
        ("R1", ArchitectureRules.InterModuleReferences),
        ("R2", ArchitectureRules.ContractsPurity),
        ("R3", ArchitectureRules.OnlyModuleClassIsPublic),
        ("R4", ArchitectureRules.DbContextStaysInModule),
        ("R5", ArchitectureRules.HostAndBuildingBlocksIsolation),
        ("R6", sc => ArchitectureRules.EntitiesAreTimestamped(sc, Exempt)),
    ];

    [Fact]
    public void RealModules_RespectBoundaries()
    {
        var scope = Real();
        var violations = Rules.SelectMany(r => r.Run(scope)).ToList();
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void ViolationsAssembly_EveryRuleDetectsItsViolation()
    {
        var scope = Violations();
        foreach (var (rule, run) in Rules)
            Assert.True(run(scope).Count > 0, $"La regla {rule} no detectó su violación deliberada.");

        // R5 cubre las dos aristas sembradas.
        var r5 = ArchitectureRules.HostAndBuildingBlocksIsolation(scope);
        Assert.Contains(r5, v => v.Contains("ApiThing"));
        Assert.Contains(r5, v => v.Contains("BbThing"));
    }
}
