using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Medios.Entities;

[Table("caratulas_quiron")]
public class CaratulaQuiron
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("nombre")]
    [StringLength(100)]
    public string Nombre { get; set; } = "";

    [Column("consumable")]
    public bool Consumable { get; set; }

    [Column("calificable")]
    public bool Calificable { get; set; }

    public virtual ICollection<ModalidadQuiron> Modalidades { get; set; } = new List<ModalidadQuiron>();
}

[Table("modalidades_quiron")]
public class ModalidadQuiron
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("nombre")]
    [StringLength(100)]
    public string Nombre { get; set; } = "";

    [Column("caratula_id")]
    public int CaratulaId { get; set; }

    [ForeignKey("CaratulaId")]
    public virtual CaratulaQuiron Caratula { get; set; } = null!;
}
