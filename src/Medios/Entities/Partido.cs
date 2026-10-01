using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Medios.Entities;

[Table("partidos_prometheus")]
public class Partido
{
    [Key]
    [Column("id")]
    public int IdPartido { get; set; }

    [Column("nombre")]
    [StringLength(50)]
    public string Nombre { get; set; } = "";

    [Column("superintendencia_id")]
    public int IdAreaResponsabilidad { get; set; }

    [ForeignKey("IdAreaResponsabilidad")]
    public virtual AreaResponsabilidad AreaResponsabilidad { get; set; } = null!;

    [Column("delegacion_id")]
    public int DelegacionPrometheusId { get; set; }

    [ForeignKey("DelegacionPrometheusId")]
    public virtual DelegacionPrometheus DelegacionPrometheus { get; set; } = null!;

    [Column("subdelegacion_id")]
    public int? SubdelegacionId { get; set; }

    // Vínculo directo con la Delegación propia (delegaciones.Id) — fuente de verdad de
    // "qué partidos tiene una delegación", reemplaza el matching indirecto vía DelegacionPrometheusId.
    [Column("DelegacionId")]
    public int? DelegacionId { get; set; }

    [ForeignKey("DelegacionId")]
    public virtual Delegacion? Delegacion { get; set; }
}
