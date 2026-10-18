using Diligencia.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Diligencia.Modules.Terceros.Persistence;

internal sealed class Tercero : ITimestamped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Cuit { get; set; } = "";
    public string TipoTercero { get; set; } = "";
    public string TipoPersona { get; set; } = "";
    public string RazonSocial { get; set; } = "";
    public string Estado { get; set; } = "EnAlta";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

internal sealed class TerceroConfiguration : IEntityTypeConfiguration<Tercero>
{
    public void Configure(EntityTypeBuilder<Tercero> b)
    {
        b.ToTable("Terceros");
        b.HasKey(t => t.Id);
        b.Property(t => t.Cuit).HasColumnType("char(11)").IsRequired();
        b.HasIndex(t => t.Cuit).IsUnique();
        b.Property(t => t.TipoTercero).HasMaxLength(32).IsRequired();
        b.Property(t => t.TipoPersona).HasMaxLength(32).IsRequired();
        b.Property(t => t.RazonSocial).HasMaxLength(200).IsRequired();
        b.Property(t => t.Estado).HasMaxLength(32).IsRequired();
        b.Property(t => t.CreatedAtUtc).HasColumnType("datetime2(7)");
        b.Property(t => t.UpdatedAtUtc).HasColumnType("datetime2(7)");
    }
}
