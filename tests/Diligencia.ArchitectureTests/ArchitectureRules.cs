using System.Reflection;
using NetArchTest.Rules;

namespace Diligencia.ArchitectureTests;

/// <summary>Un módulo: tipos internos (<see cref="ImplNamespace"/>) y su Contracts (<see cref="ContractsNamespace"/>).</summary>
public sealed record ModuleInfo(string Name, Assembly Impl, string ImplNamespace, Assembly Contracts, string ContractsNamespace)
{
    public IEnumerable<Type> ImplTypes => Impl.GetTypes().Where(t => InNs(t, ImplNamespace) && !InNs(t, ContractsNamespace));
    public IEnumerable<Type> ContractTypes => Contracts.GetTypes().Where(t => InNs(t, ContractsNamespace));

    private static bool InNs(Type t, string ns) => t.Namespace == ns || (t.Namespace?.StartsWith(ns + ".") ?? false);
}

/// <summary>Un grupo de código (host o BuildingBlocks); <see cref="Namespace"/> null = todo el ensamblado.</summary>
public sealed record CodeGroup(string Name, Assembly Assembly, string? Namespace);

public sealed record Scope(
    IReadOnlyList<ModuleInfo> Modules,
    CodeGroup Api,
    CodeGroup Worker,
    CodeGroup BuildingBlocks);

public static class ArchitectureRules
{
    private static readonly string[] EfCore = ["Microsoft.EntityFrameworkCore"];

    private static IEnumerable<Type> TypesOf(CodeGroup g) =>
        g.Assembly.GetTypes().Where(t => g.Namespace is null || t.Namespace == g.Namespace || (t.Namespace?.StartsWith(g.Namespace + ".") ?? false));

    // Tipos de `subjects` que dependen de alguno de `forbidden` (por nombre completo, incluye cuerpos de métodos).
    private static IEnumerable<string> Depends(Assembly asm, IEnumerable<Type> subjects, IEnumerable<string> forbidden)
    {
        var subjectNames = subjects.Select(t => t.FullName!).ToHashSet();
        var forbiddenArr = forbidden.ToArray();
        if (subjectNames.Count == 0 || forbiddenArr.Length == 0) return [];
        var result = Types.InAssembly(asm)
            .That().MeetCustomRule(new InSet(subjectNames))
            .ShouldNot().HaveDependencyOnAny(forbiddenArr)
            .GetResult();
        return result.FailingTypes?.Select(t => t.FullName!) ?? [];
    }

    private sealed class InSet(HashSet<string> names) : ICustomRule
    {
        public bool MeetsRule(Mono.Cecil.TypeDefinition type) => names.Contains(type.FullName.Replace('/', '+'));
    }

    // 1. Un módulo interno no referencia el interior de otro módulo.
    public static IReadOnlyList<string> InterModuleReferences(Scope s) => s.Modules.SelectMany(m =>
        Depends(m.Impl, m.ImplTypes,
                s.Modules.Where(o => o != m).SelectMany(o => o.ImplTypes).Select(t => t.FullName!))
            .Select(t => $"R1: {t} depende del interior de otro módulo")).ToList();

    // 2. Un Contracts no referencia internos ni EF Core.
    public static IReadOnlyList<string> ContractsPurity(Scope s) => s.Modules.SelectMany(m =>
        Depends(m.Contracts, m.ContractTypes,
                s.Modules.SelectMany(o => o.ImplTypes).Select(t => t.FullName!).Concat(EfCore))
            .Select(t => $"R2: {t} (Contracts) depende de internos o EF Core")).ToList();

    // 3. Lo único público de un módulo interno es <Módulo>Module.
    public static IReadOnlyList<string> OnlyModuleClassIsPublic(Scope s) => s.Modules.SelectMany(m =>
        m.ImplTypes.Where(t => (t.IsPublic || t.IsNestedPublic) && t.Name != m.Name + "Module")
            .Select(t => $"R3: {t.FullName} es público")).ToList();

