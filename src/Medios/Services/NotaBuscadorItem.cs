namespace Medios.Services;

public class NotaBuscadorItem
{
    public int NotaId { get; set; }
    public int VersionActualId { get; set; }
    public string Titulo { get; set; } = "";
    public string CategoriaNombre { get; set; } = "";
    public string? AmbitoNota { get; set; }
    public string? PartidoNombre { get; set; }
    public string EstadoRevision { get; set; } = "";
    public string TipoEvento { get; set; } = "";
    public int SesionId { get; set; }
    public DateOnly SesionFecha { get; set; }
    public string SesionTurno { get; set; } = "";
    public string SesionEstado { get; set; } = "";
    public string? SintesisEstado { get; set; }
    public DateTime? FechaNoticia { get; set; }
    public DateTime FechaRegistro { get; set; }
    // FechaNoticia efectiva: FechaNoticia si está, sino FechaRegistro
    public DateTime FechaNoticiaEfectiva => FechaNoticia ?? FechaRegistro;
    public string? DelegacionNombre { get; set; }
    public DateTime FechaVersion { get; set; }
    public double? Latitud { get; set; }
    public double? Longitud { get; set; }
    public double? DistanciaKm { get; set; }
    public bool EsLibre { get; set; }
}
