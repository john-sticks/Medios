using Medios.Entities;
using Microsoft.EntityFrameworkCore;

namespace Medios.Services;

public partial class SesionService
{
    public async Task<bool> ReabrirRevisionAsync(int id, string usuario, string rol)
    {
        if (!PuedeAdministrarArchivo(rol) || string.IsNullOrWhiteSpace(usuario) || usuario.Length > 100) return false;
        using var db = _factory.Create();
        var sesion = await db.SesionesPrensas.Include(s => s.Notas).ThenInclude(n => n.VersionActual)
            .Include(s => s.Notas).ThenInclude(n => n.Versiones).FirstOrDefaultAsync(s => s.Id == id);
        if (sesion == null || sesion.Estado != "Finalizada" || sesion.SintesisConsolidadaId != null || sesion.Notas.Count == 0) return false;
        var ids = sesion.Notas.Select(n => n.Id).ToList();
        // No reabrir notas que ya forman parte de una consolidada o publicación informada.
        if (await db.SintesisNotas.AnyAsync(sn => ids.Contains(sn.NotaPrensaId)
            && (sn.Sintesis.DelegacionId == null || sn.Sintesis.Estado == "Informada"))) return false;

        foreach (var nota in sesion.Notas)
        {
            var anterior = nota.VersionActual;
            if (anterior == null || SintesisWorkflowPolicy.EsDescartada(anterior)
                || !string.IsNullOrWhiteSpace(anterior.MotivoDescarte)) continue;
            if (anterior.EstadoRevision is not ("Aprobada" or "Finalizada" or "Agregada" or "Remitida" or "Borrador" or "Pendiente" or "Sin Remitir")) continue;
            var nueva = (NotaVersion)db.Entry(anterior).CurrentValues.Clone().ToObject();
            nueva.Id = 0; nueva.Nota = nota; nueva.ParentVersionId = anterior.Id;
            nueva.Version = nota.Versiones.Max(v => v.Version) + 1;
            nueva.EsActual = true; nueva.EstadoRevision = "Pendiente"; nueva.OperadorRevision = null;
            nueva.FechaVersion = DateTime.Now; nueva.Usuario = usuario;
            nueva.TipoEvento = "Reapertura";
            nueva.ResumenCambio = "Síntesis devuelta a Procesando. Requiere una nueva aprobación.";
            anterior.EsActual = false;
            nota.VersionActual = nueva;
            db.NotasVersion.Add(nueva);
            db.TrazaEventos.Add(new TrazaEventoEntity { SesionId = id, NotaId = nota.Id, Version = nueva.Version,
                Usuario = usuario, Rol = rol, Estado = "Pendiente", Evento = "NOTA_REABIERTA_REVISION", Detalle = nueva.ResumenCambio });
        }
        sesion.Estado = "Remitida";
        db.TrazaEventos.Add(new TrazaEventoEntity { SesionId = id, Usuario = usuario, Rol = rol,
            Estado = "Remitida", Evento = "SESION_REABIERTA_REVISION",
            Detalle = "Devuelta a Procesando. Las aprobadas requieren nueva aprobación; las denegadas conservan su estado." });
        // Un único SaveChanges: EF guarda notas, versiones, estado y trazas atómicamente.
        try { await db.SaveChangesAsync(); return true; }
        catch (DbUpdateConcurrencyException) { return false; }
    }

    internal static async Task<Dictionary<int, int>> VersionesReabiertasAsync(MediosDbContext db, List<int> notaIds)
    {
        return await db.TrazaEventos.Where(t => t.Evento == "NOTA_REABIERTA_REVISION"
                && t.NotaId.HasValue && notaIds.Contains(t.NotaId.Value) && t.Version.HasValue)
            .GroupBy(t => t.NotaId!.Value)
            .Select(g => new { NotaId = g.Key, Version = g.Max(t => t.Version!.Value) })
            .ToDictionaryAsync(x => x.NotaId, x => x.Version);
    }
}
