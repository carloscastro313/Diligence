using Diligencia.Modules.Casos.Features.Shared;
using FluentValidation;

// ChildRules solo se evalúa con el objeto presente: FluentValidation tipa el hijo como nulable y el chequeo de nulos sobra.
#pragma warning disable CS8602

namespace Diligencia.Modules.Casos.Features.Formulario;

// Reglas de forma (longitudes, rangos, países, suma de porcentajes): valen al guardar y al enviar.
// Nada es obligatorio; los validadores de guardado y de envío lo reutilizan.
internal abstract class FormularioFormaValidator : AbstractValidator<FormularioDatos>
{
    public FormularioFormaValidator()
    {
        RuleFor(d => d.NumeroInscripcion).MaximumLength(50);
        RuleFor(d => d.Nacionalidad).Must(Paises.EsValido).When(d => !string.IsNullOrEmpty(d.Nacionalidad)).WithMessage(PaisInvalido);
        RuleFor(d => d.Documento).ChildRules(doc => doc.RuleFor(x => x.Numero).MaximumLength(20));
        RuleFor(d => d.DomicilioFiscal).ChildRules(DomicilioForma);
        RuleFor(d => d.DomicilioLegal).ChildRules(DomicilioForma);
        RuleFor(d => d.Actividad).ChildRules(a =>
        {
            a.RuleFor(x => x.Descripcion).MaximumLength(200);
            a.RuleFor(x => x.Pais).Must(Paises.EsValido).When(x => !string.IsNullOrEmpty(x.Pais)).WithMessage(PaisInvalido);
        });
        RuleFor(d => d.OrigenFondos).MaximumLength(2000);
        RuleFor(d => d.Pep).ChildRules(PepForma);

        RuleFor(d => d.Beneficiarios).Must(l => l!.Count <= 20).When(d => d.Beneficiarios is not null)
            .WithMessage("Se admiten hasta 20 beneficiarios finales.");
        RuleFor(d => d.Beneficiarios).Must(l => l!.Sum(b => b?.Porcentaje ?? 0) <= 100).When(d => d.Beneficiarios is not null)
            .WithMessage("La suma de los porcentajes de los beneficiarios no puede superar 100 %.");
        RuleForEach(d => d.Beneficiarios).NotNull().WithMessage("El beneficiario no puede estar vacío.").ChildRules(b =>
        {
            b.RuleFor(x => x.Nombre).MaximumLength(200);
            b.RuleFor(x => x.Documento).MaximumLength(20);
            b.RuleFor(x => x.Nacionalidad).Must(Paises.EsValido).When(x => !string.IsNullOrEmpty(x.Nacionalidad)).WithMessage(PaisInvalido);
            b.RuleFor(x => x.Porcentaje).InclusiveBetween(0m, 100m).When(x => x.Porcentaje is not null)
                .WithMessage("El porcentaje debe estar entre 0 y 100.");
            b.RuleFor(x => x.Porcentaje).Must(p => decimal.Round(p!.Value, 2) == p).When(x => x.Porcentaje is not null)
                .WithMessage("El porcentaje admite hasta 2 decimales.");
            b.RuleFor(x => x.Pep).ChildRules(PepForma);
        });
        RuleFor(d => d.SinBeneficiarios).ChildRules(s => s.RuleFor(x => x.Explicacion).MaximumLength(1000));

        RuleFor(d => d.Representantes).Must(l => l!.Count <= 10).When(d => d.Representantes is not null)
            .WithMessage("Se admiten hasta 10 representantes.");
        RuleForEach(d => d.Representantes).NotNull().WithMessage("El representante no puede estar vacío.").ChildRules(r =>
        {
            r.RuleFor(x => x.Nombre).MaximumLength(200);
            r.RuleFor(x => x.Documento).MaximumLength(20);
            r.RuleFor(x => x.Pep).ChildRules(PepForma);
        });
    }

    internal const string PaisInvalido = "El país no es válido (código ISO de dos letras en mayúsculas).";

    private static void PepForma(InlineValidator<Pep?> p)
    {
        p.RuleFor(x => x.Cargo).MaximumLength(200);
        p.RuleFor(x => x.Organismo).MaximumLength(200);
    }

    private static void DomicilioForma(InlineValidator<Domicilio?> d)
    {
        d.RuleFor(x => x.Calle).MaximumLength(200);
        d.RuleFor(x => x.Numero).MaximumLength(20);
        d.RuleFor(x => x.Piso).MaximumLength(10);
        d.RuleFor(x => x.Depto).MaximumLength(10);
        d.RuleFor(x => x.Localidad).MaximumLength(100);
        d.RuleFor(x => x.Provincia).MaximumLength(100);
        d.RuleFor(x => x.CodigoPostal).MaximumLength(10);
        d.RuleFor(x => x.Pais).Must(Paises.EsValido).When(x => !string.IsNullOrEmpty(x.Pais)).WithMessage(PaisInvalido);
    }
}
