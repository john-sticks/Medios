using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Medios.Entities;

// Registro de trazabilidad del ciclo de vida de notas y síntesis.
// Solo guarda referencias y metadatos de cada movimiento (no el contenido de la nota).
[Table("traza_eventos")]
[Index("NotaId", Name = "idx_tz_nota")]
[Index("SesionId", Name = "idx_tz_sesion")]
[Index("SintesisId", Name = "idx_tz_sintesis")]
public class TrazaEventoEntity
{
    [Key]
    public int Id { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime Fecha { get; set; } = DateTime.Now;

    [StringLength(100)]
    public string Usuario { get; set; } = "";

    [StringLength(20)]
    public string? Rol { get; set; }

    // Referencias (según el tipo de evento)
    public int? NotaId { get; set; }
    public int? SesionId { get; set; }
    public int? SintesisId { get; set; }

    // NOTA_CREADA | NOTA_MODIFICADA | NOTA_AMPLIADA | NOTA_APROBADA | NOTA_DESCARTADA |
    // NOTA_ELIMINADA | NOTA_AGREGADA | SESION_REMITIDA | SESION_REVISADA | SESION_FINALIZADA |
    // SINTESIS_GENERADA | SINTESIS_PDF | SINTESIS_REMITIDA | SINTESIS_CONSOLIDADA |
    // SINTESIS_INFORMADA | CONSOLIDACION_REVERTIDA
    [StringLength(40)]
    public string Evento { get; set; } = "";

    public int? Version { get; set; }

    [StringLength(30)]
    public string? Estado { get; set; }

    // Resumen corto: motivo de descarte, tipo de síntesis, conteos, etc. (sin contenido)
    [StringLength(500)]
    public string? Detalle { get; set; }
}
