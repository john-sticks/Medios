using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Medios.Entities;

[Table("notas_prensa")]
[Index("SesionPrensaId", Name = "idx_nota_sesion")]
[Index("DelegacionId", Name = "idx_nota_delegacion")]
public class NotaPrensa
{
    [Key]
    public int Id { get; set; }

    public int? SesionPrensaId { get; set; }

    [ForeignKey("SesionPrensaId")]
    public virtual SesionPrensa? Sesion { get; set; }

    // Delegación directa — se asigna al crear notas libres (SesionPrensaId == null)
    public int? DelegacionId { get; set; }

    [ForeignKey("DelegacionId")]
    public virtual Delegacion? Delegacion { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime FechaRegistro { get; set; } = DateTime.Now;

    public int? VersionActualId { get; set; }

    [ForeignKey("VersionActualId")]
    public virtual NotaVersion? VersionActual { get; set; }

    // FK a la nota original cuando esta nota es una copia en borrador (AgregarDesdeVersionAsync)
    public int? NotaOrigenId { get; set; }

    [ForeignKey("NotaOrigenId")]
    public virtual NotaPrensa? NotaOrigen { get; set; }

    public virtual ICollection<NotaVersion> Versiones { get; set; } = new List<NotaVersion>();

    public virtual ICollection<NotaRelacion> Relaciones { get; set; } = new List<NotaRelacion>();
}
