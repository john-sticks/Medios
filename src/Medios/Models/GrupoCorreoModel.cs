using System.ComponentModel.DataAnnotations;

namespace Medios.Models;

public class GrupoCorreoModel
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Ingresá un nombre para el grupo."), StringLength(120)]
    public string Nombre { get; set; } = "";
    public bool Activo { get; set; } = true;
    public List<ContactoCorreoModel> Contactos { get; set; } = new();
}

public class ContactoCorreoModel
{
    [Required(ErrorMessage = "Ingresá el nombre del contacto."), StringLength(100)]
    public string Nombre { get; set; } = "";
    [Required(ErrorMessage = "Ingresá el apellido del contacto."), StringLength(100)]
    public string Apellido { get; set; } = "";
    [Required(ErrorMessage = "Ingresá el correo del contacto."), EmailAddress(ErrorMessage = "Revisá el formato del correo."), StringLength(254)]
    public string Email { get; set; } = "";
}
