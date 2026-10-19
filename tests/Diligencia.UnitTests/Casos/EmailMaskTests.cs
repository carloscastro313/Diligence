using Diligencia.Modules.Casos.Features.Shared;

namespace Diligencia.UnitTests.Casos;

public class EmailMaskTests
{
    [Theory]
    [InlineData("contacto@example.com", "c***@example.com")]
    [InlineData("ab@example.com", "a***@example.com")]
    [InlineData("a@example.com", "***@example.com")]
    [InlineData("ana.perez@sub.dominio.com.ar", "a***@sub.dominio.com.ar")]
    public void Mask_KeepsTheFirstLetterAndTheDomain_WithAFixedLength(string email, string expected) =>
        Assert.Equal(expected, EmailMask.Mask(email));

    [Fact]
    public void Mask_NeverRevealsTheLocalPartLength() =>
        Assert.Equal(EmailMask.Mask("ab@x.com")[..4], EmailMask.Mask("abcdefghijkl@x.com")[..4]);
}
