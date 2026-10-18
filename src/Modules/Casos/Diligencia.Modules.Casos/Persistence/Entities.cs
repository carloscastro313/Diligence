using Diligencia.BuildingBlocks.Persistence;
using Diligencia.Modules.Terceros.Contracts;

namespace Diligencia.Modules.Casos.Persistence;

internal enum TipoCaso { Alta }

internal enum EstadoCaso { EsperaTercero, EnVerificacion, EnAprobacion, Aprobado, Rechazado, Abandonado }

internal enum EntregaEnlace { Pendiente, Reintentando, Enviado, Fallido }

internal sealed class Caso : ITimestamped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TerceroId { get; set; }
    public string Cuit { get; set; } = "";
    public string RazonSocial { get; set; } = "";
    public TipoTercero TipoTercero { get; set; }
    public TipoCaso Tipo { get; set; } = TipoCaso.Alta;
    public EstadoCaso Estado { get; set; } = EstadoCaso.EsperaTercero;
    public DateTime EstadoDesdeUtc { get; set; }
    public DateTime? CerradoUtc { get; set; }
    public string SolicitanteId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

internal sealed class Solicitud : ITimestamped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CasoId { get; set; }
    public string SolicitanteId { get; set; } = "";
    public bool EsOriginal { get; set; }
    public string ContactoNombre { get; set; } = "";
    public string ContactoEmail { get; set; } = "";
    public string? ContactoTelefono { get; set; }
    public decimal MontoAnualEstimado { get; set; }
    public string Motivo { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

internal sealed class EnlacePortal : ITimestamped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CasoId { get; set; }
    public byte[]? TokenHash { get; set; }
    public string Destinatario { get; set; } = "";
    public DateTime VenceUtc { get; set; }
    public EntregaEnlace Entrega { get; set; } = EntregaEnlace.Pendiente;
    public int Intentos { get; set; }
    public DateTime? UltimoIntentoUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
