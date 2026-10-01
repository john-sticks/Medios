using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Medios.Entities;

[Table("superintendencias_prometheus")]
public class AreaResponsabilidad
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("nombre")]
    [StringLength(50)]
    public string AmbitoResponsabilidad { get; set; } = "";

    public virtual ICollection<Partido> Partidos { get; set; } = new List<Partido>();
}
