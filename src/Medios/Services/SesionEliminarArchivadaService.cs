using System.Data;
using System.Security.Cryptography;
using System.Text;
using Medios.Entities;
using Medios.Models;
using Microsoft.EntityFrameworkCore;

namespace Medios.Services;

public partial class SesionService
{
    public async Task<EliminarArchivadaModel?> AnalizarEliminacionArchivadaAsync(int id)
    {
        using var db = _factory.Create();
        return await AnalizarEliminacionAsync(db, id);
    }

    private static async Task<EliminarArchivadaModel?> AnalizarEliminacionAsync(MediosDbContext db, int id)
    {
        var sesion = await db.SesionesPrensas.Include(s => s.Delegacion).Include(s => s.Notas)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (sesion == null) return null;
        var notas = sesion.Notas.Select(n => n.Id).Order().ToList();
        var versiones = await db.NotasVersion.Where(v => notas.Contains(v.NotaId)).OrderBy(v => v.Id).ToListAsync();
        var versionIds = versiones.Select(v => v.Id).ToList();
        var referencias = await db.SintesisNotas.Include(sn => sn.Sintesis).ThenInclude(s => s.NotasIncluidas)
            .Where(sn => notas.Contains(sn.NotaPrensaId) || versionIds.Contains(sn.NotaVersionId)).ToListAsync();
        // Solo puede acompañar al borrado la publicación propia de estas mismas
        // notas. Nunca una consolidada, informada, filtrada o de otra delegación.
        var propias = referencias.Select(sn => sn.Sintesis).DistinctBy(s => s.Id)
            .Where(s => s.DelegacionId.HasValue && s.DelegacionId == sesion.DelegacionId
                && s.Fecha == sesion.Fecha && s.Tipo == sesion.Turno && !s.EsFiltrada
                && s.Estado is "Borrador" or "Generada" or "Remitida"
                && s.NotasIncluidas.All(sn => notas.Contains(sn.NotaPrensaId) && versionIds.Contains(sn.NotaVersionId)))
            .Select(s => s.Id).Order().ToList();
        var actuales = sesion.Notas.Select(n => n.VersionActualId).ToHashSet();
        var devueltas = versiones.Any(v => actuales.Contains(v.Id) && v.EstadoRevision is "Descartada" or "Sin Remitir");
        var bloqueos = new List<string>();
        if (versiones.Any(v => actuales.Contains(v.Id) && v.EstadoRevision is not ("Aprobada" or "Finalizada" or "Agregada" or "Descartada" or "Sin Remitir") && !(sesion.Delegacion?.Nombre == "División Medios" && v.EstadoRevision is "Borrador" or "Remitida")))
            bloqueos.Add("Hay notas pendientes de revisar; no pueden eliminarse por este flujo.");
        if (devueltas) bloqueos.Add("Hay notas devueltas a la delegación. Deben conservarse para su corrección o eliminación por la delegación. Si no hay notas aprobadas, usá Quitar síntesis sin notas aptas.");
        if (sesion.Estado != "Archivada" || sesion.SintesisConsolidadaId != null)
            bloqueos.Add("Solo se pueden eliminar síntesis archivadas sin consolidar.");
        if (referencias.Any(sn => !propias.Contains(sn.SintesisId)))
            bloqueos.Add("Hay notas o versiones utilizadas por otra síntesis, una filtrada o una publicación informada.");
        if (await db.Sintesis.AnyAsync(s => s.SintesisOrigenId.HasValue && propias.Contains(s.SintesisOrigenId.Value))
            || await db.SesionesPrensas.AnyAsync(s => s.SintesisConsolidadaId.HasValue && propias.Contains(s.SintesisConsolidadaId.Value)))
            bloqueos.Add("Una publicación vinculada tiene síntesis derivadas o sesiones asociadas.");
        if (sesion.Notas.Any(n => n.NotaOrigenId.HasValue && !notas.Contains(n.NotaOrigenId.Value))
            || await db.NotasPrensa.AnyAsync(n => !notas.Contains(n.Id) && n.NotaOrigenId.HasValue && notas.Contains(n.NotaOrigenId.Value)))
            bloqueos.Add("Una noticia comparte su origen o tiene copias fuera de esta síntesis.");
        if (versiones.Any(v => v.ParentVersionId.HasValue && !versionIds.Contains(v.ParentVersionId.Value))
            || await db.NotasVersion.AnyAsync(v => !versionIds.Contains(v.Id) && v.ParentVersionId.HasValue && versionIds.Contains(v.ParentVersionId.Value))
            || await db.NotasPrensa.AnyAsync(n => !notas.Contains(n.Id) && n.VersionActualId.HasValue && versionIds.Contains(n.VersionActualId.Value)))
            bloqueos.Add("El historial de versiones está vinculado a otra noticia.");
        if (await db.NotasRelaciones.AnyAsync(n => (notas.Contains(n.NotaId) && !notas.Contains(n.NotaRelacionadaId))
            || (!notas.Contains(n.NotaId) && notas.Contains(n.NotaRelacionadaId))))
            bloqueos.Add("Una nota está relacionada con otra noticia fuera de esta síntesis.");
        var model = new EliminarArchivadaModel { Sesion = sesion, NotaIds = notas, VersionIds = versionIds,
            SintesisIds = propias, Bloqueos = bloqueos };
        // Una confirmación vieja no puede borrar notas añadidas o corregidas después.
        var firma = $"{sesion.Id}|{sesion.Estado}|{sesion.FechaArchivo:O}|{string.Join(',', notas)}|"
            + string.Join(';', versiones.Select(v => $"{v.Id}:{v.FechaVersion:O}:{v.TipoEvento}:{v.EstadoRevision}:{v.Titulo}:{v.Texto}:{v.Sintesis}"))
            + $"|{string.Join(',', propias)}";
        model.Token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(firma)));
        return model;
    }

    public async Task<bool> EliminarArchivadaAsync(int id, string usuario, string rol, string? token, bool confirmado)
    {
        if (!PuedeAdministrarArchivo(rol) || !confirmado || string.IsNullOrWhiteSpace(usuario) || usuario.Length > 100
            || string.IsNullOrEmpty(token)) return false;
        using var strategyDb = _factory.Create();
        return await strategyDb.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            using var db = _factory.Create();
            await using var tx = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;
            var analisis = await AnalizarEliminacionAsync(db, id);
            if (analisis == null || !analisis.PuedeEliminar || analisis.Token != token) return false;
            var notas = analisis.Sesion.Notas.ToList();
            var versiones = await db.NotasVersion.Where(v => analisis.VersionIds.Contains(v.Id)).ToListAsync();
            var snapshots = await db.SintesisNotas.Where(sn => analisis.SintesisIds.Contains(sn.SintesisId)).ToListAsync();
            db.SintesisNotas.RemoveRange(snapshots);
            db.NotasRelaciones.RemoveRange(await db.NotasRelaciones
                .Where(n => analisis.NotaIds.Contains(n.NotaId) || analisis.NotaIds.Contains(n.NotaRelacionadaId)).ToListAsync());
            foreach (var nota in notas) { nota.VersionActualId = null; nota.NotaOrigenId = null; }
            foreach (var version in versiones) version.ParentVersionId = null;
            await db.SaveChangesAsync();
            db.NotasVersion.RemoveRange(versiones);
            await db.SaveChangesAsync();
            db.NotasPrensa.RemoveRange(notas);
            db.Sintesis.RemoveRange(await db.Sintesis.Where(s => analisis.SintesisIds.Contains(s.Id)).ToListAsync());
            db.SesionesPrensas.Remove(analisis.Sesion);
            db.TrazaEventos.Add(new TrazaEventoEntity
            {
                SesionId = id, Usuario = usuario, Rol = rol, Estado = "Eliminada", Evento = "SESION_ELIMINADA",
                Detalle = $"Borrado definitivo confirmado: {notas.Count} notas, {versiones.Count} versiones; publicaciones {string.Join(',', analisis.SintesisIds)}. Auditoría y archivos físicos conservados."
            });
            try
            {
                await db.SaveChangesAsync();
                if (tx != null) await tx.CommitAsync();
                return true;
            }
            catch (DbUpdateConcurrencyException) { return false; }
        });
    }
}
