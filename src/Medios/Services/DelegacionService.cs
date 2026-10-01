using Microsoft.EntityFrameworkCore;
using Medios.Entities;
using Medios.Infrastructure.Data;

namespace Medios.Services
{
    public class DelegacionService
    {
        private readonly IMediosDbContextFactory _factory;

        public DelegacionService(IMediosDbContextFactory factory)
        {
            _factory = factory;
        }

        public async Task<List<AreaResponsabilidad>> GetAreasAsync()
        {
            using var db = _factory.Create();
            return await db.AreasResponsabilidad.OrderBy(a => a.AmbitoResponsabilidad).ToListAsync();
        }

        public async Task<List<DelegacionPrometheus>> GetDelegacionesPrometheusAsync()
        {
            using var db = _factory.Create();
            return await db.DelegacionesPrometheus.OrderBy(d => d.Nombre).ToListAsync();
        }

        public async Task<List<Delegacion>> GetTodasAsync(bool? soloActivas = null)
        {
            using var db = _factory.Create();
            var q = db.Delegaciones
                .Include(d => d.AreaResponsabilidad)
                .Include(d => d.DelegacionPrometheus)
                .Include(d => d.Partidos)
                .AsQueryable();
            if (soloActivas.HasValue)
                q = q.Where(d => d.Activa == soloActivas.Value);
            return await q.OrderBy(d => d.Nombre).ToListAsync();
        }

        public async Task<Delegacion?> GetByIdAsync(int id)
        {
            using var db = _factory.Create();
            return await db.Delegaciones
                .Include(d => d.AreaResponsabilidad)
                .Include(d => d.DelegacionPrometheus)
                .FirstOrDefaultAsync(d => d.Id == id);
        }

        public async Task<int> CrearAsync(Delegacion d)
        {
            using var db = _factory.Create();
            db.Delegaciones.Add(d);
            await db.SaveChangesAsync();
            return d.Id;
        }

        public async Task<bool> ActualizarAsync(Delegacion d)
        {
            using var db = _factory.Create();
            var existente = await db.Delegaciones.FindAsync(d.Id);
            if (existente == null) return false;

            existente.Nombre = d.Nombre;
            existente.Jurisdiccion = d.Jurisdiccion;
            existente.AreaResponsabilidadId = d.AreaResponsabilidadId;
            existente.DelegacionPrometheusId = d.DelegacionPrometheusId;
            existente.PartidoId = d.PartidoId;
            existente.Email = d.Email;
            existente.UsuarioCerberus = d.UsuarioCerberus;
            existente.Activa = d.Activa;
            existente.PortalesPrensa = d.PortalesPrensa;
            await db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ActualizarPortalesAsync(int id, string? portalesJson)
        {
            using var db = _factory.Create();
            var d = await db.Delegaciones.FindAsync(id);
            if (d == null) return false;
            d.PortalesPrensa = portalesJson;
            await db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ActualizarEmailAsync(int id, string? email)
        {
            using var db = _factory.Create();
            var d = await db.Delegaciones.FindAsync(id);
            if (d == null) return false;
            d.Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
            await db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ActualizarDriveSheetsAsync(int id, string? notas, string? sintesis)
        {
            using var db = _factory.Create();
            var d = await db.Delegaciones.FindAsync(id);
            if (d == null) return false;
            d.DriveSheetNotas = string.IsNullOrWhiteSpace(notas) ? null : notas.Trim();
            d.DriveSheetSintesis = string.IsNullOrWhiteSpace(sintesis) ? null : sintesis.Trim();
            await db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ToggleActivaAsync(int id)
        {
            using var db = _factory.Create();
            var d = await db.Delegaciones.FindAsync(id);
            if (d == null) return false;
            d.Activa = !d.Activa;
            await db.SaveChangesAsync();
            return d.Activa;
        }
    }
}
