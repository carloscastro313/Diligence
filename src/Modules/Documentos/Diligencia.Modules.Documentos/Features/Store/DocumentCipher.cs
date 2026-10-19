using System.Security.Cryptography;
using System.Text;
using Diligencia.BuildingBlocks.Configuration;
using Microsoft.Extensions.Configuration;

namespace Diligencia.Modules.Documentos.Features.Store;

internal sealed class DocumentCipher(IConfiguration configuration)
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    // Perezosa: el worker y el MigrationService arrancan sin la clave.
    // shortcut: una sola clave sin KeyId; agregar la columna y el anillo de claves al necesitar rotar
    private readonly Lazy<byte[]> key = new(() =>
        SHA256.HashData(Encoding.UTF8.GetBytes(configuration.GetRequired("Documentos:EncryptionKey"))));

    public (byte[] Nonce, byte[] Tag, byte[] Cipher) Encrypt(byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var cipher = new byte[plaintext.Length];
        using var aes = new AesGcm(key.Value, TagSize);
        aes.Encrypt(nonce, plaintext, cipher, tag);
        return (nonce, tag, cipher);
    }

    public byte[] Decrypt(byte[] nonce, byte[] tag, byte[] cipher)
    {
        var plaintext = new byte[cipher.Length];
        using var aes = new AesGcm(key.Value, TagSize);
        aes.Decrypt(nonce, cipher, tag, plaintext);
        return plaintext;
    }
}
