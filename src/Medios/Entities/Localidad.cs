using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Medios.Entities;

[Table("localidades_prometheus")]
public class Localidad
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("nombre")]
    [StringLength(50)]
    public string Nombre { get; set; } = "";

    [Column("partido_id")]
    public int PartidoId { get; set; }

    [ForeignKey("PartidoId")]
    public virtual Partido Partido { get; set; } = null!;
}
