using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Medios.Entities;

[Table("delegaciones")]
public class Delegacion
{
    [Key]
    public int Id { get; set; }

    [StringLength(200)]
    public string Nombre { get; set; } = "";

    [StringLength(200)]
    public string? Jurisdiccion { get; set; }

    public int? AreaResponsabilidadId { get; set; }

    [ForeignKey("AreaResponsabilidadId")]
    public virtual AreaResponsabilidad? AreaResponsabilidad { get; set; }

    public int? DelegacionPrometheusId { get; set; }

    [ForeignKey("DelegacionPrometheusId")]
    public virtual DelegacionPrometheus? DelegacionPrometheus { get; set; }

    public int? PartidoId { get; set; }

    [ForeignKey("PartidoId")]
    public virtual Partido? Partido { get; set; }

    [StringLength(200)]
    public string? Email { get; set; }

    // Usuario de Cerberus/Cassandra asociado a esta delegación
    [StringLength(100)]
    public string? UsuarioCerberus { get; set; }

    public bool Activa { get; set; } = true;

    // JSON: lista de URLs de portales de prensa que monitorea esta delegación
    [Column(TypeName = "json")]
    public string? PortalesPrensa { get; set; }

    // Sincronización / contingencia: IDs de las planillas de Google (respuestas de los
    // Forms) desde donde el sistema captura notas y síntesis de esta delegación.
    [StringLength(200)]
    public string? DriveSheetNotas { get; set; }

    [StringLength(200)]
    public string? DriveSheetSintesis { get; set; }

    public virtual ICollection<SesionPrensa> Sesiones { get; set; } = new List<SesionPrensa>();

    // Partidos vinculados directamente a esta delegación (Partido.DelegacionId)
    public virtual ICollection<Partido> Partidos { get; set; } = new List<Partido>();
}
