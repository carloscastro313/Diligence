using Diligencia.Modules.Terceros.Contracts;
using FluentValidation;

// ChildRules solo se evalúa con el objeto presente: FluentValidation tipa el hijo como nulable y el chequeo de nulos sobra.
#pragma warning disable CS8602

namespace Diligencia.Modules.Casos.Features.Formulario;

// Lleva parámetros (persona del caso y fecha de hoy), por eso no se registra por convención: el handler lo instancia.
internal sealed class FormularioEnvioValidator : FormularioFormaValidator
{
    private const string NoCorresponde = "No corresponde al tipo de persona.";
    private const string PepObligatoria = "La declaración de persona expuesta políticamente es obligatoria.";

    public FormularioEnvioValidator(TipoPersona persona, DateOnly hoy)
    {
        RuleFor(d => d.CondicionIva).NotNull().WithMessage("La condición frente al IVA es obligatoria.");
        RuleFor(d => d.DomicilioFiscal).NotNull().WithMessage("El domicilio fiscal es obligatorio.").ChildRules(DomicilioObligatorio);
        RuleFor(d => d.Actividad).NotNull().WithMessage("La actividad es obligatoria.").ChildRules(a =>
        {
            a.RuleFor(x => x.Descripcion).Must(Llena).WithMessage("La descripción de la actividad es obligatoria.");
            a.RuleFor(x => x.Pais).Must(Llena).WithMessage("El país de la actividad es obligatorio.");
        });
        RuleFor(d => d.OrigenFondos).Must(v => v?.Trim().Length >= 10).WithMessage("El origen de los fondos es obligatorio (mínimo 10 caracteres).");

        if (persona == TipoPersona.Juridica) ReglasJuridica(hoy);
        else ReglasHumana(hoy);
    }

    private void ReglasJuridica(DateOnly hoy)
    {
        RuleFor(d => d.FechaConstitucion).NotNull().WithMessage("La fecha de constitución es obligatoria.");
        RuleFor(d => d.FechaConstitucion).Must(f => f <= hoy).When(d => d.FechaConstitucion is not null)
            .WithMessage("La fecha de constitución no puede ser futura.");
        RuleFor(d => d.NumeroInscripcion).Must(Llena).WithMessage("El número de inscripción es obligatorio.");
        RuleFor(d => d.DomicilioLegal).NotNull().When(d => d.DomicilioLegalIgualFiscal != true)
            .WithMessage("Ingresá el domicilio legal o indicá que es igual al fiscal.");
        RuleFor(d => d.DomicilioLegal).ChildRules(DomicilioObligatorio).When(d => d.DomicilioLegalIgualFiscal != true);

        RuleFor(d => d.Beneficiarios).Must((d, b) => b is { Count: > 0 } || d.SinBeneficiarios is not null)
            .WithMessage("Declará al menos un beneficiario final o indicá que no hay beneficiarios identificables.");
        RuleFor(d => d.SinBeneficiarios).Null().When(d => d.Beneficiarios is { Count: > 0 })
            .WithMessage("No se pueden declarar beneficiarios y, a la vez, que no hay beneficiarios identificables.");
        RuleFor(d => d.SinBeneficiarios).ChildRules(s => s.RuleFor(x => x.Explicacion)
            .Must(v => v?.Trim().Length is >= 20 and <= 1000).WithMessage("La explicación debe tener entre 20 y 1000 caracteres."));
        RuleForEach(d => d.Beneficiarios).ChildRules(b =>
        {
            b.RuleFor(x => x.Nombre).Must(Llena).WithMessage("El nombre del beneficiario es obligatorio.");
            b.RuleFor(x => x.Documento).Must(Llena).WithMessage("El documento del beneficiario es obligatorio.");
            b.RuleFor(x => x.Nacionalidad).Must(Llena).WithMessage("La nacionalidad del beneficiario es obligatoria.");
            b.RuleFor(x => x.Porcentaje).Must(p => p > 0).WithMessage("El porcentaje es obligatorio y debe ser mayor que 0.");
            b.RuleFor(x => x.Pep).NotNull().WithMessage(PepObligatoria).ChildRules(PepCompleta);
        });

        RuleFor(d => d.Representantes).Must(l => l is { Count: > 0 }).WithMessage("Declará al menos un representante.");
        RuleForEach(d => d.Representantes).ChildRules(r =>
        {
            r.RuleFor(x => x.Nombre).Must(Llena).WithMessage("El nombre del representante es obligatorio.");
            r.RuleFor(x => x.Documento).Must(Llena).WithMessage("El documento del representante es obligatorio.");
            r.RuleFor(x => x.Pep).NotNull().WithMessage(PepObligatoria).ChildRules(PepCompleta);
        });

        RuleFor(d => d.FechaNacimiento).Null().WithMessage(NoCorresponde);
        RuleFor(d => d.Nacionalidad).Null().WithMessage(NoCorresponde);
        RuleFor(d => d.Documento).Null().WithMessage(NoCorresponde);
        RuleFor(d => d.Pep).Null().WithMessage(NoCorresponde);
    }

