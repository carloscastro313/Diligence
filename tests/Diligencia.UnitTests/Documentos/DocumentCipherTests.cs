using System.Security.Cryptography;
using Diligencia.Modules.Documentos.Features.Store;
using Microsoft.Extensions.Configuration;

namespace Diligencia.UnitTests.Documentos;

public class DocumentCipherTests
{
    private static readonly byte[] Plain = "contenido confidencial del documento"u8.ToArray();

    private static DocumentCipher Cipher(string key = "clave-de-prueba") => new(
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Documentos:EncryptionKey"] = key })
            .Build());

    [Fact]
    public void RoundTrip_ReturnsTheOriginal_AndCipherDiffersFromPlain()
    {
        var cipher = Cipher();
        var (nonce, tag, data) = cipher.Encrypt(Plain);

        Assert.Equal(12, nonce.Length);
        Assert.Equal(16, tag.Length);
        Assert.NotEqual(Plain, data);
        Assert.Equal(Plain, cipher.Decrypt(nonce, tag, data));
    }

    [Fact]
    public void Encrypt_SamePlaintext_UsesDifferentNonce()
    {
        var cipher = Cipher();

        Assert.NotEqual(cipher.Encrypt(Plain).Nonce, cipher.Encrypt(Plain).Nonce);
    }

    [Fact]
    public void Decrypt_TamperedTagOrCipherOrOtherKey_Throws()
    {
        var (nonce, tag, data) = Cipher().Encrypt(Plain);
        var badTag = (byte[])tag.Clone();
        badTag[0] ^= 1;
        var badData = (byte[])data.Clone();
        badData[0] ^= 1;

        Assert.ThrowsAny<CryptographicException>(() => Cipher().Decrypt(nonce, badTag, data));
        Assert.ThrowsAny<CryptographicException>(() => Cipher().Decrypt(nonce, tag, badData));
        Assert.ThrowsAny<CryptographicException>(() => Cipher("otra-clave").Decrypt(nonce, tag, data));
    }

    [Fact]
    public void MissingKey_FailsOnUse_NotOnConstruction()
    {
        var cipher = new DocumentCipher(new ConfigurationBuilder().Build());

        Assert.Throws<InvalidOperationException>(() => cipher.Encrypt(Plain));
    }
}
