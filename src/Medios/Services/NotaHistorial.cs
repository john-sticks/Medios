using Medios.Entities;
using Microsoft.EntityFrameworkCore;

namespace Medios.Services;

public partial class NotaService
{
    // Lectura del linaje: no reasigna IDs, versiones ni snapshots existentes.
    public async Task<NotaHistorial> GetHistorialNoticiaAsync(int notaId)
    {
        using var db = _factory.Create();
        var inicio = await db.NotasPrensa.Where(n => n.Id == notaId)
            .Select(n => new { n.Id, n.NotaOrigenId,
                DelegacionId = n.Sesion != null ? n.Sesion.DelegacionId : n.DelegacionId })
            .FirstOrDefaultAsync();
        if (inicio == null) return new();

        var raiz = inicio.Id;
        var padreId = inicio.NotaOrigenId;
        var visitados = new HashSet<int> { inicio.Id };
        while (inicio.DelegacionId.HasValue && padreId.HasValue && visitados.Add(padreId.Value))
        {
            var padre = await db.NotasPrensa.Where(n => n.Id == padreId.Value
                && (n.Sesion != null ? n.Sesion.DelegacionId : n.DelegacionId) == inicio.DelegacionId)
                .Select(n => new { n.Id, n.NotaOrigenId }).FirstOrDefaultAsync();
            if (padre == null) break;
            raiz = padre.Id;
            padreId = padre.NotaOrigenId;
        }

        var ids = new HashSet<int> { raiz };
        var pendientes = new List<int> { raiz };
        while (inicio.DelegacionId.HasValue && pendientes.Count > 0)
        {
            var hijos = await db.NotasPrensa.Where(n => n.NotaOrigenId.HasValue
                && pendientes.Contains(n.NotaOrigenId.Value)
                && (n.Sesion != null ? n.Sesion.DelegacionId : n.DelegacionId) == inicio.DelegacionId)
                .Select(n => n.Id).ToListAsync();
            pendientes = hijos.Where(id => ids.Add(id)).ToList();
        }

        var versiones = await db.NotasVersion.AsNoTracking()
            .Include(v => v.Categoria).Include(v => v.Partido).Include(v => v.Localidad)
            .Where(v => ids.Contains(v.NotaId))
            .OrderBy(v => v.FechaVersion).ThenBy(v => v.Id).ToListAsync();
        var versionIds = versiones.Select(v => v.Id).ToList();
        var referencias = await db.SintesisNotas.AsNoTracking().Include(sn => sn.Sintesis)
            .Where(sn => versionIds.Contains(sn.NotaVersionId))
            .OrderBy(sn => sn.Sintesis.FechaCreacion).ThenBy(sn => sn.SintesisId).ToListAsync();
        var eventos = await db.TrazaEventos.AsNoTracking()
            .Where(t => t.NotaId.HasValue && ids.Contains(t.NotaId.Value))
            .OrderBy(t => t.Fecha).ThenBy(t => t.Id).ToListAsync();
        var sesionIds = eventos.Where(t => t.SesionId.HasValue).Select(t => t.SesionId!.Value).Distinct().ToList();
        var sesiones = await db.SesionesPrensas.AsNoTracking()
            .Where(s => sesionIds.Contains(s.Id) && s.DelegacionId == inicio.DelegacionId)
            .ToDictionaryAsync(s => s.Id);

        return new NotaHistorial
        {
            NotaOriginalId = raiz,
            NotaVigenteId = versiones.LastOrDefault()?.NotaId ?? notaId,
            Versiones = versiones,
            SintesisPorVersion = referencias.GroupBy(sn => sn.NotaVersionId)
                .ToDictionary(g => g.Key, g => g.Select(sn => sn.Sintesis).DistinctBy(s => s.Id).ToList()),
            Eventos = eventos,
            Sesiones = sesiones
        };
    }
}

public sealed class NotaHistorial
{
    public int NotaOriginalId { get; init; }
    public int NotaVigenteId { get; init; }
    public List<NotaVersion> Versiones { get; init; } = new();
    public Dictionary<int, List<Sintesis>> SintesisPorVersion { get; init; } = new();
    public List<TrazaEventoEntity> Eventos { get; init; } = new();
    public Dictionary<int, SesionPrensa> Sesiones { get; init; } = new();
}
