using Microsoft.EntityFrameworkCore;
using Medios.Entities;
using Medios.Infrastructure.Data;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Security.Claims;

namespace Medios.Services
{
    public class SesionService
    {
        private readonly IMediosDbContextFactory _factory;
        private readonly IWebHostEnvironment _env;
        private readonly SintesisService _sintesisService;
        private readonly NotaService _notaService;

        public SesionService(IMediosDbContextFactory factory, IWebHostEnvironment env,
            SintesisService sintesisService, NotaService notaService)
        {
            _factory = factory;
            _env = env;
            _sintesisService = sintesisService;
            _notaService = notaService;
        }

        // Sesiones propias de una delegación (por usuario Cerberus)
        public async Task<List<SesionPrensa>> GetMiasAsync(string usuarioCerberus)
        {
            using var db = _factory.Create();
            return await db.SesionesPrensas
                .Include(s => s.Delegacion)
                .Include(s => s.Notas)
                .Where(s => s.UsuarioCarga == usuarioCerberus)
                .OrderByDescending(s => s.Fecha)
                .ThenByDescending(s => s.FechaCreacion)
                .ToListAsync();
        }

        // Bandeja ANALISTA: Revisada + Finalizada
        public async Task<List<SesionPrensa>> GetBandejaAnalistaAsync(
            string? filtroEstado = null,
            int? delegacionId = null,
            DateOnly? fechaDesde = null,
            DateOnly? fechaHasta = null,
            string? turno = null,
            string? usuarioBorradores = null)
        {
            using var db = _factory.Create();
            var query = db.SesionesPrensas
                .Include(s => s.Delegacion)
                .Include(s => s.Notas)
                    .ThenInclude(n => n.VersionActual)
                // El ANALISTA revisa en "Remitida" (Procesando) y luego marca Finalizada; sigue
                // viendo en "Finalizadas" las que finalizó aunque se hayan consolidado. Además ve
                // sus PROPIOS borradores (los que él creó) — no los borradores de otras delegaciones.
                .Where(s => s.Estado == "Remitida" || s.Estado == "Finalizada" || s.Estado == "Consolidada"
                         || (s.Estado == "Borrador" && s.UsuarioCarga == usuarioBorradores))
                // Una modificación antigua pudo mover todas las notas a otro borrador.
                // Esa sesión vacía no representa una remisión pendiente de revisión.
                .Where(s => s.Estado != "Remitida" || s.Notas.Any())
                .AsQueryable();

            // Filtro exacto cuando se pide (ej. SUPERVISOR pide solo "Finalizada" para su bandeja).
            // El ANALISTA no pasa filtro: ve las tres etapas y filtra client-side por pestaña.
            if (!string.IsNullOrEmpty(filtroEstado))
                query = query.Where(s => s.Estado == filtroEstado);

            if (delegacionId.HasValue)
                query = query.Where(s => s.DelegacionId == delegacionId.Value);

            if (fechaDesde.HasValue)
                query = query.Where(s => s.Fecha >= fechaDesde.Value);

            if (fechaHasta.HasValue)
                query = query.Where(s => s.Fecha <= fechaHasta.Value);

            if (!string.IsNullOrEmpty(turno))
                query = query.Where(s => s.Turno == turno);

            return await query
                .OrderByDescending(s => s.Fecha)
                .ThenByDescending(s => s.FechaRemision)
                .ToListAsync();
        }

        // Marca sesiones como "Consolidada" para que no reaparezcan en /sintesis/consolidar,
        // vinculándolas a la síntesis consolidada generada (para poder revertir).
        public async Task MarcarConsolidadasAsync(List<int> sesionIds, int sintesisId)
        {
            if (sesionIds.Count == 0) return;
            using var db = _factory.Create();
            var sesiones = await db.SesionesPrensas
                .Where(s => sesionIds.Contains(s.Id))
                .ToListAsync();
            foreach (var s in sesiones)
            {
                s.Estado = "Consolidada";
                s.SintesisConsolidadaId = sintesisId;
            }
            await db.SaveChangesAsync();
        }

        // Revierte la consolidación: las sesiones vinculadas a la síntesis vuelven a "Finalizada".
        public async Task RevertirConsolidacionAsync(int sintesisId)
        {
            using var db = _factory.Create();
            var sesiones = await db.SesionesPrensas
                .Where(s => s.SintesisConsolidadaId == sintesisId)
                .ToListAsync();
            foreach (var s in sesiones)
            {
                s.Estado = "Finalizada";
                s.SintesisConsolidadaId = null;
            }
            await db.SaveChangesAsync();
        }

