using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Medios.Entities;
using Medios.Infrastructure.Data;

namespace Medios.Services
{
    public class AuditoriaService
    {
        private readonly IMediosDbContextFactory _factory;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuditoriaService(IMediosDbContextFactory factory, IHttpContextAccessor httpContextAccessor)
        {
            _factory = factory;
            _httpContextAccessor = httpContextAccessor;
        }

        // Si la request actual está simulando un rol (MOCK), deja constancia de quién es el
        // usuario real detrás de la simulación — para que un registro de auditoría generado
        // "como" una Delegación simulada no quede indistinguible de una Delegación real.
        private object? ConSimulacion(object? detalle)
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null || !user.HasClaim("EsSimulador", "true")) return detalle;
            var usuarioReal = user.FindFirst("UsuarioReal")?.Value;
            var usuarioActual = user.FindFirst("Usuario")?.Value;
            var rolReal = user.FindFirst("RolReal")?.Value;
            if (string.IsNullOrEmpty(usuarioReal) || string.Equals(usuarioReal, usuarioActual, StringComparison.OrdinalIgnoreCase))
                return detalle;

            return new { detalle, simuladoPor = usuarioReal, rolReal };
        }

        public async Task<List<string>> GetUsuariosAsync()
        {
            using var db = _factory.Create();
            return await db.Auditorias
                .Where(a => !string.IsNullOrEmpty(a.Usuario))
                .Select(a => a.Usuario)
                .Distinct()
                .OrderBy(u => u)
                .ToListAsync();
        }

        public async Task<List<Auditoria>> GetByUsuarioAsync(string usuario, int dias = 30)
        {
            using var db = _factory.Create();
            var desde = DateTime.Now.AddDays(-dias);
            return await db.Auditorias
                .Where(a => a.Usuario == usuario && a.Fecha >= desde)
                .OrderByDescending(a => a.Fecha)
                .ToListAsync();
        }

        public async Task RegistrarAsync(
            string usuario,
            string ip,
            string tabla,
            string tipo,
            string accion,
            object? detalle = null)
        {
            try
            {
                using var db = _factory.Create();
                var registro = new Auditoria
                {
                    Fecha = DateTime.Now,
                    Usuario = usuario,
                    Ip = ip,
                    Tabla = tabla,
                    Tipo = tipo,
                    Accion = accion,
                    Detalle = ConSimulacion(detalle) is object d ? JsonSerializer.Serialize(d) : null
                };
                db.Auditorias.Add(registro);
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuditoriaService] ERROR: {ex.Message}");
            }
        }
    }
}
