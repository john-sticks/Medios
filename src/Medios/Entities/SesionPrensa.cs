using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Medios.Entities;

[Table("sesiones_prensa")]
[Index("DelegacionId", "Fecha", "Turno", Name = "idx_sesion_delegacion_fecha")]
[Index("Estado", Name = "idx_sesion_estado")]
public class SesionPrensa
{
    [Key]
    public int Id { get; set; }

    public int? DelegacionId { get; set; }

    [ForeignKey("DelegacionId")]
    public virtual Delegacion? Delegacion { get; set; }

    [Column(TypeName = "date")]
    public DateOnly Fecha { get; set; }

    // Matutina | Vespertina | Ampliacion Matutina | Ampliacion Vespertina | Especial
    [StringLength(30)]
    public string Turno { get; set; } = "Vespertina";

    // Borrador | Remitida | Revisada
    [StringLength(20)]
    public string Estado { get; set; } = "Borrador";

    [Column(TypeName = "datetime")]
    public DateTime FechaCreacion { get; set; } = DateTime.Now;

    [Column(TypeName = "datetime")]
    public DateTime? FechaRemision { get; set; }

    [StringLength(500)]
    public string? PDFPath { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? FechaGeneracionPdf { get; set; }

    [StringLength(100)]
    public string UsuarioCarga { get; set; } = "";

    // Síntesis consolidada que absorbió esta sesión (para poder revertir la consolidación)
    public int? SintesisConsolidadaId { get; set; }

    public virtual ICollection<NotaPrensa> Notas { get; set; } = new List<NotaPrensa>();
}
