using System.ComponentModel.DataAnnotations;
using Medios.Entities;
namespace Medios.Models;

public class OperativoSolModel
{
    public Guid Id { get; set; }
    [Required(ErrorMessage = "Ingresá una descripción."), StringLength(150)]
    public string Descripcion { get; set; } = "";
    [Required, DataType(DataType.Date)]
    public DateOnly? FechaDesde { get; set; }
    [DataType(DataType.Date)]
    public DateOnly? FechaHasta { get; set; }
    public bool Activo { get; set; } = true;
    public List<int> PartidoIds { get; set; } = new();
    public bool Vigente(DateOnly fecha) => Activo && FechaDesde.HasValue && fecha >= FechaDesde.Value
        && (!FechaHasta.HasValue || fecha <= FechaHasta.Value);
}

public class OperativosSolViewModel
{
    public OperativoSolModel Formulario { get; set; } = new();
    public List<OperativoSolModel> Operativos { get; set; } = new();
    public List<Partido> Partidos { get; set; } = new();
    public bool Expandido { get; set; }
}
