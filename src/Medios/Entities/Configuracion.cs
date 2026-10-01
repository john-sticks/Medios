using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Medios.Entities;

[Table("configuracion")]
public class Configuracion
{
    [Key]
    [StringLength(100)]
    [Column("clave")]
    public string Clave { get; set; } = "";

    [Column("valor", TypeName = "text")]
    public string? Valor { get; set; }
}
