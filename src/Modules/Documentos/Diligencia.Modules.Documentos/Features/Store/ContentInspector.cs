namespace Diligencia.Modules.Documentos.Features.Store;

internal enum ContentKind { NoPermitido, Ilegible, Pdf, Jpeg, Png }

internal static class ContentInspector
{
    private const int TailSize = 1024;
    private static readonly byte[] PdfHead = "%PDF-"u8.ToArray();
    private static readonly byte[] PdfEof = "%%EOF"u8.ToArray();
    private static readonly byte[] JpegHead = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] JpegEnd = [0xFF, 0xD9];
    private static readonly byte[] PngHead = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] PngEnd = [0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82];

    public static string? ContentTypeOf(ContentKind kind) => kind switch
    {
        ContentKind.Pdf => "application/pdf",
        ContentKind.Jpeg => "image/jpeg",
        ContentKind.Png => "image/png",
        _ => null,
    };

    // shortcut: no parsea el cuerpo; la extracción con PdfPig del worker detectará PDF dañados por dentro o con contraseña de apertura
    public static ContentKind Detect(Stream stream)
    {
        if (!stream.CanSeek) throw new ArgumentException("El stream debe ser seekable.", nameof(stream));

        try
        {
            var length = stream.Length;
            var head = Read(stream, 0, (int)Math.Min(length, PngHead.Length));
            var tailStart = Math.Max(0, length - TailSize);
            var tail = Read(stream, tailStart, (int)(length - tailStart));

            if (head.AsSpan().StartsWith(PdfHead))
                return tail.AsSpan().IndexOf(PdfEof) >= 0 ? ContentKind.Pdf : ContentKind.Ilegible;
            if (head.AsSpan().StartsWith(JpegHead))
                return tail.AsSpan().EndsWith(JpegEnd) ? ContentKind.Jpeg : ContentKind.Ilegible;
            if (head.AsSpan().StartsWith(PngHead))
                return tail.AsSpan().EndsWith(PngEnd) ? ContentKind.Png : ContentKind.Ilegible;
            return ContentKind.NoPermitido;
        }
        finally
        {
            stream.Position = 0;
        }
    }

    private static byte[] Read(Stream stream, long position, int count)
    {
        var buffer = new byte[count];
        stream.Position = position;
        stream.ReadExactly(buffer);
        return buffer;
    }
}