        // Bandeja con filtros completos
        public async Task<List<SesionPrensa>> GetBandejaAsync(
            string? filtroEstado = "Remitida",
            int? delegacionId = null,
            DateOnly? fechaDesde = null,
            DateOnly? fechaHasta = null,
            string? turno = null)
        {
            using var db = _factory.Create();
            var query = db.SesionesPrensas
                .Include(s => s.Delegacion)
                .Include(s => s.Notas)
                    .ThenInclude(n => n.VersionActual)
                .AsQueryable();

            // Estados visibles: Remitida (Procesando, en revisión) + posteriores. Una sesión
            // Remitida permanece en "Procesando" hasta marcarse Finalizada, aunque ya tenga
            // todas las notas aprobadas (se eliminó el filtro a nivel de nota que la ocultaba).
            var estilosVisibles = new[] { "Remitida", "Finalizada", "Consolidada" };
            query = query.Where(s => estilosVisibles.Contains(s.Estado));
            query = query.Where(s => s.Estado != "Remitida" || s.Notas.Any());

            if (!string.IsNullOrEmpty(filtroEstado))
                query = query.Where(s => s.Estado == filtroEstado);

            if (delegacionId.HasValue)
                query = query.Where(s => s.DelegacionId == delegacionId.Value);

            if (fechaDesde.HasValue)
                query = query.Where(s => s.Fecha >= fechaDesde.Value);

            if (fechaHasta.HasValue)
                query = query.Where(s => s.Fecha <= fechaHasta.Value);

            if (!string.IsNullOrEmpty(turno))
                query = query.Where(s => s.Turno == turno);

            return await query
                .OrderByDescending(s => s.Fecha)
                .ThenByDescending(s => s.FechaRemision)
                .ToListAsync();
        }

        public async Task<SesionPrensa?> GetByIdAsync(int id)
        {
            using var db = _factory.Create();
            return await db.SesionesPrensas
                .Include(s => s.Delegacion)
                    .ThenInclude(d => d!.AreaResponsabilidad)
                .Include(s => s.Notas)
                    .ThenInclude(n => n.VersionActual!)
                        .ThenInclude(v => v.Categoria)
                .Include(s => s.Notas)
                    .ThenInclude(n => n.VersionActual!)
                        .ThenInclude(v => v.Partido)
                .Include(s => s.Notas)
                    .ThenInclude(n => n.VersionActual!)
                        .ThenInclude(v => v.Localidad)
                .Include(s => s.Notas)
                    .ThenInclude(n => n.VersionActual!)
                        .ThenInclude(v => v.CaratulaQuiron)
                .Include(s => s.Notas)
                    .ThenInclude(n => n.VersionActual!)
                        .ThenInclude(v => v.ModalidadQuiron)
                .Include(s => s.Notas)
                    .ThenInclude(n => n.Versiones)
                .FirstOrDefaultAsync(s => s.Id == id);
        }

        // Retorna el Id de la sesión creada, o 0 si ya existe una para esa delegación+fecha+turno.
        // Cuando delegacionId es null (sesión División Medios sin delegación asignada) la unicidad
        // se evalúa por usuarioCarga, porque distintos operadores pueden crear sus propias sesiones.
        public async Task<int> CrearAsync(int? delegacionId, DateOnly fecha, string turno, string usuarioCarga)
        {
            using var db = _factory.Create();

            bool existe;
            if (delegacionId.HasValue)
            {
                existe = await db.SesionesPrensas.AnyAsync(s =>
                    s.DelegacionId == delegacionId.Value &&
                    s.Fecha == fecha &&
                    s.Turno == turno);
            }
            else
            {
                // Sin delegación: la restricción aplica por operador (no bloquea a otros usuarios)
                existe = await db.SesionesPrensas.AnyAsync(s =>
                    s.DelegacionId == null &&
                    s.UsuarioCarga == usuarioCarga &&
                    s.Fecha == fecha &&
                    s.Turno == turno);
            }

            if (existe) return 0;

            var sesion = new SesionPrensa
            {
                DelegacionId = delegacionId,
                Fecha = fecha,
                Turno = turno,
                Estado = "Borrador",
                FechaCreacion = DateTime.Now,
                UsuarioCarga = usuarioCarga
            };
            db.SesionesPrensas.Add(sesion);
            await db.SaveChangesAsync();
            return sesion.Id;
        }

