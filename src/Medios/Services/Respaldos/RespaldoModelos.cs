using System.ComponentModel.DataAnnotations;

namespace Medios.Services.Respaldos;

public class RespaldoEstado
{
    public int Version { get; set; } = 1;
    public bool MantenimientoDelegaciones { get; set; }
    public bool RetornoSoloLectura { get; set; }
    public string Generacion { get; set; } = Guid.NewGuid().ToString("N");
    public bool CopiaLista { get; set; }
    public bool Restaurando { get; set; }
    public string? CopiaOrigen { get; set; }
    public DateTimeOffset? RestauradaUtc { get; set; }
    public bool Programado { get; set; }
    public string Hora { get; set; } = "02:00";
    public int RetencionDias { get; set; } = 14;
    public DateTimeOffset? UltimoAgenteUtc { get; set; }
    public string? InstalacionHash { get; set; }
    public DateTimeOffset? InstalacionHastaUtc { get; set; }
    public string? SolicitudPendiente { get; set; }
    public DateTimeOffset? SolicitadaUtc { get; set; }
    public string? UltimaSolicitudAtendida { get; set; }
    public string? UltimoDiaProgramado { get; set; }
    public string? ErrorAgente { get; set; }
    public DateTimeOffset? ContingenciaHastaUtc { get; set; }
    public string EpocaContingencia { get; set; } = Guid.NewGuid().ToString("N");
    public List<RespaldoArchivo> Archivos { get; set; } = [];
    public List<AccesoEmergencia> Accesos { get; set; } = [];
    public List<EventoRespaldo> Eventos { get; set; } = [];
    public string? RestauracionPendiente { get; set; }
    public string? ErrorRestauracion { get; set; }
    public bool ContingenciaActiva => ContingenciaHastaUtc > DateTimeOffset.UtcNow;
}
public class RespaldoArchivo
{
    public string Id { get; set; } = "";
    public DateTimeOffset FechaUtc { get; set; }
    public long Bytes { get; set; }
    public string Sha256 { get; set; } = "";
    public string Solicitud { get; set; } = "";
}
public class AccesoEmergencia
{
    public string Usuario { get; set; } = "";
    public string Hash { get; set; } = "";
    public DateTimeOffset HastaUtc { get; set; }
    public bool CambiarClave { get; set; } = true;
    public int Intentos { get; set; }
    public DateTimeOffset? BloqueadoHastaUtc { get; set; }
    public string Revision { get; set; } = Guid.NewGuid().ToString("N");
}
public record EventoRespaldo(DateTimeOffset FechaUtc, string Usuario, string Accion);
public record AgenteSecretos(string Token, string Clave);
public class ProgramacionRespaldo
{
    public bool Programado { get; set; }
    [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$")] public string Hora { get; set; } = "02:00";
    [Range(2,90)] public int RetencionDias { get; set; } = 14;
}
public class RespaldoVista
{
    public RespaldoEstado Estado { get; set; } = new();
    public string BaseActual { get; set; } = "medios";
    public bool AgenteConfigurado { get; set; }
    public List<Medios.Entities.AutorizacionUsuario> Usuarios { get; set; } = [];
    public string? Aviso { get; set; }
}
