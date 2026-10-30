using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Medios.Entities;

[Table("grupos_correo")]
public class GrupoCorreo
{
    [Key] public int Id { get; set; }
    [Required, StringLength(120)] public string Nombre { get; set; } = "";
    public bool Activo { get; set; } = true;
    public List<ContactoCorreo> Contactos { get; set; } = new();
}

[Table("contactos_correo")]
public class ContactoCorreo
{
    [Key] public int Id { get; set; }
    public int GrupoCorreoId { get; set; }
    [Required, StringLength(100)] public string Nombre { get; set; } = "";
    [Required, StringLength(100)] public string Apellido { get; set; } = "";
    [Required, StringLength(254)] public string Email { get; set; } = "";
    public GrupoCorreo Grupo { get; set; } = null!;
}
