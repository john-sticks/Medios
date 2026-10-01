using Microsoft.EntityFrameworkCore;
using Medios.Entities;
using Medios.Infrastructure.Data;

namespace Medios.Services
{
    public class AutorizacionService
    {
        private readonly IMediosDbContextFactory _factory;

        public AutorizacionService(IMediosDbContextFactory factory)
        {
            _factory = factory;
        }

        public async Task<string?> GetEstado(string usuario)
        {
            using var db = _factory.Create();
            var ultima = await db.AutorizacionesUsuario
                .Where(a => a.Usuario == usuario)
                .OrderByDescending(a => a.FechaSolicitud)
                .FirstOrDefaultAsync();
            return ultima?.Estado;
        }

        public async Task<AutorizacionUsuario?> GetPendiente(string usuario)
        {
            using var db = _factory.Create();
            return await db.AutorizacionesUsuario
                .Where(a => a.Usuario == usuario && a.Estado == "pendiente")
                .OrderByDescending(a => a.FechaSolicitud)
                .FirstOrDefaultAsync();
        }

        public async Task<AutorizacionUsuario?> GetById(int id)
        {
            using var db = _factory.Create();
            return await db.AutorizacionesUsuario.FindAsync(id);
        }

        public async Task<(int Id, string Codigo)> CrearSolicitud(string usuario, string nombre, string? email,
            string? destino, string? jerarquia, string? legajo, string? telefono, string? rol = null)
        {
            using var db = _factory.Create();

            var pendientes = await db.AutorizacionesUsuario
                .Where(a => a.Usuario == usuario && a.Estado == "pendiente")
                .ToListAsync();
            pendientes.ForEach(p => p.Estado = "reemplazada");

            var codigo = new Random().Next(100000, 999999).ToString();

            var solicitud = new AutorizacionUsuario
            {
                Usuario = usuario,
                Nombre = nombre,
                Email = email,
                Destino = destino,
                Jerarquia = jerarquia,
                Rol = rol,
                Legajo = legajo,
                Telefono = telefono,
                Codigo = codigo,
                Estado = "pendiente",
                FechaSolicitud = DateTime.Now
            };

            db.AutorizacionesUsuario.Add(solicitud);
            await db.SaveChangesAsync();
            return (solicitud.Id, codigo);
        }

        public async Task<bool> Aprobar(int id, string codigo, string aprobadoPor,
            int? delegacionId = null, int? ambitoId = null, string? rol = null)
        {
            using var db = _factory.Create();
            var solicitud = await db.AutorizacionesUsuario.FindAsync(id);
            if (solicitud == null || solicitud.Estado != "pendiente") return false;
            if (solicitud.Codigo != codigo.Trim()) return false;

            solicitud.Estado = "aprobada";
            solicitud.FechaResolucion = DateTime.Now;
            solicitud.AprobadoPor = aprobadoPor;
            if (!string.IsNullOrWhiteSpace(rol)) solicitud.Rol = rol.Trim().ToUpper();
            solicitud.DelegacionId = delegacionId;

            // Vincular el usuario con la delegación elegida. El sistema resuelve la delegación
            // del usuario por Delegacion.UsuarioCerberus; se estampa en la elegida y se libera
            // de cualquier otra (1 usuario ↔ 1 delegación).
            if (delegacionId.HasValue)
            {
                var previas = await db.Delegaciones
                    .Where(d => d.UsuarioCerberus == solicitud.Usuario && d.Id != delegacionId.Value)
                    .ToListAsync();
                foreach (var p in previas) p.UsuarioCerberus = null;

                var elegida = await db.Delegaciones.FindAsync(delegacionId.Value);
                if (elegida != null)
                    elegida.UsuarioCerberus = solicitud.Usuario;
            }

            await db.SaveChangesAsync();
            return true;
        }

        public async Task<List<Medios.Entities.Delegacion>> GetDelegacionesAsync()
        {
            using var db = _factory.Create();
            return await db.Delegaciones
                .Where(d => d.Activa)
                .OrderBy(d => d.Nombre)
                .ToListAsync();
        }

