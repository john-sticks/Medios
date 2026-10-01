using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Medios.Entities;

[Table("nota_relaciones")]
[Index("NotaId", Name = "idx_relacion_nota")]
[Index("NotaRelacionadaId", Name = "idx_relacion_relacionada")]
public class NotaRelacion
{
    [Key]
    public int Id { get; set; }

    public int NotaId { get; set; }

    public int NotaRelacionadaId { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime FechaRelacion { get; set; } = DateTime.Now;

    [MaxLength(100)]
    public string Usuario { get; set; } = "";

    [ForeignKey("NotaId")]
    public virtual NotaPrensa Nota { get; set; } = null!;

    [ForeignKey("NotaRelacionadaId")]
    public virtual NotaPrensa NotaRelacionada { get; set; } = null!;
}
