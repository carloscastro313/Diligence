using Diligencia.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Diligencia.Modules.Documentos.Persistence;

internal enum MotivoBaja { Reemplazado, Eliminado }

// CasoId y TerceroId sin FK: viven en otros schemas. Tipo es un código opaco que define Casos.
internal sealed class Documento : ITimestamped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CasoId { get; set; }
    public Guid TerceroId { get; set; }
    public string Tipo { get; set; } = "";
    public string NombreOriginal { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long TamanoBytes { get; set; }
    public byte[] Sha256 { get; set; } = [];
    public string CargadoPor { get; set; } = "";
    public DateTime CargadoUtc { get; set; }
    public DateTime? BajaUtc { get; set; }
    public MotivoBaja? Baja { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

// Aparte para que listar documentos nunca lea el contenido.
internal sealed class DocumentoContenido
{
    public Guid DocumentoId { get; set; }
    public byte[] Nonce { get; set; } = [];
    public byte[] Tag { get; set; } = [];
    public byte[] Cifrado { get; set; } = [];
}

internal sealed class ParametrosDocumentos
{
    public const long TamanoPorDefectoBytes = 10L * 1024 * 1024;

    public int Id { get; set; } = 1;
    public long TamanoMaximoBytes { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

internal sealed class DocumentoConfiguration : IEntityTypeConfiguration<Documento>
{
    public void Configure(EntityTypeBuilder<Documento> b)
    {
        b.ToTable("Documentos");
        b.HasKey(d => d.Id);
        b.Property(d => d.Tipo).HasMaxLength(64).IsRequired();
        b.Property(d => d.NombreOriginal).HasMaxLength(255).IsRequired();
        b.Property(d => d.ContentType).HasMaxLength(64).IsRequired();
        b.Property(d => d.Sha256).HasColumnType("binary(32)");
        b.Property(d => d.CargadoPor).HasMaxLength(128).IsRequired();
        b.Property(d => d.CargadoUtc).HasColumnType("datetime2");
        b.Property(d => d.BajaUtc).HasColumnType("datetime2");
        b.Property(d => d.Baja).HasColumnType("nvarchar(32)").HasConversion<string>();
        b.Property(d => d.CreatedAtUtc).HasColumnType("datetime2");
        b.Property(d => d.UpdatedAtUtc).HasColumnType("datetime2");
        // Un vigente por tipo y caso; las bajas quedan como evidencia.
        b.HasIndex(d => new { d.CasoId, d.Tipo }).IsUnique().HasFilter("[BajaUtc] IS NULL");
        b.HasIndex(d => d.CasoId);
    }
}

internal sealed class DocumentoContenidoConfiguration : IEntityTypeConfiguration<DocumentoContenido>
{
    public void Configure(EntityTypeBuilder<DocumentoContenido> b)
    {
        b.ToTable("DocumentoContenidos");
        b.HasKey(c => c.DocumentoId);
        b.Property(c => c.Nonce).HasColumnType("binary(12)");
        b.Property(c => c.Tag).HasColumnType("binary(16)");
        b.Property(c => c.Cifrado).HasColumnType("varbinary(max)");
        b.HasOne<Documento>().WithOne().HasForeignKey<DocumentoContenido>(c => c.DocumentoId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ParametrosConfiguration : IEntityTypeConfiguration<ParametrosDocumentos>
{
    public void Configure(EntityTypeBuilder<ParametrosDocumentos> b)
    {
        b.ToTable("Configuracion", t => t.HasCheckConstraint("CK_Configuracion_Id", "[Id] = 1"));
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).ValueGeneratedNever();
        b.Property(c => c.TamanoMaximoBytes).HasColumnType("bigint");
        b.Property(c => c.UpdatedAtUtc).HasColumnType("datetime2");
        b.HasData(new ParametrosDocumentos { Id = 1, TamanoMaximoBytes = ParametrosDocumentos.TamanoPorDefectoBytes, UpdatedAtUtc = new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc) });
    }
}
