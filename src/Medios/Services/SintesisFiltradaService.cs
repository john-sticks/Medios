using Medios.Entities;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace Medios.Services;

public partial class SintesisService
{
    public static bool PuedeCrearFiltrada([NotNullWhen(true)] Sintesis? origen) =>
        origen is { DelegacionId: null, Estado: "Generada" };

    public async Task<bool> TieneFiltradasAsync(int origenId)
    {
        using var db = _factory.Create();
        return await db.Sintesis.AnyAsync(s => s.SintesisOrigenId == origenId);
    }

    public async Task<(int Id, string? Error)> CrearFiltradaAsync(int origenId,
        IEnumerable<int> seleccionIds, string operador)
    {
        using var db = _factory.Create();
        var origen = await db.Sintesis.AsNoTracking().Include(s => s.NotasIncluidas)
            .SingleOrDefaultAsync(s => s.Id == origenId);
        if (!PuedeCrearFiltrada(origen))
            return (0, "Elegí una síntesis consolidada generada y pendiente de informar.");
        var ids = seleccionIds.ToHashSet();
        if (ids.Count == 0)
            return (0, "Seleccioná al menos una nota.");
        var seleccion = origen.NotasIncluidas.Where(sn => ids.Contains(sn.Id)).OrderBy(sn => sn.Orden).ThenBy(sn => sn.Id).ToList();
        if (seleccion.Count != ids.Count)
            return (0, "La selección contiene notas que no pertenecen a la síntesis de origen.");

        var nueva = new Sintesis
        {
            SintesisOrigenId = origen.Id, Fecha = origen.Fecha, Tipo = origen.Tipo,
            Estado = "Borrador", OperadorGenera = operador, FechaCreacion = DateTime.Now
        };
        foreach (var sn in seleccion)
            nueva.NotasIncluidas.Add(new SintesisNota
            {
                NotaPrensaId = sn.NotaPrensaId, NotaVersionId = sn.NotaVersionId,
                Orden = nueva.NotasIncluidas.Count
            });
        // Las versiones se referencian tal como quedaron en el origen; no se copian ni se actualizan.
        db.Sintesis.Add(nueva);
        await db.SaveChangesAsync();
        return (nueva.Id, null);
    }
}
