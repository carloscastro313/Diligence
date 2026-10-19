using Diligencia.Modules.Terceros.Contracts;

namespace Diligencia.Modules.Casos.Features.Shared;

internal sealed record DocumentoRequerido(string Tipo, string Nombre, bool Obligatorio);

// Catálogo fijo de la versión (FR-016); hacerlo configurable es RF-30.
internal static class DocumentosRequeridos
{
    private static readonly DocumentoRequerido[] Juridica =
    [
        new("Estatuto", "Estatuto o contrato social", true),
        new("ConstanciaArca", "Constancia de inscripción en ARCA", true),
        new("ActaAutoridades", "Acta de designación de autoridades", true),
        new("DocumentoRepresentante", "Documento del representante legal", true),
        new("UltimoBalance", "Último balance", false),
    ];

    private static readonly DocumentoRequerido[] Humana =
    [
        new("DniFrente", "Documento de identidad (frente)", true),
        new("DniDorso", "Documento de identidad (dorso)", true),
        new("ConstanciaArca", "Constancia de inscripción en ARCA", true),
        new("ConstanciaDomicilio", "Constancia de domicilio", false),
    ];

    public static IReadOnlyList<DocumentoRequerido> For(TipoPersona persona) =>
        persona == TipoPersona.Juridica ? Juridica : Humana;
}
