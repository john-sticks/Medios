using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Medios.Entities;

[Table("autorizaciones_usuario")]
[Index("Usuario", Name = "idx_usuario")]
[Index("Estado", Name = "idx_estado")]
public class AutorizacionUsuario
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("usuario")]
    [StringLength(100)]
    public string Usuario { get; set; } = "";

    [Column("nombre")]
    [StringLength(200)]
    public string Nombre { get; set; } = "";

    [Column("email")]
    [StringLength(200)]
    public string? Email { get; set; }

    [Column("destino")]
    [StringLength(300)]
    public string? Destino { get; set; }

    [Column("jerarquia")]
    [StringLength(100)]
    public string? Jerarquia { get; set; }

    // Rol asignado por el administrador (sobrescribe el rol del proveedor de auth)
    [Column("rol")]
    [StringLength(20)]
    public string? Rol { get; set; }

    [Column("legajo")]
    [StringLength(50)]
    public string? Legajo { get; set; }

    [Column("telefono")]
    [StringLength(50)]
    public string? Telefono { get; set; }

    [Column("codigo")]
    [StringLength(6)]
    public string Codigo { get; set; } = "";

    [Column("estado")]
    [StringLength(20)]
    public string Estado { get; set; } = "pendiente";

    // Acceso activo/suspendido (solo aplica a usuarios aprobados)
    [Column("activo")]
    public bool Activo { get; set; } = true;

    [Column("fecha_solicitud", TypeName = "datetime")]
    public DateTime FechaSolicitud { get; set; } = DateTime.Now;

    [Column("fecha_resolucion", TypeName = "datetime")]
    public DateTime? FechaResolucion { get; set; }

    [Column("aprobado_por")]
    [StringLength(100)]
    public string? AprobadoPor { get; set; }

    // Ámbito asignado al aprobar (solo uno de los dos aplica según el rol)
    [Column("delegacion_id")]
    public int? DelegacionId { get; set; }

    [Column("ambito_id")]
    public int? AmbitoId { get; set; }

    [ForeignKey("DelegacionId")]
    public virtual Delegacion? Delegacion { get; set; }

    [ForeignKey("AmbitoId")]
    public virtual AreaResponsabilidad? Ambito { get; set; }
}
