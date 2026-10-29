using Medios.Entities;
using Microsoft.EntityFrameworkCore;

namespace Medios.Services;

public partial class SesionService
{
    public static bool PuedeAdministrarArchivo(string rol) => rol is "MEDIOS" or "DESARROLLADOR";

    public Task<bool> ArchivarAsync(int id, string usuario, string rol, string motivo) =>
        CambiarArchivoAsync(id, usuario, rol, motivo, archivar: true);

    public Task<bool> RestaurarArchivadaAsync(int id, string usuario, string rol) =>
        CambiarArchivoAsync(id, usuario, rol, null, archivar: false);

    public static bool PuedeQuitarSinAptas(SesionPrensa sesion) =>
        sesion.Estado is "Finalizada" or "Archivada" && sesion.SintesisConsolidadaId == null
        && sesion.Notas.All(n => SintesisWorkflowPolicy.EsDescartada(n.VersionActual));

    public async Task<bool> QuitarSinAptasAsync(int id, string usuario, string rol)
    {
        if (!PuedeAdministrarArchivo(rol) || string.IsNullOrWhiteSpace(usuario) || usuario.Length > 100) return false;
        using var db = _factory.Create();
        var sesion = await db.SesionesPrensas.Include(s => s.Notas).ThenInclude(n => n.VersionActual)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (sesion == null || !PuedeQuitarSinAptas(sesion)) return false;
        sesion.Estado = "Eliminada";
        sesion.FechaArchivo = DateTime.Now;
        sesion.UsuarioArchivo = usuario;
        sesion.MotivoArchivo = "Sin notas aprobadas. Se conservan las notas devueltas y su recorrido.";
        db.TrazaEventos.Add(new TrazaEventoEntity { SesionId = id, Usuario = usuario, Rol = rol,
            Estado = "Eliminada", Evento = "SESION_SIN_APTAS_ELIMINADA", Detalle = sesion.MotivoArchivo });
        foreach (var nota in sesion.Notas)
            db.TrazaEventos.Add(new TrazaEventoEntity { SesionId = id, NotaId = nota.Id,
                Usuario = usuario, Rol = rol, Estado = nota.VersionActual?.EstadoRevision,
                Evento = "SESION_SIN_APTAS_ELIMINADA", Detalle = "La síntesis de origen fue quitada sin notas aprobadas. La noticia permanece disponible para la delegación." });
        try { await db.SaveChangesAsync(); return true; }
        catch (DbUpdateConcurrencyException) { return false; }
    }

    private async Task<bool> CambiarArchivoAsync(int id, string usuario, string rol, string? motivo, bool archivar)
    {
        motivo = motivo?.Trim();
        if (!PuedeAdministrarArchivo(rol) || string.IsNullOrWhiteSpace(usuario) || usuario.Length > 100
            || (archivar && (string.IsNullOrWhiteSpace(motivo) || motivo.Length > 500))) return false;

        using var db = _factory.Create();
        var sesion = await db.SesionesPrensas.FindAsync(id);
        if (sesion == null || sesion.Estado != (archivar ? "Finalizada" : "Archivada")
            || sesion.SintesisConsolidadaId != null) return false;

        sesion.Estado = archivar ? "Archivada" : "Finalizada";
        if (archivar)
        {
            sesion.FechaArchivo = DateTime.Now;
            sesion.UsuarioArchivo = usuario;
            sesion.MotivoArchivo = motivo;
        }
        // La traza se guarda en la misma transacción que el cambio. Las notas,
        // snapshots, PDF y remisiones permanecen intactos, incluso al restaurar.
        db.TrazaEventos.Add(new TrazaEventoEntity
        {
            SesionId = id, Usuario = usuario, Rol = rol, Estado = sesion.Estado,
            Evento = archivar ? "SESION_ARCHIVADA" : "SESION_RESTAURADA",
            Detalle = archivar ? motivo : "Restaurada a Finalizadas; se conserva el último motivo de archivo."
        });
        try { await db.SaveChangesAsync(); return true; }
        catch (DbUpdateConcurrencyException) { return false; }
    }
}
