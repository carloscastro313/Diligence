using System.Text;

namespace Diligencia.IntegrationTests.Support;

/// <summary>Archivos de prueba generados en código: ninguno depende de fixtures en disco.</summary>
public static class SampleFiles
{
    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    public static byte[] Pdf() => Ascii("%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n");

    // Contraseña de propietario: el trailer lleva /Encrypt pero el documento se abre sin contraseña (debe aceptarse).
    public static byte[] PdfCifrado() =>
        Ascii("%PDF-1.7\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< /Root 1 0 R /Encrypt 2 0 R >>\n%%EOF\n");

    public static byte[] PdfTruncado() => Ascii("%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n");

    public static byte[] Jpeg() => [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9];

    public static byte[] JpegTruncado() => Jpeg()[..^2];

    public static byte[] Png() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89,
        0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    public static byte[] PngTruncado() => Png()[..^12];

    public static byte[] Gif() => Ascii("GIF89a\x01\x00\x01\x00\x00\x00\x00;");

    public static byte[] TextoRenombrado() => Ascii("Esto es un texto plano con extensión .pdf\n");

    /// <summary>PDF válido (empieza con %PDF-, termina con %%EOF) relleno hasta el tamaño pedido.</summary>
    public static byte[] Grande(long bytes)
    {
        var head = Ascii("%PDF-1.4\n%");
        var tail = Ascii("\n%%EOF\n");
        var data = new byte[bytes];
        head.CopyTo(data, 0);
        Array.Fill(data, (byte)'x', head.Length, (int)bytes - head.Length - tail.Length);
        tail.CopyTo(data, bytes - tail.Length);
        return data;
    }
}
