using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Medios.Entities;

[Table("auditoria")]
[Index("Usuario", Name = "idx_usuario")]
[Index("Fecha", Name = "idx_fecha")]
[Index("Tabla", "Accion", Name = "idx_tabla_accion")]
public class Auditoria
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("fecha", TypeName = "datetime")]
    public DateTime Fecha { get; set; } = DateTime.Now;

    [Column("usuario")]
    [StringLength(100)]
    public string Usuario { get; set; } = "";

    [Column("ip")]
    [StringLength(50)]
    public string Ip { get; set; } = "";

    [Column("tabla")]
    [StringLength(50)]
    public string Tabla { get; set; } = "";

    [Column("tipo")]
    [StringLength(10)]
    public string Tipo { get; set; } = "";

    [Column("accion")]
    [StringLength(30)]
    public string Accion { get; set; } = "";

    [Column("detalle", TypeName = "json")]
    public string? Detalle { get; set; }
}