    private static IEnumerable<Type> DbContextsOf(ModuleInfo m) =>
        m.ImplTypes.Where(t => !t.IsAbstract && InheritsFrom(t, "Microsoft.EntityFrameworkCore.DbContext"));

    private static bool InheritsFrom(Type t, string fullName)
    {
        for (var b = t.BaseType; b is not null; b = b.BaseType)
            if (b.FullName == fullName) return true;
        return false;
    }

    // 4. Un DbContext solo se usa dentro de su módulo.
    public static IReadOnlyList<string> DbContextStaysInModule(Scope s)
    {
        var all = new List<string>();
        foreach (var m in s.Modules)
        {
            var names = DbContextsOf(m).Select(t => t.FullName!).ToArray();
            foreach (var o in s.Modules.Where(o => o != m))
                all.AddRange(Depends(o.Impl, o.ImplTypes, names).Select(t => $"R4: {t} usa un DbContext de {m.Name}"));
            foreach (var host in new[] { s.Api, s.Worker, s.BuildingBlocks })
                all.AddRange(Depends(host.Assembly, TypesOf(host), names).Select(t => $"R4: {t} usa un DbContext de {m.Name}"));
        }
        return all;
    }

    // 6. Toda entidad de un DbSet implementa ITimestamped, salvo las exentas (AuditEvent: inmutable, su fecha es OccurredAtUtc).
    // shortcut: solo ve entidades expuestas como DbSet<T>; si un módulo mapea por Set<T>() o configuración, ampliar con el modelo de EF.
    public static IReadOnlyList<string> EntitiesAreTimestamped(Scope s, IReadOnlySet<string> exempt)
    {
        var all = new List<string>();
        foreach (var m in s.Modules)
            foreach (var ctx in DbContextsOf(m))
                foreach (var prop in ctx.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (!prop.PropertyType.IsGenericType ||
                        prop.PropertyType.GetGenericTypeDefinition().FullName != "Microsoft.EntityFrameworkCore.DbSet`1") continue;
                    var entity = prop.PropertyType.GetGenericArguments()[0];
                    if (exempt.Contains(entity.Name)) continue;
                    if (!entity.GetInterfaces().Any(i => i.FullName == "Diligencia.BuildingBlocks.Persistence.ITimestamped"))
                        all.Add($"R6: la entidad {entity.FullName} ({ctx.Name}) no implementa ITimestamped");
                }
        return all;
    }

    // 5. Api ⟂ Worker; BuildingBlocks no referencia módulos.
    public static IReadOnlyList<string> HostAndBuildingBlocksIsolation(Scope s)
    {
        var all = new List<string>();
        all.AddRange(Edge(s.Api, s.Worker));
        all.AddRange(Edge(s.Worker, s.Api));
        foreach (var m in s.Modules)
        {
            all.AddRange(Edge(s.BuildingBlocks, new CodeGroup(m.Name, m.Impl, m.ImplNamespace)));
            all.AddRange(Edge(s.BuildingBlocks, new CodeGroup(m.Name + ".Contracts", m.Contracts, m.ContractsNamespace)));
        }
        return all;
    }

    private static IEnumerable<string> Edge(CodeGroup from, CodeGroup to)
    {
        if (from.Assembly != to.Assembly &&
            from.Assembly.GetReferencedAssemblies().Any(r => r.Name == to.Assembly.GetName().Name))
            yield return $"R5: el ensamblado {from.Name} referencia a {to.Name}";

        // Nombres que existen en ambos lados (p. ej. `Program`) no indican dependencia, solo coinciden.
        var own = TypesOf(from).Select(t => t.FullName!).ToHashSet();
        var forbidden = TypesOf(to).Select(t => t.FullName!).Where(n => !own.Contains(n));
        foreach (var t in Depends(from.Assembly, TypesOf(from), forbidden))
            yield return $"R5: {t} ({from.Name}) depende de {to.Name}";
    }
}
