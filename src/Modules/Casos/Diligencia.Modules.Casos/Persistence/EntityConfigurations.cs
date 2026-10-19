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
        b.Property(e => e.FallidosConsecutivos).HasDefaultValue(0);
        b.Property(e => e.BloqueadoHastaUtc).HasColumnType("datetime2");
        b.Property(e => e.CreatedAtUtc).HasColumnType("datetime2");
        b.Property(e => e.UpdatedAtUtc).HasColumnType("datetime2");
        b.Property(e => e.Entrega).HasColumnType("nvarchar(32)").HasConversion<string>();
        b.HasIndex(e => e.CasoId).IsUnique();
        b.HasIndex(e => e.TokenHash).IsUnique().HasFilter("[TokenHash] IS NOT NULL");
        b.HasOne<Caso>().WithMany().HasForeignKey(e => e.CasoId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CodigoIngresoConfiguration : IEntityTypeConfiguration<CodigoIngreso>
{
    public void Configure(EntityTypeBuilder<CodigoIngreso> b)
    {
        b.ToTable("CodigosIngreso");
        b.HasKey(c => c.Id);
        b.Property(c => c.CodigoHash).HasColumnType("binary(32)");
        b.Property(c => c.VenceUtc).HasColumnType("datetime2");
        b.Property(c => c.Estado).HasColumnType("nvarchar(32)").HasConversion<string>();
        b.Property(c => c.CreatedAtUtc).HasColumnType("datetime2");
        b.Property(c => c.UpdatedAtUtc).HasColumnType("datetime2");
        b.HasIndex(c => new { c.EnlaceId, c.CreatedAtUtc });
        b.HasOne<EnlacePortal>().WithMany().HasForeignKey(c => c.EnlaceId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FormularioTerceroConfiguration : IEntityTypeConfiguration<FormularioTercero>
{
    public void Configure(EntityTypeBuilder<FormularioTercero> b)
    {
        b.ToTable("FormulariosTercero");
        b.HasKey(f => f.CasoId);
        b.Property(f => f.Datos).HasColumnType("nvarchar(max)").IsRequired();
        b.Property(f => f.Estado).HasColumnType("nvarchar(32)").HasConversion<string>();
        b.Property(f => f.GuardadoUtc).HasColumnType("datetime2");
        b.Property(f => f.EnviadoUtc).HasColumnType("datetime2");
        b.Property(f => f.EnviadoPor).HasMaxLength(128);
        b.Property(f => f.Version).IsRowVersion();
        b.Property(f => f.CreatedAtUtc).HasColumnType("datetime2");
        b.Property(f => f.UpdatedAtUtc).HasColumnType("datetime2");
        b.HasOne<Caso>().WithOne().HasForeignKey<FormularioTercero>(f => f.CasoId).OnDelete(DeleteBehavior.Restrict);
    }
}