        public async Task<bool> RemitirAsync(int id, string usuarioCarga)
        {
            using var db = _factory.Create();
            var sesion = await db.SesionesPrensas
                .Include(s => s.Notas)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (sesion == null) return false;
            if (sesion.UsuarioCarga != usuarioCarga) return false;
            if (sesion.Estado != "Borrador") return false;
            if (!sesion.Notas.Any()) return false;
            if (string.IsNullOrEmpty(sesion.PDFPath)) return false;

            sesion.Estado = "Remitida";
            sesion.FechaRemision = DateTime.Now;

            // Notas origen "Sin Remitir" pasan a "Borrador" al ser incluidas en sesión
            var origenIds = sesion.Notas
                .Where(n => n.NotaOrigenId.HasValue)
                .Select(n => n.NotaOrigenId!.Value)
                .ToList();

            if (origenIds.Any())
            {
                var versionesSinRemitir = await db.NotasVersion
                    .Where(v => origenIds.Contains(v.NotaId) && v.EsActual && v.EstadoRevision == "Sin Remitir")
                    .ToListAsync();
                foreach (var v in versionesSinRemitir)
                    v.EstadoRevision = "Borrador";
            }

            await db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> EliminarBorradorAsync(int sesionId, string usuarioCarga, bool eliminarNotas = true)
        {
            using var strategyDb = _factory.Create();
            return await strategyDb.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                using var db = _factory.Create();
                var sesion = await db.SesionesPrensas
                    .Include(s => s.Notas)
                    .FirstOrDefaultAsync(s => s.Id == sesionId);

                if (sesion == null || sesion.UsuarioCarga != usuarioCarga || sesion.Estado != "Borrador")
                    return false;

                // El borrado en bloque usa varias operaciones: un fallo no debe dejar la sesión
                // sin notas o con enlaces eliminados parcialmente.
                using var transaction = eliminarNotas && sesion.Notas.Any()
                    ? await db.Database.BeginTransactionAsync() : null;

                if (sesion.Notas.Any())
                {
                    if (eliminarNotas)
                    {
                        var notaIds = sesion.Notas.Select(n => n.Id).ToList();

                        // Limpiar tablas que referencian notas_prensa por FK RESTRICT
                        var sinNotas = await db.SintesisNotas
                            .Where(sn => notaIds.Contains(sn.NotaPrensaId)).ToListAsync();
                        db.SintesisNotas.RemoveRange(sinNotas);

                        var relaciones = await db.NotasRelaciones
                            .Where(r => notaIds.Contains(r.NotaId) || notaIds.Contains(r.NotaRelacionadaId))
                            .ToListAsync();
                        db.NotasRelaciones.RemoveRange(relaciones);
                        await db.SaveChangesAsync();

                        // Romper FKs circulares directo en DB (EF no genera UPDATE para entidades
                        // en estado Deleted, así que hay que bypasear el change tracker) y eliminar.
                        await db.NotasVersion.Where(v => notaIds.Contains(v.NotaId))
                            .ExecuteUpdateAsync(s => s.SetProperty(v => v.ParentVersionId, (int?)null));
                        await db.NotasPrensa.Where(n => notaIds.Contains(n.Id))
                            .ExecuteUpdateAsync(s => s.SetProperty(n => n.VersionActualId, (int?)null));
                        await db.NotasPrensa.Where(n => notaIds.Contains(n.Id)).ExecuteDeleteAsync();
                        // ExecuteDelete no actualiza las entidades ya cargadas. Evitar que EF
                        // intente borrarlas/actualizarlas otra vez al eliminar la sesión.
                        db.ChangeTracker.Clear();
                        sesion.Notas.Clear();
                    }
                    else
                    {
                        // Las notas quedan libres (SesionPrensaId = null via SetNull)
                        // EF Core lo maneja automáticamente al borrar la sesión
                    }
                }

                db.SesionesPrensas.Remove(sesion);
                await db.SaveChangesAsync();
                if (transaction != null) await transaction.CommitAsync();
                return true;
            });
        }

        public async Task<bool> AgregarNotaExistenteAsync(int sesionId, int notaId, string usuario)
        {
            using var db = _factory.Create();
            var sesion = await db.SesionesPrensas
                .FirstOrDefaultAsync(s => s.Id == sesionId && s.Estado == "Borrador" && s.UsuarioCarga == usuario);
            if (sesion == null) return false;

            var nota = await db.NotasPrensa
                .Include(n => n.VersionActual)
                .FirstOrDefaultAsync(n => n.Id == notaId
                    && (n.SesionPrensaId == null
                        || n.VersionActual!.EstadoRevision == "Sin Remitir"));
            if (nota == null) return false;

            if (nota.DelegacionId.HasValue && nota.DelegacionId != sesion.DelegacionId)
                return false;

            nota.SesionPrensaId = sesionId;

            // Cambiar "Sin Remitir" → "Borrador" al incorporar al borrador
            if (nota.VersionActual?.EstadoRevision == "Sin Remitir")
                nota.VersionActual.EstadoRevision = "Borrador";

            await db.SaveChangesAsync();
            return true;
        }

        private static readonly HashSet<string> TiposUnicos = new(StringComparer.OrdinalIgnoreCase)
            { "Matutina", "Vespertina" };