        // Nombre para mostrar de una delegación (prioriza el catálogo Prometheus)
        public async Task<string?> GetDelegacionDisplayNombreAsync(int id)
        {
            using var db = _factory.Create();
            var d = await db.Delegaciones
                .Include(x => x.DelegacionPrometheus)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (d == null) return null;
            return d.DelegacionPrometheus?.Nombre ?? d.Nombre;
        }

        // Usuario real (Cerberus/Cassandra) aprobado y activo asociado a una delegación —
        // MockSwitchController lo usa para "impersonar" esa delegación durante una simulación
        // (así se ve continuidad con sus borradores/notas ya cargados). Ya no es obligatorio:
        // la resolución real pasa por el claim DelegacionId (SesionService.GetDelegacionEfectivaAsync);
        // si no hay usuario real, la simulación sigue funcionando con una identidad sintética.
        // Fallback: Delegacion.UsuarioCerberus (campo histórico).
        public async Task<string?> GetUsuarioDeDelegacionAsync(int delegacionId)
        {
            using var db = _factory.Create();
            var usuario = await db.AutorizacionesUsuario
                .Where(a => a.DelegacionId == delegacionId && a.Estado == "aprobada" && a.Activo)
                .OrderByDescending(a => a.FechaSolicitud)
                .ThenByDescending(a => a.Id)
                .Select(a => a.Usuario)
                .FirstOrDefaultAsync();
            if (!string.IsNullOrWhiteSpace(usuario)) return usuario;

            var deleg = await db.Delegaciones.FindAsync(delegacionId);
            return deleg?.UsuarioCerberus;
        }

        public async Task<List<Medios.Entities.AreaResponsabilidad>> GetAmbitosAsync()
        {
            using var db = _factory.Create();
            return await db.AreasResponsabilidad
                .OrderBy(a => a.AmbitoResponsabilidad)
                .ToListAsync();
        }

        // Obtiene el ámbito asignado a un usuario aprobado
        public async Task<(int? DelegacionId, int? AmbitoId)> GetAmbitoUsuarioAsync(string usuario)
        {
            using var db = _factory.Create();
            var aut = await db.AutorizacionesUsuario
                .Where(a => a.Usuario == usuario && a.Estado == "aprobada")
                .OrderByDescending(a => a.FechaSolicitud)
                .FirstOrDefaultAsync();
            return (aut?.DelegacionId, aut?.AmbitoId);
        }

        // Última autorización aprobada del usuario (para verificar Activo en el login)
        public async Task<AutorizacionUsuario?> GetAprobadaAsync(string usuario)
        {
            using var db = _factory.Create();
            return await db.AutorizacionesUsuario
                .Where(a => a.Usuario == usuario && a.Estado == "aprobada")
                .OrderByDescending(a => a.FechaSolicitud)
                .FirstOrDefaultAsync();
        }

        // Cambiar el rol de un usuario aprobado
        private static readonly HashSet<string> RolesValidos = new(StringComparer.OrdinalIgnoreCase)
            { "DESARROLLADOR", "MEDIOS", "DELEGACION" };

        public async Task<bool> CambiarRolAsync(int id, string rol)
        {
            if (string.IsNullOrWhiteSpace(rol) || !RolesValidos.Contains(rol.Trim())) return false;
            using var db = _factory.Create();
            var aut = await db.AutorizacionesUsuario.FindAsync(id);
            if (aut == null || aut.Estado != "aprobada") return false;
            aut.Rol = rol.Trim().ToUpper();
            await db.SaveChangesAsync();
            return true;
        }

        // Activar / desactivar el acceso de un usuario aprobado
        public async Task<bool> ToggleActivoAsync(int id)
        {
            using var db = _factory.Create();
            var aut = await db.AutorizacionesUsuario.FindAsync(id);
            if (aut == null || aut.Estado != "aprobada") return false;
            aut.Activo = !aut.Activo;
            await db.SaveChangesAsync();
            return aut.Activo;
        }