    private void ReglasHumana(DateOnly hoy)
    {
        RuleFor(d => d.FechaNacimiento).NotNull().WithMessage("La fecha de nacimiento es obligatoria.");
        RuleFor(d => d.FechaNacimiento).Must(f => f <= hoy.AddYears(-18) && f > hoy.AddYears(-121)).When(d => d.FechaNacimiento is not null)
            .WithMessage("La edad debe estar entre 18 y 120 años.");
        RuleFor(d => d.Nacionalidad).Must(Llena).WithMessage("La nacionalidad es obligatoria.");
        RuleFor(d => d.Documento).NotNull().WithMessage("El documento es obligatorio.").ChildRules(doc =>
        {
            doc.RuleFor(x => x.Tipo).NotNull().WithMessage("El tipo de documento es obligatorio.");
            doc.RuleFor(x => x.Numero).Must(Llena).WithMessage("El número de documento es obligatorio.");
            doc.RuleFor(x => x.Numero).Matches(@"^\d{7,8}$").When(x => x.Tipo == TipoDocumentoPersonal.DNI && Llena(x.Numero))
                .WithMessage("El DNI debe tener 7 u 8 dígitos.");
        });
        RuleFor(d => d.Pep).NotNull().WithMessage(PepObligatoria).ChildRules(PepCompleta);

        RuleFor(d => d.FechaConstitucion).Null().WithMessage(NoCorresponde);
        RuleFor(d => d.NumeroInscripcion).Null().WithMessage(NoCorresponde);
        RuleFor(d => d.DomicilioLegal).Null().WithMessage(NoCorresponde);
        RuleFor(d => d.DomicilioLegalIgualFiscal).Null().WithMessage(NoCorresponde);
        RuleFor(d => d.Beneficiarios).Must(l => l is not { Count: > 0 }).WithMessage(NoCorresponde);
        RuleFor(d => d.SinBeneficiarios).Null().WithMessage(NoCorresponde);
        RuleFor(d => d.Representantes).Must(l => l is not { Count: > 0 }).WithMessage(NoCorresponde);
    }

    private static bool Llena(string? v) => !string.IsNullOrWhiteSpace(v);

    private static void PepCompleta(InlineValidator<Pep?> p)
    {
        p.RuleFor(x => x.EsPep).NotNull().WithMessage("Indicá si es persona expuesta políticamente.");
        p.RuleFor(x => x.Cargo).Must(Llena).When(x => x.EsPep == true).WithMessage("El cargo es obligatorio si es PEP.");
        p.RuleFor(x => x.Organismo).Must(Llena).When(x => x.EsPep == true).WithMessage("El organismo es obligatorio si es PEP.");
    }

    private static void DomicilioObligatorio(InlineValidator<Domicilio?> d)
    {
        d.RuleFor(x => x.Calle).Must(Llena).WithMessage("La calle es obligatoria.");
        d.RuleFor(x => x.Numero).Must(Llena).WithMessage("El número es obligatorio.");
        d.RuleFor(x => x.Localidad).Must(Llena).WithMessage("La localidad es obligatoria.");
        d.RuleFor(x => x.Provincia).Must(Llena).WithMessage("La provincia es obligatoria.");
        d.RuleFor(x => x.CodigoPostal).Must(Llena).WithMessage("El código postal es obligatorio.");
        d.RuleFor(x => x.Pais).Must(Llena).WithMessage("El país es obligatorio.");
    }
}