        // -1: error genérico | -2: ya existe síntesis de ese tipo/fecha/delegación
        public async Task<int> ConvertirASintesisAsync(int sesionId, string operador)
        {
            using var db = _factory.Create();
            var sesion = await db.SesionesPrensas
                .Include(s => s.Notas)
                    .ThenInclude(n => n.VersionActual)
                .FirstOrDefaultAsync(s => s.Id == sesionId);

            if (sesion == null || sesion.Estado != "Borrador" || !sesion.Notas.Any()) return -1;
            if (!string.Equals(sesion.UsuarioCarga, operador, StringComparison.OrdinalIgnoreCase)) return -1;
            if (sesion.Notas.Any(n => n.VersionActual == null)) return -1;

            // Validar unicidad: Matutina y Vespertina solo 1 por delegación por día
            if (TiposUnicos.Contains(sesion.Turno) && sesion.DelegacionId.HasValue)
            {
                var existe = await db.Sintesis.AnyAsync(s =>
                    s.Tipo == sesion.Turno &&
                    s.Fecha == sesion.Fecha &&
                    s.DelegacionId == sesion.DelegacionId &&
                    s.Estado != "Borrador"); // Borrador puede reemplazarse
                if (existe) return -2;
            }

            var sintesis = new Sintesis
            {
                Fecha = sesion.Fecha,
                Tipo = sesion.Turno,
                Estado = "Borrador",
                FechaCreacion = DateTime.Now,
                OperadorGenera = operador,
                DelegacionId = sesion.DelegacionId
            };
            db.Sintesis.Add(sintesis);

            var notaIds = sesion.Notas.Select(n => n.Id).ToList();

            int orden = 0;
            foreach (var nota in sesion.Notas)
            {
                sintesis.NotasIncluidas.Add(new SintesisNota
                {
                    Nota = nota,
                    NotaVersion = nota.VersionActual!,
                    Orden = orden++
                });
            }

            // Contenido propio de MEDIOS (sin Delegación real de por medio): quien lo carga y
            // quien lo revisaría son el mismo operador, así que se salta el paso intermedio
            // "Remitida"/Procesando y pasa directo a "Finalizada" (las notas, ya auto-aprobadas
            // al cargarlas, también pasan directo a "Finalizada").
            var divisionMedios = await GetDelegacionDivisionMediosAsync();
            bool esPropioDeMedios = divisionMedios != null && sesion.DelegacionId == divisionMedios.Id;

            sesion.Estado = esPropioDeMedios ? "Finalizada" : "Remitida";
            sesion.FechaRemision = DateTime.Now;

            if (esPropioDeMedios)
            {
                var versionesPropias = await db.NotasVersion
                    .Where(v => notaIds.Contains(v.NotaId) && v.EsActual)
                    .ToListAsync();
                foreach (var v in versionesPropias)
                    if (string.IsNullOrWhiteSpace(v.MotivoDescarte)
                        && v.EstadoRevision is "Borrador" or "Sin Remitir" or "Remitida" or "Aprobada" or "Agregada")
                        v.EstadoRevision = "Finalizada";
            }

            // Notas libres/ampliadas que pasan a esta síntesis: de "Sin Remitir" → "Borrador"
            var allNotaIds = notaIds
                .Concat(sesion.Notas.Where(n => n.NotaOrigenId.HasValue).Select(n => n.NotaOrigenId!.Value))
                .Distinct()
                .ToList();
            var versionesSinRemitir = await db.NotasVersion
                .Where(v => allNotaIds.Contains(v.NotaId) && v.EsActual && v.EstadoRevision == "Sin Remitir")
                .ToListAsync();
            foreach (var v in versionesSinRemitir)
                if (v.EstadoRevision == "Sin Remitir" && string.IsNullOrWhiteSpace(v.MotivoDescarte))
                    v.EstadoRevision = "Borrador";

            await db.SaveChangesAsync();
            return sintesis.Id;
        }

        public async Task<List<NotaPrensa>> GetNotasSinRemitirByDelegacionAsync(int delegacionId)
        {
            using var db = _factory.Create();
            return await db.NotasPrensa
                .Include(n => n.Sesion)
                .Include(n => n.VersionActual)
                    .ThenInclude(v => v!.Categoria)
                .Include(n => n.VersionActual)
                    .ThenInclude(v => v!.Partido)
                .Include(n => n.Versiones.OrderBy(v => v.Version))
                    .ThenInclude(v => v.Categoria)
                .Where(n => n.SesionPrensaId != null
                         && n.Sesion!.DelegacionId == delegacionId
                         && n.VersionActual!.EstadoRevision == "Sin Remitir")
                .OrderByDescending(n => n.VersionActual!.FechaVersion)
                .ToListAsync();
        }

        // Devuelve un dict notaOrigenId → sesionBorrador para las notas ya copiadas por el usuario
        public async Task<Dictionary<int, SesionPrensa>> GetBorradorPorNotaOrigenAsync(
            IEnumerable<int> notaOrigenIds, string usuarioCarga)
        {
            using var db = _factory.Create();
            var ids = notaOrigenIds.ToList();
            if (!ids.Any()) return new Dictionary<int, SesionPrensa>();

            var copias = await db.NotasPrensa
                .Include(n => n.Sesion)
                .Where(n => n.NotaOrigenId != null
                         && ids.Contains(n.NotaOrigenId!.Value)
                         && n.SesionPrensaId != null
                         && n.Sesion!.UsuarioCarga == usuarioCarga
                         && n.Sesion.Estado == "Borrador")
                .ToListAsync();

            return copias
                .Where(n => n.NotaOrigenId.HasValue && n.Sesion != null)
                .GroupBy(n => n.NotaOrigenId!.Value)
                .ToDictionary(g => g.Key, g => g.First().Sesion!);
        }

        // Mueve la copia de una nota sin remitir a otra sesión borrador del mismo usuario
        public async Task<bool> CambiarBorradorNotaAsync(int notaOrigenId, int nuevaSesionId, string usuarioCarga)
        {
            using var db = _factory.Create();

            var nuevaSesion = await db.SesionesPrensas
                .FirstOrDefaultAsync(s => s.Id == nuevaSesionId
                                       && s.UsuarioCarga == usuarioCarga
                                       && s.Estado == "Borrador");
            if (nuevaSesion == null) return false;

            var copia = await db.NotasPrensa
                .Include(n => n.Sesion)
                .FirstOrDefaultAsync(n => n.NotaOrigenId == notaOrigenId
                                       && n.SesionPrensaId != null
                                       && n.Sesion!.UsuarioCarga == usuarioCarga
                                       && n.Sesion.Estado == "Borrador");
            if (copia == null) return false;

            copia.SesionPrensaId = nuevaSesionId;
            await db.SaveChangesAsync();
            return true;
        }

        public async Task<List<Delegacion>> GetDelegacionesAsync()
        {
            using var db = _factory.Create();
            return await db.Delegaciones.Where(d => d.Activa).OrderBy(d => d.Nombre).ToListAsync();
        }

