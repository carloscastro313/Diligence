namespace Diligencia.BuildingBlocks.Persistence;

public interface ITimestamped
{
    DateTime CreatedAtUtc { get; set; }
    DateTime UpdatedAtUtc { get; set; }
}
