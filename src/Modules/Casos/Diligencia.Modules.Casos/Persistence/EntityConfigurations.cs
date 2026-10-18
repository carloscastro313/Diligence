using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Diligencia.Modules.Casos.Persistence;

internal sealed class CasoConfiguration : IEntityTypeConfiguration<Caso>
{
    public void Configure(EntityTypeBuilder<Caso> b)
    {
        b.ToTable("Casos");
        b.HasKey(c => c.Id);
        b.Property(c => c.Cuit).HasMaxLength(11).IsRequired();
        b.Property(c => c.RazonSocial).HasMaxLength(200).IsRequired();
        b.Property(c => c.TipoTercero).HasColumnType("nvarchar(32)").HasConversion<string>();
        b.Property(c => c.Tipo).HasColumnType("nvarchar(32)").HasConversion<string>();
        b.Property(c => c.Estado).HasColumnType("nvarchar(32)").HasConversion<string>();
        b.Property(c => c.SolicitanteId).HasMaxLength(128).IsRequired();
        b.Property(c => c.EstadoDesdeUtc).HasColumnType("datetime2");
        b.Property(c => c.CerradoUtc).HasColumnType("datetime2");
        b.Property(c => c.CreatedAtUtc).HasColumnType("datetime2");
        b.Property(c => c.UpdatedAtUtc).HasColumnType("datetime2");
        b.HasIndex(c => c.TerceroId).IsUnique()
            .HasFilter("[Estado] IN ('EsperaTercero','EnVerificacion','EnAprobacion')");
    }
}

internal sealed class SolicitudConfiguration : IEntityTypeConfiguration<Solicitud>
{
    public void Configure(EntityTypeBuilder<Solicitud> b)
    {
        b.ToTable("Solicitudes");
        b.HasKey(s => s.Id);
        b.Property(s => s.SolicitanteId).HasMaxLength(128).IsRequired();
        b.Property(s => s.ContactoNombre).HasMaxLength(200).IsRequired();
        b.Property(s => s.ContactoEmail).HasMaxLength(254).IsRequired();
        b.Property(s => s.ContactoTelefono).HasMaxLength(50);
        b.Property(s => s.MontoAnualEstimado).HasColumnType("decimal(18,2)");
        b.Property(s => s.Motivo).HasMaxLength(1000).IsRequired();
        b.Property(s => s.CreatedAtUtc).HasColumnType("datetime2");
        b.Property(s => s.UpdatedAtUtc).HasColumnType("datetime2");
        b.HasIndex(s => new { s.CasoId, s.SolicitanteId }).IsUnique();
        b.HasIndex(s => s.SolicitanteId);
        b.HasOne<Caso>().WithMany().HasForeignKey(s => s.CasoId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EnlacePortalConfiguration : IEntityTypeConfiguration<EnlacePortal>
{
    public void Configure(EntityTypeBuilder<EnlacePortal> b)
    {
        b.ToTable("EnlacesPortal");
        b.HasKey(e => e.Id);
        b.Property(e => e.TokenHash).HasColumnType("binary(32)");
        b.Property(e => e.Destinatario).HasMaxLength(254).IsRequired();
        b.Property(e => e.VenceUtc).HasColumnType("datetime2");
        b.Property(e => e.UltimoIntentoUtc).HasColumnType("datetime2");
        b.Property(e => e.CreatedAtUtc).HasColumnType("datetime2");
        b.Property(e => e.UpdatedAtUtc).HasColumnType("datetime2");
        b.Property(e => e.Entrega).HasColumnType("nvarchar(32)").HasConversion<string>();
        b.HasIndex(e => e.CasoId).IsUnique();
        b.HasIndex(e => e.TokenHash).IsUnique().HasFilter("[TokenHash] IS NOT NULL");
        b.HasOne<Caso>().WithMany().HasForeignKey(e => e.CasoId).OnDelete(DeleteBehavior.Restrict);
    }
}
