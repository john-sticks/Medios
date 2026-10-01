using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Medios.Entities;
using Medios.Infrastructure.Data;

namespace Medios.Services
{
    // Registro y consulta de la trazabilidad del ciclo de vida de notas y síntesis.
    public class TrazaService
    {
        private readonly IMediosDbContextFactory _factory;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public TrazaService(IMediosDbContextFactory factory, IHttpContextAccessor httpContextAccessor)
        {
            _factory = factory;
            _httpContextAccessor = httpContextAccessor;
        }

        // Si la request actual está simulando un rol (MOCK), deja constancia en el detalle de
        // quién es el usuario real detrás de la simulación (ver AuditoriaService.ConSimulacion).
        private string? ConSimulacion(string? detalle)
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null || !user.HasClaim("EsSimulador", "true")) return detalle;
            var usuarioReal = user.FindFirst("UsuarioReal")?.Value;
            var usuarioActual = user.FindFirst("Usuario")?.Value;
            var rolReal = user.FindFirst("RolReal")?.Value;
            if (string.IsNullOrEmpty(usuarioReal) || string.Equals(usuarioReal, usuarioActual, StringComparison.OrdinalIgnoreCase))
                return detalle;

            var nota = $"[simulado por {usuarioReal}/{rolReal}]";
            return string.IsNullOrEmpty(detalle) ? nota : $"{detalle} {nota}";
        }

        // Nombres de evento (constantes para evitar typos)
        public const string NotaCreada      = "NOTA_CREADA";
        public const string NotaModificada  = "NOTA_MODIFICADA";
        public const string NotaAmpliada    = "NOTA_AMPLIADA";
        public const string NotaAprobada    = "NOTA_APROBADA";
        public const string NotaDescartada  = "NOTA_DESCARTADA";
        public const string NotaEliminada   = "NOTA_ELIMINADA";
        public const string NotaAgregada    = "NOTA_AGREGADA";
        public const string SesionRemitida  = "SESION_REMITIDA";
        public const string SesionRevisada  = "SESION_REVISADA";
        public const string SesionFinalizada = "SESION_FINALIZADA";
        public const string SintesisGenerada = "SINTESIS_GENERADA";
        public const string SintesisPdf     = "SINTESIS_PDF";
        public const string SintesisRemitida = "SINTESIS_REMITIDA";
        public const string SintesisConsolidada = "SINTESIS_CONSOLIDADA";
        public const string SintesisInformada = "SINTESIS_INFORMADA";
        public const string ConsolidacionRevertida = "CONSOLIDACION_REVERTIDA";

        // Registra un evento de traza. Nunca lanza (no debe romper el flujo de negocio).
        public async Task RegistrarAsync(string evento, string usuario, string? rol,
            int? notaId = null, int? sesionId = null, int? sintesisId = null,
            int? version = null, string? estado = null, string? detalle = null)
        {
            try
            {
                using var db = _factory.Create();
                db.TrazaEventos.Add(new TrazaEventoEntity
                {
                    Fecha = DateTime.Now,
                    Usuario = usuario,
                    Rol = rol,
                    NotaId = notaId,
                    SesionId = sesionId,
                    SintesisId = sintesisId,
                    Evento = evento,
                    Version = version,
                    Estado = estado,
                    Detalle = ConSimulacion(detalle)
                });
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TrazaService] ERROR: {ex.Message}");
            }
        }

        // Devuelve todos los eventos de traza relevantes para una nota:
        // eventos de la nota + de su sesión + de las síntesis donde participó + del linaje.
        public async Task<List<TrazaEventoEntity>> GetEventosNotaAsync(int notaId)
        {
            using var db = _factory.Create();

            var nota = await db.NotasPrensa
                .Where(n => n.Id == notaId)
                .Select(n => new { n.Id, n.SesionPrensaId, n.NotaOrigenId })
                .FirstOrDefaultAsync();
            if (nota == null) return new();

            // Cadena de notas del linaje (esta nota + sus orígenes)
            var notaIds = new List<int> { notaId };
            int? cur = nota.NotaOrigenId; int guard = 0;
            while (cur != null && guard++ < 20)
            {
                notaIds.Add(cur.Value);
                cur = await db.NotasPrensa.Where(n => n.Id == cur).Select(n => n.NotaOrigenId).FirstOrDefaultAsync();
            }

            // Sesiones de esas notas
            var sesionIds = await db.NotasPrensa
                .Where(n => notaIds.Contains(n.Id) && n.SesionPrensaId != null)
                .Select(n => n.SesionPrensaId!.Value)
                .Distinct().ToListAsync();

            // Síntesis donde participaron esas notas
            var sintesisIds = await db.SintesisNotas
                .Where(sn => notaIds.Contains(sn.NotaPrensaId))
                .Select(sn => sn.SintesisId)
                .Distinct().ToListAsync();

            // - Eventos CON NotaId: solo los de esta nota (o su linaje), no las hermanas de la sesión.
            // - Eventos de sesión/síntesis (sin NotaId): aplican a toda la sesión/síntesis.
            return await db.TrazaEventos
                .Where(e => (e.NotaId != null && notaIds.Contains(e.NotaId.Value))
                         || (e.NotaId == null && e.SesionId != null && sesionIds.Contains(e.SesionId.Value))
                         || (e.NotaId == null && e.SintesisId != null && sintesisIds.Contains(e.SintesisId.Value)))
                .OrderBy(e => e.Fecha)
                .ThenBy(e => e.Id)
                .ToListAsync();
        }

        // Traza de una síntesis AGRUPADA: junta las acciones repetidas (ej. "3 notas creadas
        // por Carlos Delegación") por tipo de evento + rol + usuario + estado (+ delegación en
        // los eventos de nota). Reduce el ruido de las consolidadas que abarcan varias sesiones.
        public async Task<List<TrazaGrupo>> GetTrazaSintesisAgrupadaAsync(int sintesisId)
        {
            var eventos = await GetEventosSintesisAsync(sintesisId);
            if (eventos.Count == 0) return new();

            // Quitar los eventos de consolidación a nivel sesión (redundantes con el principal)
            eventos = eventos
                .Where(e => !(e.Evento == SintesisConsolidada && e.SesionId != null && e.SintesisId == sintesisId))
                .ToList();

            using var db = _factory.Create();
            var notaIds = eventos.Where(e => e.NotaId != null).Select(e => e.NotaId!.Value).Distinct().ToList();
            var sesionIds = eventos.Where(e => e.SesionId != null).Select(e => e.SesionId!.Value).Distinct().ToList();

            var notaDeleg = await db.NotasPrensa
                .Include(n => n.Sesion).ThenInclude(s => s!.Delegacion)
                .Include(n => n.Delegacion)
                .Where(n => notaIds.Contains(n.Id))
                .ToDictionaryAsync(n => n.Id, n => n.Sesion != null
                    ? (n.Sesion.Delegacion != null ? n.Sesion.Delegacion.Nombre : "División Medios")
                    : (n.Delegacion != null ? n.Delegacion.Nombre : "División Medios"));

            var sesionDeleg = await db.SesionesPrensas
                .Include(s => s.Delegacion)
                .Where(s => sesionIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Delegacion != null ? s.Delegacion.Nombre : "División Medios");

            // Delegación para agrupar: solo en eventos de nota (las sesiones se agrupan sin delegación)
            string? DelegNota(TrazaEventoEntity e) =>
                e.NotaId != null && notaDeleg.TryGetValue(e.NotaId.Value, out var d) ? d : null;

            // SINTESIS_INFORMADA no se agrupa: cada envío/edición debe verse individualmente
            // en el log (distintos destinatarios/canales aunque coincidan rol/usuario/estado).
            var informados = eventos.Where(e => e.Evento == SintesisInformada)
                .Select(e => new TrazaGrupo
                {
                    Evento = e.Evento, Rol = e.Rol, Usuario = e.Usuario, Estado = e.Estado,
                    Delegacion = null, Cantidad = 1, Fecha = e.Fecha, Detalle = e.Detalle,
                    NotaIds = new List<int>()
                });

            var grupos = eventos
                .Where(e => e.Evento != SintesisInformada)
                .GroupBy(e => new {
                    e.Evento, e.Rol, e.Usuario, e.Estado,
                    Deleg = e.Evento.StartsWith("NOTA_") ? DelegNota(e) : null
                })
                .Select(g => new TrazaGrupo
                {
                    Evento = g.Key.Evento,
                    Rol = g.Key.Rol,
                    Usuario = g.Key.Usuario,
                    Estado = g.Key.Estado,
                    Delegacion = g.Key.Deleg,
                    Cantidad = g.Count(),
                    Fecha = g.Min(e => e.Fecha),
                    Detalle = g.OrderBy(e => e.Fecha).First().Detalle,
                    NotaIds = g.Where(e => e.NotaId != null).Select(e => e.NotaId!.Value).Distinct().OrderBy(x => x).ToList()
                })
                .Concat(informados)
                .OrderBy(g => g.Fecha)
                .ThenBy(g => g.Evento)
                .ToList();

            return grupos;
        }

        // Eventos de traza de una síntesis: los de la síntesis + sus sesiones + sus notas.
        public async Task<List<TrazaEventoEntity>> GetEventosSintesisAsync(int sintesisId)
        {
            using var db = _factory.Create();

            var notaIds = await db.SintesisNotas
                .Where(sn => sn.SintesisId == sintesisId)
                .Select(sn => sn.NotaPrensaId).Distinct().ToListAsync();

            var sesionIds = await db.NotasPrensa
                .Where(n => notaIds.Contains(n.Id) && n.SesionPrensaId != null)
                .Select(n => n.SesionPrensaId!.Value).Distinct().ToListAsync();

            // Sesiones consolidadas vinculadas a esta síntesis
            var consolidadas = await db.SesionesPrensas
                .Where(s => s.SintesisConsolidadaId == sintesisId)
                .Select(s => s.Id).ToListAsync();
            sesionIds = sesionIds.Union(consolidadas).ToList();

            return await db.TrazaEventos
                .Where(e => e.SintesisId == sintesisId
                         || (e.NotaId != null && notaIds.Contains(e.NotaId.Value))
                         || (e.NotaId == null && e.SesionId != null && sesionIds.Contains(e.SesionId.Value)))
                .OrderBy(e => e.Fecha)
                .ThenBy(e => e.Id)
                .ToListAsync();
        }
    }

    // Grupo de eventos de traza (acciones repetidas juntadas) para la vista de síntesis
    public class TrazaGrupo
    {
        public string Evento { get; set; } = "";
        public string? Rol { get; set; }
        public string? Usuario { get; set; }
        public string? Estado { get; set; }
        public string? Delegacion { get; set; }
        public int Cantidad { get; set; }
        public DateTime Fecha { get; set; }
        public string? Detalle { get; set; }
        public List<int> NotaIds { get; set; } = new();
    }
}
