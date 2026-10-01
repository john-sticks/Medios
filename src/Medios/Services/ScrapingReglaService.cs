using Microsoft.EntityFrameworkCore;
using Medios.Entities;
using Medios.Infrastructure.Data;

namespace Medios.Services
{
    public class ScrapingReglaService
    {
        private readonly IMediosDbContextFactory _factory;

        public ScrapingReglaService(IMediosDbContextFactory factory)
        {
            _factory = factory;
        }

        public async Task<List<ScrapingRegla>> GetTodasAsync()
        {
            using var db = _factory.Create();
            return await db.ScrapingReglas.OrderBy(r => r.Dominio).ToListAsync();
        }

        public async Task<ScrapingRegla?> GetByIdAsync(int id)
        {
            using var db = _factory.Create();
            return await db.ScrapingReglas.FindAsync(id);
        }

        // Regla activa para un dominio (normalizado sin www)
        public async Task<ScrapingRegla?> GetByDominioAsync(string dominio)
        {
            using var db = _factory.Create();
            return await db.ScrapingReglas
                .FirstOrDefaultAsync(r => r.Activo && r.Dominio == dominio);
        }

        public async Task<(bool Ok, string? Error)> GuardarAsync(ScrapingRegla regla)
        {
            using var db = _factory.Create();

            regla.Dominio = NormalizarDominio(regla.Dominio);
            if (string.IsNullOrWhiteSpace(regla.Dominio))
                return (false, "El dominio es obligatorio.");

            // Evitar dominios duplicados
            var dup = await db.ScrapingReglas
                .AnyAsync(r => r.Dominio == regla.Dominio && r.Id != regla.Id);
            if (dup) return (false, $"Ya existe una regla para el dominio '{regla.Dominio}'.");

            regla.FechaActualizacion = DateTime.Now;

            if (regla.Id == 0)
            {
                db.ScrapingReglas.Add(regla);
            }
            else
            {
                var existente = await db.ScrapingReglas.FindAsync(regla.Id);
                if (existente == null) return (false, "Regla no encontrada.");
                existente.Dominio = regla.Dominio;
                existente.XPathTitulo = Vacio(regla.XPathTitulo);
                existente.XPathCuerpo = Vacio(regla.XPathCuerpo);
                existente.ClasesExcluir = Vacio(regla.ClasesExcluir);
                existente.PreferirJsonLd = regla.PreferirJsonLd;
                existente.Activo = regla.Activo;
                existente.Descripcion = Vacio(regla.Descripcion);
                existente.FechaActualizacion = DateTime.Now;
            }
            await db.SaveChangesAsync();
            return (true, null);
        }

        public async Task EliminarAsync(int id)
        {
            using var db = _factory.Create();
            var r = await db.ScrapingReglas.FindAsync(id);
            if (r != null)
            {
                db.ScrapingReglas.Remove(r);
                await db.SaveChangesAsync();
            }
        }

        public static string NormalizarDominio(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var d = raw.Trim().ToLower();
            // Si viene una URL completa, extraer el host
            if (d.Contains("://") && Uri.TryCreate(d, UriKind.Absolute, out var uri))
                d = uri.Host;
            d = d.Replace("www.", "");
            return d.TrimEnd('/');
        }

        private static string? Vacio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
