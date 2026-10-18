using Diligencia.Modules.Identidad.Authentication;

namespace Diligencia.UnitTests.Identidad;

public class PasswordHashingTests
{
    [Fact]
    public void Hash_Verifies_AndIsSalted()
    {
        const string pwd = "una-contrasena-larga-1";
        var a = PasswordHashing.Hash(pwd);
        var b = PasswordHashing.Hash(pwd);

        Assert.NotEqual(a, b);
        Assert.Equal(60, a.Length);
        Assert.True(PasswordHashing.Verify(pwd, a));
        Assert.False(PasswordHashing.Verify(pwd + "x", a));
    }

    [Theory]
    [InlineData(11, false)]
    [InlineData(12, true)]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void IsValidLength_BoundariesInCharacters(int length, bool valid) =>
        Assert.Equal(valid, PasswordHashing.IsValidLength(new string('a', length)));

    [Fact]
    public void IsValidLength_RejectsMoreThan72Utf8Bytes()
    {
        Assert.False(PasswordHashing.IsValidLength(new string('ñ', 40))); // 40 caracteres, 80 bytes
        Assert.True(PasswordHashing.IsValidLength(new string('ñ', 36)));  // 72 bytes
        Assert.False(PasswordHashing.IsValidLength(null));
    }

    [Fact]
    public void VerifyDummy_DoesNotThrow() => PasswordHashing.VerifyDummy("cualquiera");
}