        public async Task<Delegacion?> GetDelegacionByUsuarioAsync(string usuario)
        {
            using var db = _factory.Create();

            // La delegación del usuario se resuelve por su última autorización aprobada y activa
            // (autorizaciones_usuario.DelegacionId). Esto permite que varios usuarios compartan
            // una misma delegación. Fallback: el campo histórico Delegacion.UsuarioCerberus.
            var delegId = await db.AutorizacionesUsuario
                .Where(a => a.Usuario == usuario && a.Estado == "aprobada" && a.Activo && a.DelegacionId != null)
                .OrderByDescending(a => a.FechaSolicitud)
                .ThenByDescending(a => a.Id)
                .Select(a => a.DelegacionId)
                .FirstOrDefaultAsync();

            var query = db.Delegaciones
                .Include(d => d.Partido)
                .Include(d => d.AreaResponsabilidad)
                .Include(d => d.DelegacionPrometheus);

            if (delegId != null)
                return await query.FirstOrDefaultAsync(d => d.Id == delegId.Value && d.Activa);

            return await query.FirstOrDefaultAsync(d => d.UsuarioCerberus == usuario && d.Activa);
        }

        // Igual que GetDelegacionByUsuarioAsync, pero prioriza el claim "DelegacionId" cuando
        // está presente (login real de una DELEGACION aprobada, o simulación MOCK) antes de caer
        // al lookup por username. Necesario porque una Delegación simulada sin usuario real
        // asociado no tiene ninguna fila en autorizaciones_usuario/Delegacion.UsuarioCerberus
        // que el lookup por username pueda encontrar.
        public async Task<Delegacion?> GetDelegacionEfectivaAsync(ClaimsPrincipal user)
        {
            if (int.TryParse(user.FindFirst("DelegacionId")?.Value, out var id))
            {
                using var db = _factory.Create();
                return await db.Delegaciones
                    .Include(d => d.Partido)
                    .Include(d => d.AreaResponsabilidad)
                    .Include(d => d.DelegacionPrometheus)
                    .FirstOrDefaultAsync(d => d.Id == id && d.Activa);
            }
            var usuario = user.FindFirst("Usuario")?.Value ?? user.Identity?.Name ?? "";
            var deleg = await GetDelegacionByUsuarioAsync(usuario);

            // MEDIOS no tiene delegación real: usa la fila especial "División Medios" para que
            // sus notas/síntesis propias reutilicen toda la lógica de Delegación (límite de
            // Matutina/Vespertina por día, "Mis Borradores", "Generadas", etc.) en vez de
            // compartir el mismo DelegacionId=NULL que usa la síntesis consolidada real.
            if (deleg == null && user.IsInRole("MEDIOS"))
                deleg = await GetDelegacionDivisionMediosAsync();

            return deleg;
        }

        // Fila especial en `delegaciones` (no es una delegación real) que identifica el
        // contenido cargado directamente por MEDIOS (sin ninguna Delegación de por medio).
        public async Task<Delegacion?> GetDelegacionDivisionMediosAsync()
        {
            using var db = _factory.Create();
            return await db.Delegaciones
                .Include(d => d.Partido)
                .Include(d => d.AreaResponsabilidad)
                .Include(d => d.DelegacionPrometheus)
                .FirstOrDefaultAsync(d => d.Nombre == "División Medios");
        }

        // Marca la sesión como Revisada cuando no quedan notas Pendientes
        public async Task ActualizarEstadoSiCompletaAsync(int sesionId)
        {
            // Estado ahora se cambia manualmente vía MarcarRevisadaAsync — esta función queda vacía por compatibilidad
        }

        public async Task<bool> TodasNotasRevisadasAsync(int sesionId)
        {
            using var db = _factory.Create();
            var sesion = await db.SesionesPrensas
                .Include(s => s.Notas).ThenInclude(n => n.VersionActual)
                .FirstOrDefaultAsync(s => s.Id == sesionId);
            if (sesion == null || sesion.Estado != "Remitida") return false;
            return !sesion.Notas.Any(n => n.VersionActual?.EstadoRevision is "Borrador" or "Remitida" or "Pendiente");
        }

        // El ANALISTA, tras revisar todas las notas de una sesión Remitida, la marca Finalizada
        // (se eliminó la etapa intermedia "Revisada"). Las Aprobadas/Agregadas pasan a "Finalizada"
        // y las descartadas se procesan (salen de la síntesis y vuelven a la delegación).
        public async Task<bool> MarcarFinalizadaAsync(int sesionId)
        {
            if (!await TodasNotasRevisadasAsync(sesionId)) return false;
            using var db = _factory.Create();
            var sesion = await db.SesionesPrensas
                .Include(s => s.Notas).ThenInclude(n => n.VersionActual)
                .FirstOrDefaultAsync(s => s.Id == sesionId);
            if (sesion == null || sesion.Estado != "Remitida") return false;

            // Versión que la síntesis realmente contiene (snapshot si existe, sino la actual)
            var notaIds = sesion.Notas.Select(n => n.Id).ToList();
            var snList = await db.SintesisNotas
                .Include(sn => sn.NotaVersion).Include(sn => sn.Sintesis)
                .Where(sn => notaIds.Contains(sn.NotaPrensaId) && sn.Sintesis.Estado != "Borrador")
                .ToListAsync();
            var snapByNota = snList
                .GroupBy(sn => sn.NotaPrensaId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(sn => sn.SintesisId).First().NotaVersion);

            foreach (var nota in sesion.Notas)
            {
                var v = snapByNota.TryGetValue(nota.Id, out var snap) ? snap : nota.VersionActual;
                if (v == null) continue;
                if (v.EstadoRevision is "Aprobada" or "Agregada")
                    v.EstadoRevision = "Finalizada";
            }

            sesion.Estado = "Finalizada";
            await db.SaveChangesAsync();
            await ProcesarNotasDescartadasAsync(sesionId);
            return true;
        }

