using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Medios.Entities;

[Table("portales_prensa")]
public class PortalPrensa
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string Nombre { get; set; } = "";

    [Required, MaxLength(255)]
    public string Url { get; set; } = "";

    [MaxLength(100)]
    public string? Region { get; set; }

    public int? PartidoId { get; set; }

    [ForeignKey("PartidoId")]
    public virtual Partido? Partido { get; set; }

    public bool Activo { get; set; } = true;
}
