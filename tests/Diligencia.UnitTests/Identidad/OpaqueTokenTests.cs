using Diligencia.Modules.Identidad.Authentication;

namespace Diligencia.UnitTests.Identidad;

public class OpaqueTokenTests
{
    [Fact]
    public void Generate_IsBase64Url_Of32Bytes_AndNeverRepeats()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => OpaqueToken.Generate()).ToList();

        Assert.Equal(100, tokens.Distinct().Count());
        Assert.All(tokens, t =>
        {
            Assert.Equal(43, t.Length); // 32 bytes en base64url sin relleno
            Assert.Matches("^[A-Za-z0-9_-]+$", t);
        });
    }

    [Fact]
    public void Hash_IsDeterministicSha256()
    {
        var token = OpaqueToken.Generate();

        Assert.Equal(32, OpaqueToken.Hash(token).Length);
        Assert.Equal(OpaqueToken.Hash(token), OpaqueToken.Hash(token));
        Assert.NotEqual(OpaqueToken.Hash(token), OpaqueToken.Hash(OpaqueToken.Generate()));
    }
}
