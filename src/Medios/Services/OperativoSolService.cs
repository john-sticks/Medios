using System.Text.Json;
using Medios.Entities;
using Medios.Infrastructure.Data;
using Medios.Models;
using Microsoft.EntityFrameworkCore;
namespace Medios.Services;

// Una configuración por operativo: queda incluida en los respaldos y respeta la base elegida en test.
public class OperativoSolService(IMediosDbContextFactory factory)
{
    private const string Prefijo = "operativo_sol:";
    public async Task<List<OperativoSolModel>> ListarAsync()
    {
        using var db = factory.Create();
        var valores = await db.Configuraciones.AsNoTracking().Where(c => c.Clave.StartsWith(Prefijo)).ToListAsync();
        return valores.Select(c => JsonSerializer.Deserialize<OperativoSolModel>(c.Valor!)
                ?? throw new InvalidOperationException("Configuración de Operativo SOL inválida."))
            .OrderByDescending(o => o.FechaDesde).ThenBy(o => o.Descripcion).ToList();
    }
    public async Task GuardarAsync(OperativoSolModel modelo)
    {
        if (string.IsNullOrWhiteSpace(modelo.Descripcion) || modelo.Descripcion.Trim().Length > 150 || !modelo.FechaDesde.HasValue)
            throw new ArgumentException("Completá la descripción y la fecha desde.");
        if (modelo.FechaHasta < modelo.FechaDesde)
            throw new ArgumentException("La fecha hasta debe ser igual o posterior a la fecha desde.");
        modelo.PartidoIds = modelo.PartidoIds.Distinct().OrderBy(id => id).ToList();
        if (modelo.PartidoIds.Count == 0) throw new ArgumentException("Seleccioná al menos un partido.");
        using var db = factory.Create();
        if (await db.Partidos.CountAsync(p => modelo.PartidoIds.Contains(p.IdPartido)) != modelo.PartidoIds.Count)
            throw new ArgumentException("La selección contiene partidos inexistentes.");
        Configuracion? registro = null;
        if (modelo.Id != Guid.Empty)
            registro = await db.Configuraciones.FindAsync(Prefijo + modelo.Id.ToString("N"))
                ?? throw new ArgumentException("El operativo ya no existe. Actualizá la vista.");
        else
        {
            modelo.Id = Guid.NewGuid();
            registro = new Configuracion { Clave = Prefijo + modelo.Id.ToString("N") };
            db.Configuraciones.Add(registro);
        }
        modelo.Descripcion = modelo.Descripcion.Trim();
        registro.Valor = JsonSerializer.Serialize(modelo);
        await db.SaveChangesAsync();
    }
    public async Task<HashSet<int>> PartidosVigentesAsync(DateOnly fecha) =>
        (await ListarAsync()).Where(o => o.Vigente(fecha)).SelectMany(o => o.PartidoIds).ToHashSet();
}
