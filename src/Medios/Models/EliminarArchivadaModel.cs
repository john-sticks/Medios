namespace Medios.Models;

public sealed class EliminarArchivadaModel
{
    public Medios.Entities.SesionPrensa Sesion { get; init; } = null!;
    public List<int> NotaIds { get; init; } = new();
    public List<int> VersionIds { get; init; } = new();
    public List<int> SintesisIds { get; init; } = new();
    public List<string> Bloqueos { get; init; } = new();
    public string Token { get; set; } = "";
    public bool PuedeEliminar => Sesion.Estado == "Archivada" && Bloqueos.Count == 0;
}
