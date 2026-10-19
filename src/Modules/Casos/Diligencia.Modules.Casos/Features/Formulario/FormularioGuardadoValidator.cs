using Diligencia.Modules.Casos.Features.GuardarFormulario;
using FluentValidation;

namespace Diligencia.Modules.Casos.Features.Formulario;

// Solo forma: un borrador vacío o a medias es válido.
internal sealed class FormularioGuardadoValidator : AbstractValidator<GuardarFormularioRequest>
{
    public FormularioGuardadoValidator()
    {
        RuleFor(r => r.Datos).NotNull().WithMessage("Los datos del formulario son obligatorios.").SetValidator(new SoloForma());
        RuleFor(r => r.Version).MaximumLength(64);
    }

    // Anidado y sin registro por convención: la base no tiene un uso propio como IValidator<FormularioDatos>.
    private sealed class SoloForma : FormularioFormaValidator;
}
