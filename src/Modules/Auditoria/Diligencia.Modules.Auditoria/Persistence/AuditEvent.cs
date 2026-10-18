using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Diligencia.Modules.Auditoria.Persistence;

// No implementa ITimestamped: es inmutable y su única fecha es OccurredAtUtc (la asigna la base).
internal sealed class AuditEvent
{
    public long Id { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string ActorId { get; set; } = "";
    public string Surface { get; set; } = "";
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string CorrelationId { get; set; } = "";
}

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> b)
    {
        b.ToTable("AuditEvents");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedOnAdd();
        b.Property(e => e.OccurredAtUtc).HasColumnType("datetime2(7)").HasDefaultValueSql("SYSUTCDATETIME()").ValueGeneratedOnAdd();
        b.Property(e => e.ActorId).HasMaxLength(200).IsRequired();
        b.Property(e => e.Surface).HasMaxLength(16).IsRequired();
        b.Property(e => e.Action).HasMaxLength(32).IsRequired();
        b.Property(e => e.EntityType).HasMaxLength(128).IsRequired();
        b.Property(e => e.EntityId).HasMaxLength(128).IsRequired();
        b.Property(e => e.CorrelationId).HasMaxLength(64).IsRequired();

        b.HasIndex(e => new { e.EntityType, e.EntityId, e.OccurredAtUtc, e.Id }).HasDatabaseName("IX_AuditEvents_Entity");
        b.HasIndex(e => new { e.ActorId, e.OccurredAtUtc, e.Id }).HasDatabaseName("IX_AuditEvents_Actor");
        b.HasIndex(e => new { e.OccurredAtUtc, e.Id }).HasDatabaseName("IX_AuditEvents_OccurredAt");
    }
}
