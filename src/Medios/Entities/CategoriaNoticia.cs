using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Medios.Entities;

[Table("categorias_noticia")]
public class CategoriaNoticia
{
    [Key]
    public int Id { get; set; }

    [StringLength(100)]
    public string Nombre { get; set; } = "";

    public int Orden { get; set; }

    public bool Activa { get; set; } = true;
}