        // Cambiar la delegación de un usuario aprobado (re-vincula)
        public async Task<bool> CambiarDelegacionAsync(int id, int delegacionId)
        {
            using var db = _factory.Create();
            var aut = await db.AutorizacionesUsuario.FindAsync(id);
            if (aut == null || aut.Estado != "aprobada") return false;

            var elegida = await db.Delegaciones.FindAsync(delegacionId);
            if (elegida == null) return false;

            // Liberar al usuario de cualquier otra delegación (1 usuario ↔ 1 delegación)
            var previas = await db.Delegaciones
                .Where(d => d.UsuarioCerberus == aut.Usuario && d.Id != delegacionId)
                .ToListAsync();
            foreach (var p in previas) p.UsuarioCerberus = null;

            elegida.UsuarioCerberus = aut.Usuario;
            aut.DelegacionId = delegacionId;
            await db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> Rechazar(int id, string aprobadoPor)
        {
            using var db = _factory.Create();
            var solicitud = await db.AutorizacionesUsuario.FindAsync(id);
            if (solicitud == null || solicitud.Estado != "pendiente") return false;

            solicitud.Estado = "rechazada";
            solicitud.FechaResolucion = DateTime.Now;
            solicitud.AprobadoPor = aprobadoPor;
            await db.SaveChangesAsync();
            return true;
        }

        // Cantidad de solicitudes pendientes (alta o revalidación) para el dashboard del admin
        public async Task<int> GetPendientesCountAsync()
        {
            using var db = _factory.Create();
            return await db.AutorizacionesUsuario.CountAsync(a => a.Estado == "pendiente");
        }

        // Usuarios (aprobados) asignados a una delegación — última autorización por usuario
        public async Task<List<AutorizacionUsuario>> GetUsuariosByDelegacionAsync(int delegacionId)
        {
            using var db = _factory.Create();
            var aprobadas = await db.AutorizacionesUsuario
                .Where(a => a.Estado == "aprobada" && a.DelegacionId == delegacionId)
                .ToListAsync();
            return aprobadas
                .GroupBy(a => a.Usuario)
                .Select(g => g.OrderByDescending(a => a.FechaSolicitud).ThenByDescending(a => a.Id).First())
                .OrderBy(a => a.Nombre)
                .ToList();
        }

        // Conteo de usuarios aprobados por delegación (para el listado)
        public async Task<Dictionary<int, int>> GetUsuariosCountByDelegacionAsync()
        {
            using var db = _factory.Create();
            var aprobadas = await db.AutorizacionesUsuario
                .Where(a => a.Estado == "aprobada" && a.DelegacionId != null)
                .Select(a => new { a.Usuario, DelegacionId = a.DelegacionId!.Value, a.FechaSolicitud, a.Id })
                .ToListAsync();
            return aprobadas
                .GroupBy(a => a.Usuario)
                .Select(g => g.OrderByDescending(a => a.FechaSolicitud).ThenByDescending(a => a.Id).First())
                .GroupBy(a => a.DelegacionId)
                .ToDictionary(g => g.Key, g => g.Count());
        }

        public async Task<List<AutorizacionUsuario>> GetListado(string? filtroEstado = null, bool incluirDesarrolladores = false)
        {
            using var db = _factory.Create();
            var query = db.AutorizacionesUsuario
                .Include(a => a.Delegacion)
                .Include(a => a.Ambito)
                .AsQueryable();

            // Los usuarios con rol DESARROLLADOR solo son visibles para otro desarrollador
            if (!incluirDesarrolladores)
                query = query.Where(a => a.Rol != "DESARROLLADOR");

            var todas = await query.ToListAsync();

            // Solo la última autorización por usuario (estado actual): evita mostrar registros
            // superados (ej. una aprobada-inactiva vieja junto a la revalidada activa).
            var ultimas = todas
                .GroupBy(a => a.Usuario)
                .Select(g => g.OrderByDescending(a => a.FechaSolicitud).ThenByDescending(a => a.Id).First())
                .ToList();

            if (!string.IsNullOrEmpty(filtroEstado))
                ultimas = ultimas.Where(a => a.Estado == filtroEstado).ToList();

            return ultimas.OrderByDescending(a => a.FechaSolicitud).ToList();
        }
    }
}
