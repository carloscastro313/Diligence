using System.Text;
using Diligencia.Modules.Documentos.Features.Store;

namespace Diligencia.UnitTests.Documentos;

public class ContentInspectorTests
{
    private static readonly byte[] PngHead = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] PngEnd = [0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82];

    private static byte[] Pdf(string trailer = "trailer\n<< /Root 1 0 R >>\n%%EOF\n") =>
        Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\n" + trailer);

    private static byte[] Padded(byte[] head, byte[] tail) => [.. head, .. new byte[5000], .. tail];

    private static ContentKind Detect(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var kind = ContentInspector.Detect(stream);
        Assert.Equal(0, stream.Position);
        return kind;
    }

    [Fact]
    public void Detect_ValidFiles_ReturnTheirType()
    {
        Assert.Equal(ContentKind.Pdf, Detect(Pdf()));
        Assert.Equal(ContentKind.Jpeg, Detect(Padded([0xFF, 0xD8, 0xFF, 0xE0], [0xFF, 0xD9])));
        Assert.Equal(ContentKind.Png, Detect(Padded(PngHead, PngEnd)));
        Assert.Equal("application/pdf", ContentInspector.ContentTypeOf(ContentKind.Pdf));
        Assert.Equal("image/jpeg", ContentInspector.ContentTypeOf(ContentKind.Jpeg));
        Assert.Equal("image/png", ContentInspector.ContentTypeOf(ContentKind.Png));
    }

    [Fact]
    public void Detect_PdfWithEncryptInTrailer_IsAccepted()
    {
        Assert.Equal(ContentKind.Pdf, Detect(Pdf("trailer\n<< /Root 1 0 R /Encrypt 5 0 R >>\n%%EOF\n")));
    }

    [Fact]
    public void Detect_EofBeyondLast1024Bytes_IsUnreadable()
    {
        Assert.Equal(ContentKind.Ilegible, Detect([.. Pdf(), .. new byte[2000]]));
    }

    [Fact]
    public void Detect_ValidHeaderWithoutEnd_IsUnreadable()
    {
        Assert.Equal(ContentKind.Ilegible, Detect(Pdf("trailer\n<< /Root 1 0 R >>\n")));
        Assert.Equal(ContentKind.Ilegible, Detect(Padded([0xFF, 0xD8, 0xFF, 0xE0], [0x00, 0x00])));
        Assert.Equal(ContentKind.Ilegible, Detect(Padded(PngHead, [0x00, 0x00, 0x00, 0x00])));
    }

    [Fact]
    public void Detect_UnknownHeaderOrEmpty_IsNotAllowed()
    {
        Assert.Equal(ContentKind.NoPermitido, Detect(Encoding.ASCII.GetBytes("hola, esto es un texto %%EOF")));
        Assert.Equal(ContentKind.NoPermitido, Detect(Encoding.ASCII.GetBytes("GIF89a....;")));
        Assert.Equal(ContentKind.NoPermitido, Detect([]));
        Assert.Null(ContentInspector.ContentTypeOf(ContentKind.NoPermitido));
    }

    [Fact]
    public void Detect_NonSeekableStream_Throws()
    {
        using var stream = new NonSeekable();
        Assert.Throws<ArgumentException>(() => ContentInspector.Detect(stream));
    }

    private sealed class NonSeekable : MemoryStream
    {
        public override bool CanSeek => false;
    }
}