        // ── MEDIOS: revertir una Finalizada propia (creada por MEDIOS, no de una Delegación)
        //    de vuelta a Borrador, para poder seguir editándola. Deshace lo que hizo
        //    ConvertirASintesisAsync: borra la Síntesis propia y su PDF si las notas no
        //    fueron incluidas en una publicación informada. Conserva la sesión y las notas.
        public async Task<bool> RevertirFinalizadaABorradorAsync(int sesionId)
        {
            using var db = _factory.Create();
            var sesion = await db.SesionesPrensas.Include(s => s.Notas)
                .FirstOrDefaultAsync(s => s.Id == sesionId);
            if (sesion == null || sesion.Estado != "Finalizada") return false;

            var divisionMedios = await GetDelegacionDivisionMediosAsync();
            if (divisionMedios == null || sesion.DelegacionId != divisionMedios.Id) return false;

            var notaIds = sesion.Notas.Select(n => n.Id).ToList();

            // Solo la(s) Síntesis propia(s) de MEDIOS (DelegacionId = División Medios) que haya
            // generado esta sesión al publicarse — no la consolidada real, que puede incluir
            // copias de estas mismas notas por un "Consolidar" aparte y no debe tocarse acá.
            var sintesisIds = await db.SintesisNotas
                .Where(sn => notaIds.Contains(sn.NotaPrensaId) && sn.Sintesis.DelegacionId == divisionMedios.Id)
                .Select(sn => sn.SintesisId)
                .Distinct()
                .ToListAsync();
            var sintesisList = await db.Sintesis.Where(s => sintesisIds.Contains(s.Id)).ToListAsync();

            // "Remitida" en una síntesis propia significa Finalizar, no informar a terceros.
            // Las publicaciones informadas y sus snapshots deben permanecer inmutables.
            if (await db.SintesisNotas.AnyAsync(sn => notaIds.Contains(sn.NotaPrensaId)
                && sn.Sintesis.Estado == "Informada")) return false;

            foreach (var s in sintesisList)
            {
                if (string.IsNullOrEmpty(s.PDFPath)) continue;
                var ruta = Path.Combine(_env.WebRootPath, s.PDFPath.TrimStart('/'));
                if (File.Exists(ruta)) File.Delete(ruta);
            }

            var enlaces = await db.SintesisNotas.Where(sn => sintesisIds.Contains(sn.SintesisId)).ToListAsync();
            db.SintesisNotas.RemoveRange(enlaces);
            db.Sintesis.RemoveRange(sintesisList);

            var versiones = await db.NotasVersion
                .Where(v => notaIds.Contains(v.NotaId) && v.EsActual)
                .ToListAsync();
            foreach (var v in versiones)
                v.EstadoRevision = "Borrador";

            sesion.Estado = "Borrador";
            sesion.FechaRemision = null;
            sesion.PDFPath = null;
            sesion.FechaGeneracionPdf = null;

            await db.SaveChangesAsync();
            return true;
        }

        // ── Procesar notas descartadas tras revisión completa ────────

        private async Task ProcesarNotasDescartadasAsync(int sesionId)
        {
            using var db = _factory.Create();
            var sesion = await db.SesionesPrensas
                .Include(s => s.Notas)
                    .ThenInclude(n => n.VersionActual)
                .FirstOrDefaultAsync(s => s.Id == sesionId);

            if (sesion == null) return;

            var notasDescartadas = sesion.Notas
                .Where(n => n.VersionActual?.EstadoRevision == "Descartada")
                .ToList();
            if (!notasDescartadas.Any()) return;

            var idsDescartados = notasDescartadas.Select(n => n.Id).ToList();

            // Una nota negada deja de pertenecer a la síntesis (deja las instancias superiores).
            // Se quita de sintesis_notas en las síntesis aún en curso (no en las ya Informadas,
            // que quedan como registro histórico). Vuelve a "Sin Remitir" con su MotivoDescarte
            // para que la DELEGACIÓN la trate como nota sin vincular (eliminar o anexar a un borrador).
            var snDescartadas = await db.SintesisNotas
                .Include(sn => sn.Sintesis)
                .Where(sn => idsDescartados.Contains(sn.NotaPrensaId) && sn.Sintesis.Estado != "Informada")
                .ToListAsync();
            var sintesisAfectadas = snDescartadas.Select(sn => sn.SintesisId).Distinct().ToList();
            db.SintesisNotas.RemoveRange(snDescartadas);

            var versiones = await db.NotasVersion
                .Where(v => idsDescartados.Contains(v.NotaId) && v.EsActual)
                .ToListAsync();
            foreach (var v in versiones)
                v.EstadoRevision = "Sin Remitir"; // MotivoDescarte se preserva

            await db.SaveChangesAsync();

            // Regenerar el PDF de cada síntesis afectada que todavía tenga notas aprobadas
            foreach (var sintId in sintesisAfectadas)
            {
                var hayAprobadas = await db.SintesisNotas
                    .Include(sn => sn.Nota).ThenInclude(n => n.VersionActual)
                    .AnyAsync(sn => sn.SintesisId == sintId
                        && sn.Nota.VersionActual != null
                        && sn.Nota.VersionActual.EstadoRevision == "Aprobada");
                if (hayAprobadas)
                    await _sintesisService.GenerarPdfAsync(sintId);
            }
        }

