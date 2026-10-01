using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Medios.Entities;

[Table("delegaciones_prometheus")]
public class DelegacionPrometheus
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("nombre")]
    [StringLength(50)]
    public string Nombre { get; set; } = "";
}