        // ── Generar PDF de sesión borrador ────────────────────────────

        public async Task<string> GenerarPdfAsync(int sesionId)
        {
            var sesion = await GetByIdAsync(sesionId);
            if (sesion == null) throw new InvalidOperationException("Sesión no encontrada");

            var notas = sesion.Notas
                .OrderBy(n => n.VersionActual?.Categoria?.Orden ?? 99)
                .ThenBy(n => n.VersionActual?.Partido?.Nombre ?? "")
                .ToList();

            var porCategoria = notas
                .GroupBy(n => n.VersionActual?.Categoria?.Nombre ?? "Sin categoría")
                .ToList();

            // Línea de tiempo: ancla en la versión original + actualizaciones (ResumenCambio)
            var versionPorNota = notas
                .Where(n => n.VersionActual != null)
                .ToDictionary(n => n.Id, n => n.VersionActual!);
            var lineas = await _notaService.GetLineasTiempoAsync(versionPorNota);

            var nombreArchivo = $"sesion_{sesion.Turno.ToLower()}_{sesion.Fecha:yyyyMMdd}_{sesionId}.pdf";
            var pdfDir = Path.Combine(_env.WebRootPath, "pdf");
            Directory.CreateDirectory(pdfDir);
            var pdfPath = Path.Combine(pdfDir, nombreArchivo);

            var meses = new[] { "", "enero", "febrero", "marzo", "abril", "mayo", "junio",
                                "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" };
            var fechaLarga = $"{sesion.Fecha.Day} de {meses[sesion.Fecha.Month]} de {sesion.Fecha.Year}";
            var delegacionNombre = sesion.Delegacion?.Nombre ?? "División Medios";

            // Geo sin duplicar: el nombre de Localidad ya trae el partido entre paréntesis
            // para desambiguar homónimas de otros partidos (ej. "SAN GENARO (MONTE)").
            string FormatGeo(Partido? partido, Localidad? localidad)
            {
                if (partido == null) return "";
                var loc = localidad?.Nombre ?? "";
                var sufijo = $"({partido.Nombre})";
                if (loc.EndsWith(sufijo, StringComparison.OrdinalIgnoreCase))
                    loc = loc[..^sufijo.Length].Trim();
                if (loc.Length == 0 || string.Equals(loc, partido.Nombre, StringComparison.OrdinalIgnoreCase))
                    return partido.Nombre.ToUpper();
                return $"{partido.Nombre.ToUpper()} - {loc.ToUpper()}";
            }

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.5f, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));

                    page.Header().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().AlignCenter().Column(c =>
                            {
                                c.Item().Text("Policía de la Provincia de Buenos Aires")
                                    .FontSize(8).FontColor(Colors.Grey.Darken2);
                                c.Item().Text("Superintendencia de Inteligencia Criminal")
                                    .FontSize(8).FontColor(Colors.Grey.Darken2);
                                c.Item().Text("División Medios O.S.INT")
                                    .FontSize(8).FontColor(Colors.Grey.Darken2);
                            });
                        });

                        col.Item().PaddingVertical(4).LineHorizontal(1).LineColor(Colors.Grey.Medium);

                        col.Item().AlignCenter().Text($"REMISIÓN DE NOTAS DE PRENSA — {sesion.Turno.ToUpper()}")
                            .FontSize(14).Bold().FontColor(Color.FromHex("#8B4513"));

                        col.Item().AlignCenter().Text(fechaLarga).FontSize(12).Bold();

                        col.Item().AlignCenter().Text($"Delegación: {delegacionNombre}")
                            .FontSize(10).Italic();

                        col.Item().PaddingVertical(4).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                    });

                    page.Content().PaddingTop(8).Column(col =>
                    {
                        foreach (var grupo in porCategoria)
                        {
                            col.Item().PaddingTop(6).Text(grupo.Key.ToUpper())
                                .FontSize(11).Bold().FontColor(Color.FromHex("#8B4513"));
                            col.Item().PaddingBottom(4).LineHorizontal(0.5f).LineColor(Color.FromHex("#8B4513"));

                            foreach (var nota in grupo)
                            {
                                // Cuerpo anclado en la versión original (v1) si la noticia evolucionó
                                lineas.TryGetValue(nota.Id, out var linea);
                                var tieneHist = linea != null && linea.Actualizaciones.Any();
                                var v = (tieneHist ? linea!.Ancla : null) ?? nota.VersionActual!;
                                col.Item().PaddingTop(4).Column(notaCol =>
                                {
                                    var geo = FormatGeo(v.Partido, v.Localidad);
                                    if (geo.Length > 0)
                                        notaCol.Item().Text(geo).FontSize(9).Bold();
                                    notaCol.Item().Text(v.Titulo.ToUpper()).Bold().FontSize(10);
                                    notaCol.Item().Text(v.Texto).FontSize(10).LineHeight(1.3f);
                                    if (!string.IsNullOrEmpty(v.Fuente))
                                        notaCol.Item().Text($"({v.Fuente.ToUpper()})").FontSize(10).Italic();
                                    if (!string.IsNullOrEmpty(v.Link))
                                        notaCol.Item().Text(v.Link).FontSize(8).FontColor(Colors.Blue.Medium);

                                    // Actualizaciones (ampliaciones posteriores) como anotaciones
                                    if (tieneHist)
                                    {
                                        foreach (var act in linea!.Actualizaciones)
                                        {
                                            var etiqueta = $"ACTUALIZACIÓN · {act.Fecha:dd/MM/yyyy}"
                                                + (string.IsNullOrEmpty(act.Turno) ? "" : $" · {act.Turno}");
                                            notaCol.Item().PaddingTop(3).PaddingLeft(8)
                                                .BorderLeft(2).BorderColor(Color.FromHex("#8B4513"))
                                                .PaddingLeft(6).Column(actCol =>
                                                {
                                                    actCol.Item().Text(etiqueta).FontSize(8).Bold()
                                                        .FontColor(Color.FromHex("#8B4513"));
                                                    actCol.Item().Text(act.ResumenCambio ?? "").FontSize(9).LineHeight(1.2f);
                                                });
                                        }
                                    }
                                });
                            }

                            col.Item().PaddingVertical(6).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
                        }
                    });

                    page.Footer().AlignCenter().Text(txt =>
                    {
                        txt.Span("División Medios O.S.INT — ").FontSize(8).FontColor(Colors.Grey.Darken1);
                        txt.CurrentPageNumber().FontSize(8);
                        txt.Span(" / ").FontSize(8);
                        txt.TotalPages().FontSize(8);
                    });
                });
            }).GeneratePdf(pdfPath);

            using var db = _factory.Create();
            var s = await db.SesionesPrensas.FindAsync(sesionId);
            if (s != null)
            {
                s.PDFPath = $"/pdf/{nombreArchivo}";
                s.FechaGeneracionPdf = DateTime.Now;
                await db.SaveChangesAsync();
            }

            return $"/pdf/{nombreArchivo}";
        }

        // Métricas para el dashboard
        public async Task<DashboardMetricas> GetMetricasAsync()
        {
            using var db = _factory.Create();
            var hoy = DateOnly.FromDateTime(DateTime.Today);

            // ── OPERADOR ───────────────────────────────────────────────
            var sesionesRemitidas = db.SesionesPrensas
                .Where(s => s.Estado == "Remitida"
                    && s.Notas.Any(n => n.VersionActual != null
                        && (n.VersionActual.EstadoRevision == "Remitida"
                            || n.VersionActual.EstadoRevision == "Aprobada"
                            || n.VersionActual.EstadoRevision == "Descartada")));

            // ── ANALISTA ───────────────────────────────────────────────
            // Sesiones "Remitidas": pendientes de revisar/finalizar por el ANALISTA
            var sesionesRevisadas = db.SesionesPrensas.Where(s => s.Estado == "Remitida");
            // Sesiones "Finalizadas" hoy (análisis completado hoy)
            var sesionesFinalizadasHoy = db.SesionesPrensas
                .Where(s => s.Estado == "Finalizada" && s.Fecha == hoy);

            return new DashboardMetricas
            {
                // Operador
                SesionesRemitidas    = await sesionesRemitidas.CountAsync(),
                NotasPendientesHoy   = await db.NotasVersion
                    .Include(v => v.Nota).ThenInclude(n => n.Sesion)
                    .CountAsync(v => v.EsActual
                        && v.EstadoRevision == "Remitida"
                        && v.Nota.SesionPrensaId != null
                        && v.Nota.Sesion!.Fecha == hoy),
                NotasAprobadasHoy    = await db.NotasVersion
                    .Include(v => v.Nota).ThenInclude(n => n.Sesion)
                    .CountAsync(v => v.EsActual
                        && v.EstadoRevision == "Aprobada"
                        && v.Nota.SesionPrensaId != null
                        && v.Nota.Sesion!.Fecha == hoy),
                SesionesHoy          = await sesionesRemitidas.CountAsync(s => s.Fecha == hoy),
                // Analista
                SesionesRevisadas    = await sesionesRevisadas.CountAsync(),
                SesionesFinalizadasHoy = await sesionesFinalizadasHoy.CountAsync(),
                NotasAgregadasHoy    = await db.NotasVersion
                    .Include(v => v.Nota).ThenInclude(n => n.Sesion)
                    .CountAsync(v => v.EsActual
                        && v.EstadoRevision == "Agregada"
                        && v.Nota.SesionPrensaId != null
                        && v.Nota.Sesion!.Fecha == hoy),
                SesionesRevisadasHoy = await sesionesRevisadas.CountAsync(s => s.Fecha == hoy),
            };
        }
    }

    public class DashboardMetricas
    {
        // Operador
        public int SesionesRemitidas     { get; set; }
        public int NotasPendientesHoy    { get; set; }
        public int NotasAprobadasHoy     { get; set; }
        public int SesionesHoy           { get; set; }
        // Analista
        public int SesionesRevisadas     { get; set; }
        public int SesionesFinalizadasHoy { get; set; }
        public int NotasAgregadasHoy     { get; set; }
        public int SesionesRevisadasHoy  { get; set; }
    }
}
