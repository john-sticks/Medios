using Microsoft.EntityFrameworkCore;
using Medios.Entities;
using Medios.Infrastructure.Data;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Text.RegularExpressions;

namespace Medios.Services
{
    public class SintesisService
    {
        private readonly IMediosDbContextFactory _factory;
        private readonly IWebHostEnvironment _env;
        private readonly NotaService _notaService;

        public SintesisService(IMediosDbContextFactory factory, IWebHostEnvironment env, NotaService notaService)
        {
            _factory = factory;
            _env = env;
            _notaService = notaService;
        }

        // ── Listado de síntesis ───────────────────────────────────────

        public async Task<List<Sintesis>> GetListadoAsync(int cantidad = 30, string? operadorFiltro = null, int? delegacionFiltro = null)
        {
            using var db = _factory.Create();
            var query = db.Sintesis
                .Include(s => s.NotasIncluidas)
                    .ThenInclude(sn => sn.Nota)
                        .ThenInclude(n => n.VersionActual)
                .Include(s => s.NotasIncluidas)
                    .ThenInclude(sn => sn.Nota)
                        .ThenInclude(n => n.Sesion)
                .AsQueryable();
            if (operadorFiltro != null)
                query = query.Where(s => s.OperadorGenera == operadorFiltro);
            if (delegacionFiltro != null)
                query = query.Where(s => s.DelegacionId == delegacionFiltro);
            return await query
                .OrderByDescending(s => s.Fecha)
                .ThenByDescending(s => s.FechaCreacion)
                .Take(cantidad)
                .ToListAsync();
        }

        // Búsqueda de síntesis para el módulo Traza (por fecha / tipo / delegación)
        public async Task<List<Sintesis>> BuscarParaTrazaAsync(DateOnly? desde, DateOnly? hasta, string? tipo, int? delegacionId)
        {
            using var db = _factory.Create();
            var q = db.Sintesis
                .Include(s => s.Delegacion)
                .Include(s => s.NotasIncluidas)
                .AsQueryable();

            if (desde.HasValue) q = q.Where(s => s.Fecha >= desde.Value);
            if (hasta.HasValue) q = q.Where(s => s.Fecha <= hasta.Value);
            if (!string.IsNullOrEmpty(tipo)) q = q.Where(s => s.Tipo == tipo);
            if (delegacionId.HasValue)
                q = delegacionId.Value == 0
                    ? q.Where(s => s.DelegacionId == null)   // 0 = consolidadas (sin delegación)
                    : q.Where(s => s.DelegacionId == delegacionId.Value);

            return await q
                .OrderByDescending(s => s.Fecha)
                .ThenByDescending(s => s.FechaCreacion)
                .Take(100)
                .ToListAsync();
        }

        public async Task<Sintesis?> GetByIdAsync(int id)
        {
            using var db = _factory.Create();
            return await db.Sintesis
                .Include(s => s.Delegacion)
                    .ThenInclude(d => d!.AreaResponsabilidad)
                .Include(s => s.Delegacion)
                    .ThenInclude(d => d!.DelegacionPrometheus)
                .Include(s => s.NotasIncluidas)
                    .ThenInclude(sn => sn.NotaVersion)
                        .ThenInclude(v => v.Categoria)
                .Include(s => s.NotasIncluidas)
                    .ThenInclude(sn => sn.NotaVersion)
                        .ThenInclude(v => v.Partido)
                .Include(s => s.NotasIncluidas)
                    .ThenInclude(sn => sn.NotaVersion)
                        .ThenInclude(v => v.Localidad)
                .Include(s => s.NotasIncluidas)
                    .ThenInclude(sn => sn.Nota)
                        .ThenInclude(n => n.VersionActual!)
                .Include(s => s.NotasIncluidas)
                    .ThenInclude(sn => sn.Nota)
                        .ThenInclude(n => n.Sesion)
                .Include(s => s.DelegacionesDestino)
                    .ThenInclude(d => d.Delegacion)
                .FirstOrDefaultAsync(s => s.Id == id);
        }

        // Devuelve la sesión en la que fue remitida la copia de cada nota origen
        public async Task<Dictionary<int, SesionPrensa>> GetSesionPorNotaOrigenAsync(IEnumerable<int> notaOrigenIds)
        {
            var ids = notaOrigenIds.ToList();
            if (!ids.Any()) return new Dictionary<int, SesionPrensa>();

            using var db = _factory.Create();
            var copias = await db.NotasPrensa
                .Include(n => n.Sesion)
                .Where(n => n.NotaOrigenId.HasValue && ids.Contains(n.NotaOrigenId!.Value))
                .ToListAsync();

            return copias
                .Where(n => n.NotaOrigenId.HasValue && n.Sesion != null)
                .GroupBy(n => n.NotaOrigenId!.Value)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(n => n.Sesion!.Fecha).First().Sesion!);
        }

        // Devuelve la síntesis donde fue incluida cada nota origen (para marcar actualizaciones)
        public async Task<Dictionary<int, Sintesis>> GetSintesisPorNotaOrigenAsync(IEnumerable<int> notaOrigenIds)
        {
            var ids = notaOrigenIds.ToList();
            if (!ids.Any()) return new Dictionary<int, Sintesis>();

            using var db = _factory.Create();
            var rows = await db.SintesisNotas
                .Include(sn => sn.Sintesis)
                .Where(sn => ids.Contains(sn.NotaPrensaId))
                .ToListAsync();

            return rows
                .GroupBy(sn => sn.NotaPrensaId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(sn => sn.Sintesis.Fecha).First().Sintesis);
        }

        // ── Síntesis consolidadas (DelegacionId null) ─────────────────

        // Pendientes de informar: Borrador o Generada
        public async Task<List<Sintesis>> GetConsolidasAsync()
        {
            using var db = _factory.Create();
            return await db.Sintesis
                .Include(s => s.NotasIncluidas)
                .Where(s => s.DelegacionId == null && s.Estado != "Informada")
                .OrderByDescending(s => s.Fecha)
                .ThenByDescending(s => s.FechaCreacion)
                .ToListAsync();
        }

        public async Task<List<Sintesis>> GetInformadasAsync()
        {
            using var db = _factory.Create();
            return await db.Sintesis
                .Include(s => s.NotasIncluidas)
                .Where(s => s.DelegacionId == null && s.Estado == "Informada")
                .OrderByDescending(s => s.Fecha)
                .ThenByDescending(s => s.FechaCreacion)
                .ToListAsync();
        }

        // Marca la síntesis consolidada como Informada (distribuida)
        public async Task<bool> InformarAsync(int sintesisId)
        {
            using var db = _factory.Create();
            var s = await db.Sintesis.FindAsync(sintesisId);
            if (s == null || s.DelegacionId != null) return false;
            s.Estado = "Informada";
            s.FechaGeneracion = DateTime.Now;
            await db.SaveChangesAsync();
            return true;
        }

        // Informa la síntesis consolidada por los canales seleccionados (todos los roles, mails
        // y/o visibilidad in-app para delegaciones específicas o todas).
        public async Task<bool> InformarConCanalesAsync(int sintesisId, bool todosRoles, string? mails,
            bool todasDelegaciones = false, List<int>? delegacionIds = null)
        {
            using var db = _factory.Create();
            var s = await db.Sintesis.FindAsync(sintesisId);
            if (s == null || s.DelegacionId != null) return false;
            s.Estado = "Informada";
            s.CanalTodosRoles = todosRoles;
            s.CanalTodasDelegaciones = todasDelegaciones;
            s.CanalMails = string.IsNullOrWhiteSpace(mails) ? null : mails.Trim();
            s.FechaInformada = DateTime.Now;
            s.FechaGeneracion = DateTime.Now;

            var actuales = await db.SintesisDelegacionDestinos
                .Where(d => d.SintesisId == sintesisId).ToListAsync();
            db.SintesisDelegacionDestinos.RemoveRange(actuales);
            if (!todasDelegaciones && delegacionIds != null)
                foreach (var dId in delegacionIds.Distinct())
                    db.SintesisDelegacionDestinos.Add(new SintesisDelegacionDestino
                    { SintesisId = sintesisId, DelegacionId = dId });

            await db.SaveChangesAsync();
            return true;
        }

        // Revierte la consolidación: elimina la síntesis consolidada y sus snapshots.
        // Las sesiones vinculadas vuelven a "Finalizada" (lo hace SesionService aparte).
        public async Task<bool> EliminarConsolidadaAsync(int sintesisId)
        {
            using var db = _factory.Create();
            var s = await db.Sintesis
                .Include(x => x.NotasIncluidas)
                .FirstOrDefaultAsync(x => x.Id == sintesisId);
            if (s == null || s.DelegacionId != null) return false;
            if (s.Estado == "Informada") return false; // no se revierte una ya informada

            db.SintesisNotas.RemoveRange(s.NotasIncluidas);
            db.Sintesis.Remove(s);
            await db.SaveChangesAsync();
            return true;
        }

        // Cuenta síntesis de una delegación cuya sesión ya fue finalizada/consolidada (= "aprobadas")
        public async Task<int> GetAprobadasCountByDelegacionAsync(int delegacionId)
        {
            using var db = _factory.Create();
            return await db.Sintesis
                .Where(s => s.DelegacionId == delegacionId
                         && s.NotasIncluidas.Any(sn =>
                             sn.Nota.SesionPrensaId != null &&
                             (sn.Nota.Sesion!.Estado == "Finalizada" || sn.Nota.Sesion!.Estado == "Consolidada")))
                .CountAsync();
        }

        // Síntesis publicadas. El rol DELEGACION solo ve las informadas que le llegan
        // (Todos los Roles, Todas las delegaciones, o destino específico para la suya).
        // Cualquier otro rol (ANALISTA/SUPERVISOR/etc.) ve todas las informadas sin restricción
        // de canal: la restricción por delegación es exclusiva del rol DELEGACION.
        public async Task<List<Sintesis>> GetPublicadasAsync(bool esDelegacion = false, int? delegacionId = null)
        {
            using var db = _factory.Create();
            var query = db.Sintesis
                .Include(s => s.NotasIncluidas)
                .Where(s => s.DelegacionId == null && s.Estado == "Informada")
                .AsQueryable();

            if (esDelegacion)
                query = query.Where(s => s.CanalTodosRoles
                    || (delegacionId != null && (s.CanalTodasDelegaciones
                        || s.DelegacionesDestino.Any(d => d.DelegacionId == delegacionId.Value))));

            return await query
                .OrderByDescending(s => s.Fecha)
                .ThenByDescending(s => s.FechaInformada)
                .ToListAsync();
        }

        // ── Deduplicación Nacional/Provincial para Consolidar ─────────

        // Devuelve grupos de notas Nacional/Provincial de las sesiones seleccionadas,
        // agrupadas por URL igual o similitud de título (Jaccard ≥ 0.6).
        public async Task<List<GrupoNotaNacProv>> GetNotasNacProvParaConsolidarAsync(List<int> sesionIds)
        {
            if (sesionIds.Count == 0) return new();
            using var db = _factory.Create();

            var notas = await db.NotasPrensa
                .Include(n => n.VersionActual).ThenInclude(v => v!.Categoria)
                .Include(n => n.Sesion).ThenInclude(s => s!.Delegacion)
                    .ThenInclude(d => d!.DelegacionPrometheus)
                .Where(n => n.SesionPrensaId != null
                         && sesionIds.Contains(n.SesionPrensaId.Value)
                         && n.VersionActual != null
                         && (n.VersionActual.AmbitoNota == "Nacional" || n.VersionActual.AmbitoNota == "Provincial")
                         // Excluir descartadas (Descartada o "Sin Remitir" con MotivoDescarte): no van a la consolidada.
                         && n.VersionActual.EstadoRevision != "Descartada"
                         && !(n.VersionActual.EstadoRevision == "Sin Remitir" && n.VersionActual.MotivoDescarte != null))
                .ToListAsync();

            if (!notas.Any()) return new();

            var notaIds = notas.Select(n => n.Id).ToList();
            var snaps = await GetSnapshotVersionesPorNotaAsync(notaIds);
            NotaVersion? VerDe(NotaPrensa n) =>
                snaps.TryGetValue(n.Id, out var sv) ? sv : n.VersionActual;

            var items = notas
                .Select(n =>
                {
                    var ver = VerDe(n);
                    if (ver == null) return null;
                    return new NotaEnGrupo
                    {
                        NotaId    = n.Id,
                        VersionId = ver.Id,
                        Titulo    = ver.Titulo,
                        Fuente    = ver.Fuente,
                        Url       = ver.Link,
                        Delegacion = n.Sesion?.Delegacion?.DelegacionPrometheus?.Nombre
                                  ?? n.Sesion?.Delegacion?.Nombre ?? "División Medios",
                        Ambito   = ver.AmbitoNota ?? "Nacional",
                        Categoria = ver.Categoria?.Nombre
                    };
                })
                .Where(x => x != null).Cast<NotaEnGrupo>()
                .ToList();

            return AgruparNotasSimilares(items);
        }

        private static List<GrupoNotaNacProv> AgruparNotasSimilares(List<NotaEnGrupo> items)
        {
            var grupos   = new List<GrupoNotaNacProv>();
            var asignadas = new HashSet<int>();

            // Paso 1: agrupar por URL normalizada (solo grupos con 2+ variantes)
            var conUrl = items
                .Where(x => !string.IsNullOrWhiteSpace(x.Url))
                .GroupBy(x => NormUrl(x.Url!));

            foreach (var g in conUrl)
            {
                var variantes = g.ToList();
                variantes.ForEach(v => asignadas.Add(v.NotaId));
                grupos.Add(new GrupoNotaNacProv
                {
                    Ambito    = variantes[0].Ambito,
                    Titulo    = variantes[0].Titulo,
                    Url       = g.Key,
                    Variantes = variantes,
                    NotaIdSeleccionada = variantes[0].NotaId
                });
            }

            // Paso 2: notas sin URL o sin duplicado URL → agrupar por similitud de título
            var restantes = items.Where(x => !asignadas.Contains(x.NotaId)).ToList();
            foreach (var item in restantes)
            {
                if (asignadas.Contains(item.NotaId)) continue;
                var grupo = new GrupoNotaNacProv
                {
                    Ambito = item.Ambito, Titulo = item.Titulo, Url = item.Url,
                    Variantes = new List<NotaEnGrupo> { item },
                    NotaIdSeleccionada = item.NotaId
                };
                asignadas.Add(item.NotaId);
                foreach (var otro in restantes)
                {
                    if (asignadas.Contains(otro.NotaId) || otro.Ambito != item.Ambito) continue;
                    if (SimilitudJaccard(item.Titulo, otro.Titulo) >= 0.6)
                    {
                        grupo.Variantes.Add(otro);
                        asignadas.Add(otro.NotaId);
                    }
                }
                grupos.Add(grupo);
            }

            return grupos
                .OrderBy(g => g.Ambito == "Nacional" ? 0 : 1)
                .ThenBy(g => g.Titulo)
                .ToList();
        }

        private static string NormUrl(string url) =>
            url.ToLower().Trim().TrimEnd('/');

        private static double SimilitudJaccard(string a, string b)
        {
            var wa = Tokens(a);
            var wb = Tokens(b);
            if (wa.Count == 0 || wb.Count == 0) return 0;
            var intersect = wa.Intersect(wb, StringComparer.OrdinalIgnoreCase).Count();
            var union     = wa.Union(wb, StringComparer.OrdinalIgnoreCase).Count();
            return union == 0 ? 0 : (double)intersect / union;
        }

        private static HashSet<string> Tokens(string t) =>
            new(Regex.Replace(t.ToLower(), @"[^a-záéíóúüñ\s]", "")
                     .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                     .Where(w => w.Length >= 4),
                StringComparer.OrdinalIgnoreCase);

        // ── Consolidación del ANALISTA: ventana de turnos ─────────────
        //
        // Grupo de turnos que absorbe cada síntesis destino:
        //   Vespertina  → Vespertina + Ampliacion Vespertina + Especial
        //   Matutina    → Matutina   + Ampliacion Matutina   + Especial
        //   Especial    → solo Especial (urgentes standalone)
        public static string[] TurnosDeVentana(string destino) => destino switch
        {
            "Matutina"   => new[] { "Matutina", "Ampliacion Matutina", "Especial" },
            "Vespertina" => new[] { "Vespertina", "Ampliacion Vespertina", "Especial" },
            "Especial"   => new[] { "Especial" },
            _            => new[] { destino }
        };

        // Fecha+Tipo de las consolidadas ya generadas (no en Borrador, "Especial" excluida
        // porque admite varias por día) — usado por la pantalla de Consolidar para avisar
        // ANTES de armar toda la selección que el guard de duplicados de CrearAsync va a
        // rechazar esa combinación.
        public async Task<List<(DateOnly Fecha, string Tipo)>> GetFechasTipoConsolidadosAsync()
        {
            using var db = _factory.Create();
            var rows = await db.Sintesis
                .Where(s => s.DelegacionId == null && s.Estado != "Borrador" && s.Tipo != "Especial")
                .Select(s => new { s.Fecha, s.Tipo })
                .Distinct()
                .ToListAsync();
            return rows.Select(r => (r.Fecha, r.Tipo)).ToList();
        }

        // Sesiones FINALIZADAS candidatas para consolidar (todas las delegaciones).
        // De CUALQUIER tipo/turno; la fecha es un filtro OPCIONAL (si es null se listan
        // todas las finalizadas pendientes de cualquier fecha). El supervisor elige cuáles
        // incorporar y recién al confirmar define tipo + fecha de la consolidada de salida.
        public async Task<List<SesionPrensa>> GetSesionesFinalizadasParaConsolidarAsync(DateOnly? fecha = null)
        {
            using var db = _factory.Create();
            var q = db.SesionesPrensas
                .Include(s => s.Delegacion)
                .Include(s => s.Notas)
                    .ThenInclude(n => n.VersionActual)
                .Where(s => s.Estado == "Finalizada");
            if (fecha.HasValue) q = q.Where(s => s.Fecha == fecha.Value);
            return await q
                .OrderByDescending(s => s.Fecha)
                .ThenBy(s => s.Delegacion != null ? s.Delegacion.Nombre : "")
                .ThenBy(s => s.Turno)
                .ToListAsync();
        }

        // Carga las sesiones Finalizadas seleccionadas (por id) para consolidar.
        public async Task<List<SesionPrensa>> GetSesionesParaConsolidarByIdsAsync(List<int> ids)
        {
            if (ids == null || ids.Count == 0) return new();
            using var db = _factory.Create();
            return await db.SesionesPrensas
                .Include(s => s.Delegacion)
                .Include(s => s.Notas)
                    .ThenInclude(n => n.VersionActual)
                .Where(s => s.Estado == "Finalizada" && ids.Contains(s.Id))
                .ToListAsync();
        }

        // Raíz del linaje de cada nota (sube por NotaOrigenId) — para deduplicar noticias repetidas
        public async Task<Dictionary<int, int>> GetLineageRootsAsync(List<int> notaIds)
        {
            using var db = _factory.Create();
            var result = new Dictionary<int, int>();
            foreach (var id in notaIds)
            {
                int cur = id, guard = 0;
                while (guard++ < 20)
                {
                    var origen = await db.NotasPrensa
                        .Where(n => n.Id == cur).Select(n => n.NotaOrigenId).FirstOrDefaultAsync();
                    if (origen == null) break;
                    cur = origen.Value;
                }
                result[id] = cur;
            }
            return result;
        }

        // Devuelve el snapshot (NotaVersion capturado en síntesis) por NotaPrensaId
        public async Task<Dictionary<int, NotaVersion>> GetSnapshotVersionesPorNotaAsync(List<int> notaIds)
        {
            using var db = _factory.Create();
            return await db.SintesisNotas
                .Include(sn => sn.Sintesis)
                .Include(sn => sn.NotaVersion)
                    .ThenInclude(v => v.Categoria)
                .Include(sn => sn.NotaVersion)
                    .ThenInclude(v => v.Partido)
                .Include(sn => sn.NotaVersion)
                    .ThenInclude(v => v.Localidad)
                .Where(sn => notaIds.Contains(sn.NotaPrensaId)
                          && sn.Sintesis.Estado != "Borrador")
                // Si hay varias síntesis publicadas para la misma nota, usar la más reciente
                .GroupBy(sn => sn.NotaPrensaId)
                .ToDictionaryAsync(
                    g => g.Key,
                    g => g.OrderByDescending(sn => sn.SintesisId).First().NotaVersion);
        }

        // ── Crear / armar síntesis ────────────────────────────────────

        private static readonly HashSet<string> TiposUnicos = new(StringComparer.OrdinalIgnoreCase)
            { "Matutina", "Vespertina" };

        // Retorna -2 si ya existe síntesis del mismo tipo/fecha/delegación.
        // versionOverride: si se pasa, congela esa versión por nota en lugar de VersionActualId
        // (usado en la síntesis consolidada para fijar la versión Finalizada, no una ampliación posterior).
        // consolidada: marca la síntesis como consolidada de todas las delegaciones (DelegacionId null + guard propio).
        public async Task<int> CrearAsync(DateOnly fecha, string tipo, string operador, List<int> notaIds,
            int? delegacionId = null,
            Dictionary<int, int>? versionOverride = null,
            bool consolidada = false)
        {
            using var db = _factory.Create();

            if (TiposUnicos.Contains(tipo) && delegacionId.HasValue)
            {
                var existe = await db.Sintesis.AnyAsync(s =>
                    s.Tipo == tipo &&
                    s.Fecha == fecha &&
                    s.DelegacionId == delegacionId &&
                    s.Estado != "Borrador");
                if (existe) return -2;
            }

            // Guard anti-duplicado para síntesis consolidada (sin delegación).
            // La "Especial" se exceptúa: se pueden crear tantas como sea necesario.
            if (consolidada && !string.Equals(tipo, "Especial", StringComparison.OrdinalIgnoreCase))
            {
                var existeCons = await db.Sintesis.AnyAsync(s =>
                    s.Tipo == tipo &&
                    s.Fecha == fecha &&
                    s.DelegacionId == null &&
                    s.Estado != "Borrador");
                if (existeCons) return -2;
            }

            var sintesis = new Sintesis
            {
                Fecha = fecha,
                Tipo = tipo,
                Estado = "Borrador",
                FechaCreacion = DateTime.Now,
                OperadorGenera = operador,
                DelegacionId = delegacionId
            };
            db.Sintesis.Add(sintesis);
            await db.SaveChangesAsync();

            // Versión a congelar por nota: override (snapshot Finalizada) o VersionActualId
            var versionMap = await db.NotasPrensa
                .Where(n => notaIds.Contains(n.Id))
                .Select(n => new { n.Id, n.VersionActualId })
                .ToDictionaryAsync(x => x.Id, x => x.VersionActualId ?? 0);

            for (int i = 0; i < notaIds.Count; i++)
            {
                int verId = versionOverride != null && versionOverride.TryGetValue(notaIds[i], out var ov) && ov > 0
                    ? ov
                    : (versionMap.TryGetValue(notaIds[i], out var vid) ? vid : 0);

                db.SintesisNotas.Add(new SintesisNota
                {
                    SintesisId = sintesis.Id,
                    NotaPrensaId = notaIds[i],
                    NotaVersionId = verId,
                    Orden = i
                });
            }
            await db.SaveChangesAsync();
            return sintesis.Id;
        }

        // ── Generar PDF ───────────────────────────────────────────────

        public async Task<string> GenerarPdfAsync(int sintesisId)
        {
            var sintesis = await GetByIdAsync(sintesisId);
            if (sintesis == null) throw new InvalidOperationException("Síntesis no encontrada");

            // Usar versión snapshot, saltando notas rechazadas (Sin Remitir con MotivoDescarte)
            var incluidas = sintesis.NotasIncluidas
                .OrderBy(sn => sn.Orden)
                .Where(sn => !(sn.Nota?.VersionActual?.EstadoRevision == "Sin Remitir"
                              && !string.IsNullOrEmpty(sn.Nota?.VersionActual?.MotivoDescarte)))
                .ToList();
            var notas = incluidas.Select(sn => sn.NotaVersion).ToList();

            // Jerarquía: ÁMBITO (Nacional → Provincial → Partido) y, dentro de cada ámbito,
            // las CATEGORÍAS (Institucionales, Denuncias, etc.). Un solo titular por ámbito.
            int OrdenAmbito(string? a) => a switch { "Nacional" => 0, "Provincial" => 1, _ => 2 };

            // Geo sin duplicar: el nombre de Localidad (dato externo sincronizado) ya trae el
            // partido entre paréntesis para desambiguar localidades homónimas de otros partidos
            // (ej. "SAN GENARO (MONTE)"), así que no hay que anteponerlo si ya está.
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

            // Sin punto final (el instructivo lo prohíbe en título y fuente)
            static string SinPuntoFinal(string s) => s.TrimEnd().TrimEnd('.');

            // Fuente + repercusión en otros medios: "(CLARIN, LANACION, LANOTICIA1)"
            static string FormatFuente(string? fuente, string? otrosMedios)
            {
                var medios = new List<string>();
                if (!string.IsNullOrWhiteSpace(fuente)) medios.Add(SinPuntoFinal(fuente.ToUpper()));
                if (!string.IsNullOrWhiteSpace(otrosMedios))
                    medios.AddRange(otrosMedios
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(m => SinPuntoFinal(m.ToUpper())));
                return medios.Count > 0 ? $"({string.Join(", ", medios)})" : "";
            }

            var porAmbito = notas
                .GroupBy(v => string.IsNullOrEmpty(v.AmbitoNota) ? "Partido" : v.AmbitoNota!)
                .OrderBy(g => OrdenAmbito(g.Key))
                .Select(ga => new
                {
                    Ambito = ga.Key,
                    // Sin categoría (ámbito Nacional/Provincial, o Partido sin categoría asignada)
                    // va agrupado al final, bajo una etiqueta sintética "SIN CATEGORÍA".
                    Categorias = ga.GroupBy(v => new
                        {
                            Id = v.Categoria?.Id ?? 0,
                            Nombre = v.Categoria?.Nombre ?? "Sin categoría",
                            Orden = v.Categoria?.Orden ?? int.MaxValue
                        })
                        .OrderBy(c => c.Key.Orden).ToList()
                })
                .ToList();

            // Línea de tiempo: cuerpo anclado en v1 + actualizaciones (ResumenCambio)
            var versionPorNota = incluidas
                .Where(sn => sn.NotaVersion != null)
                .GroupBy(sn => sn.NotaPrensaId)
                .ToDictionary(g => g.Key, g => g.First().NotaVersion);
            var lineas = await _notaService.GetLineasTiempoAsync(versionPorNota);

            var nombreArchivo = $"sintesis_{sintesis.Tipo.ToLower()}_{sintesis.Fecha:yyyyMMdd}_{sintesisId}.pdf";
            var pdfDir = Path.Combine(_env.WebRootPath, "pdf");
            Directory.CreateDirectory(pdfDir);
            var pdfPath = Path.Combine(pdfDir, nombreArchivo);

            var meses = new[] { "", "enero", "febrero", "marzo", "abril", "mayo", "junio",
                                "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" };
            // "Ampliacion Matutina/Vespertina" reutiliza la portada de Matutina/Vespertina
            // (misma franja horaria) y se distingue agregando "- AMPLIACIÓN" a la fecha.
            var esAmpliacion = sintesis.Tipo.StartsWith("Ampliacion", StringComparison.OrdinalIgnoreCase);
            var fechaLarga = $"{sintesis.Fecha.Day} de {meses[sintesis.Fecha.Month]} de {sintesis.Fecha.Year}"
                + (esAmpliacion ? " - AMPLIACIÓN" : "");

            // Nombre de la Delegación (si la síntesis pertenece a una sola) — usado para
            // reemplazar la etiqueta genérica "ÁMBITO PARTIDO" en el índice y el cuerpo.
            var delegNombre = sintesis.Delegacion?.DelegacionPrometheus?.Nombre
                           ?? sintesis.Delegacion?.Nombre;

            // "ÁMBITO PARTIDO" es genérico; cuando la síntesis pertenece a una sola Delegación
            // se usa su nombre real (ej. "ÁMBITO LA PLATA"). En la consolidada (sin Delegación
            // única) no hay a quién atribuírselo y se mantiene el genérico.
            string EtiquetaAmbito(string amb) => amb == "Partido" && !string.IsNullOrWhiteSpace(delegNombre)
                ? delegNombre!.ToUpper()
                : amb.ToUpper();

            // Banner de cabecera por Tipo (Vespertina/Matutina/Especial). "Ampliacion Matutina/
            // Vespertina" usa la misma portada que su franja base (la fecha ya distingue que es
            // ampliación). Si todavía no existe el archivo para ese Tipo (ej. Especial), se usa
            // el header de texto de siempre (ver más abajo).
            var tipoBanner = esAmpliacion ? sintesis.Tipo["Ampliacion".Length..].Trim() : sintesis.Tipo;
            var tipoImgPath = Path.Combine(_env.WebRootPath, "img", $"sintesis_{tipoBanner.ToLower()}.png");
            var tieneBanner = File.Exists(tipoImgPath);

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginTop(1.3f, Unit.Centimetre);
                    page.MarginLeft(2.5f, Unit.Centimetre);
                    page.MarginRight(1.2f, Unit.Centimetre);
                    page.MarginBottom(1.2f, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));

                    // ── HEADER ── (solo en la primera página, junto con el índice — ShowOnce
                    // hace que QuestPDF lo muestre una única vez aunque el header se evalúe en
                    // todas las páginas; de la página 2 en adelante no queda nada)
                    page.Header().Column(col =>
                    {
                        if (tieneBanner)
                        {
                            col.Item().ShowOnce().Image(tipoImgPath).FitWidth();
                        }
                        else
                        {
                            col.Item().ShowOnce().Row(row =>
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

                            col.Item().ShowOnce().PaddingVertical(4).LineHorizontal(1).LineColor(Colors.Grey.Medium);

                            col.Item().ShowOnce().AlignCenter().Text($"SÍNTESIS DE PRENSA {sintesis.Tipo.ToUpper()}")
                                .FontSize(16).Bold().FontColor(Color.FromHex("#8B4513"));

                            col.Item().ShowOnce().AlignCenter().Text("Diarios del Conurbano e Interior - Agencias de Noticias")
                                .FontSize(9).Italic();
                            col.Item().ShowOnce().AlignCenter().Text("— Edición Digital —")
                                .FontSize(9).Italic();
                        }

                        col.Item().ShowOnce().PaddingTop(10).AlignCenter().Text(fechaLarga)
                            .FontSize(20).Bold().FontFamily("URW Bookman");
                    });

                    // ── BODY ──
                    page.Content().PaddingTop(36).Column(col =>
                    {
                        col.Item().PaddingBottom(20).Text("CONTENIDO").FontSize(16).Bold()
                            .FontColor(Color.FromHex("#8B4513"));

                        // Fila de índice con leader de puntos (imitando el punteado de Word) del
                        // título al número de página — ClampLines corta la tira de puntos al ancho
                        // disponible, sin desbordar sobre el número de página.
                        void FilaIndice(string texto, string sectionId)
                        {
                            col.Item().PaddingTop(2).Row(row =>
                            {
                                row.AutoItem().AlignMiddle().Text(texto).FontSize(10);
                                row.RelativeItem().AlignMiddle().PaddingHorizontal(4)
                                    .Text(new string('.', 300))
                                    .FontSize(10).FontColor(Colors.Grey.Medium)
                                    .ClampLines(1, "");
                                row.ConstantItem(20).AlignMiddle().AlignRight()
                                    .Text(t => t.BeginPageNumberOfSection(sectionId).FontSize(10));
                            });
                        }

                        foreach (var amb in porAmbito)
                        {
                            var ambSectionId = $"amb-{amb.Ambito}";
                            FilaIndice($"ÁMBITO {EtiquetaAmbito(amb.Ambito)}", ambSectionId);
                            foreach (var cat in amb.Categorias)
                                FilaIndice(cat.Key.Nombre.ToUpper(), $"amb-{amb.Ambito}-cat-{cat.Key.Id}");
                        }

                        // El cuerpo de las notas arranca en una página nueva, separado del índice.
                        col.Item().PageBreak();

                        foreach (var amb in porAmbito)
                        {
                            // ── Encabezado de ÁMBITO (un solo titular) ── (marcado como Section
                            // para que el índice pueda referenciar la página donde arranca)
                            col.Item().Section($"amb-{amb.Ambito}").PaddingTop(10).Text($"ÁMBITO {EtiquetaAmbito(amb.Ambito)}")
                                .FontSize(14).Bold().FontColor(Color.FromHex("#1a5276"));
                            col.Item().PaddingBottom(2).LineHorizontal(1.5f).LineColor(Color.FromHex("#1a5276"));

                          foreach (var grupo in amb.Categorias)
                          {
                            // ── Subsección por CATEGORÍA ── (marcada como Section para el índice)
                            var sectionId = $"amb-{amb.Ambito}-cat-{grupo.Key.Id}";
                            col.Item().Section(sectionId).PaddingTop(6).Text(grupo.Key.Nombre.ToUpper())
                                .FontSize(12).Bold().FontColor(Color.FromHex("#8B4513"));

                            col.Item().PaddingBottom(4).LineHorizontal(0.5f).LineColor(Color.FromHex("#8B4513"));

                            foreach (var snap in grupo.OrderBy(v => v.Partido?.Nombre ?? ""))
                            {
                                // Cuerpo anclado en la versión original (v1) si la noticia evolucionó
                                lineas.TryGetValue(snap.NotaId, out var linea);
                                var tieneHist = linea != null && linea.Actualizaciones.Any();
                                var nota = (tieneHist ? linea!.Ancla : null) ?? snap;

                                col.Item().PaddingTop(4).Column(notaCol =>
                                {
                                    var geo = FormatGeo(nota.Partido, nota.Localidad);
                                    if (geo.Length > 0)
                                        notaCol.Item().Text(geo).FontSize(9).Bold();

                                    notaCol.Item().Text(SinPuntoFinal(nota.Titulo.ToUpper()))
                                        .Bold().FontSize(14).FontFamily("URW Bookman");

                                    if (!string.IsNullOrEmpty(nota.Sintesis))
                                    {
                                        notaCol.Item().Text(nota.Sintesis).FontSize(14).FontFamily("URW Bookman")
                                            .LineHeight(1.15f).Justify();
                                        notaCol.Item().Text("(Síntesis IA)").FontSize(8).Italic()
                                            .FontColor(Colors.Grey.Medium);
                                    }
                                    else
                                    {
                                        notaCol.Item().Text(nota.Texto).FontSize(14).FontFamily("URW Bookman")
                                            .LineHeight(1.15f).Justify();
                                    }

                                    var fuenteTexto = FormatFuente(nota.Fuente, nota.OtrosMedios);
                                    if (fuenteTexto.Length > 0)
                                        notaCol.Item().Text(fuenteTexto)
                                            .FontSize(14).FontFamily("URW Bookman");

                                    if (!string.IsNullOrEmpty(nota.Link))
                                        notaCol.Item().PaddingBottom(10).Text(nota.Link)
                                            .FontSize(12).FontFamily("URW Bookman").FontColor(Colors.Blue.Medium);

                                    if (!string.IsNullOrEmpty(nota.VideoUrl))
                                        notaCol.Item().PaddingBottom(10).Text($"Video: {nota.VideoUrl}")
                                            .FontSize(12).FontFamily("URW Bookman").FontColor(Colors.Blue.Medium);

                                    // Actualizaciones posteriores (ampliaciones) como anotaciones
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
                                                    actCol.Item().Text(act.ResumenCambio ?? "")
                                                        .FontSize(9).LineHeight(1.2f);
                                                });
                                        }
                                    }
                                });
                            }

                            col.Item().PaddingVertical(6).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
                          } // categorías del ámbito
                        } // ámbitos

                        col.Item().PaddingTop(8).Text(
                            "Nota: La información suministrada por las agencias de noticias son anticipos que refieren " +
                            "a posibles temas a tratar en el día de mañana, en los medios periodísticos nacionales o locales.")
                            .FontSize(8).Italic().FontColor(Colors.Grey.Darken2);
                    });

                    // ── FOOTER ──
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
            var s = await db.Sintesis.FindAsync(sintesisId);
            if (s != null)
            {
                // Si ya estaba Remitida, mantener Remitida (no regresar a Generada)
                if (s.Estado != "Remitida")
                    s.Estado = "Generada";
                s.PDFPath = $"/pdf/{nombreArchivo}";
                s.FechaGeneracion = DateTime.Now;
                await db.SaveChangesAsync();
            }

            return $"/pdf/{nombreArchivo}";
        }

        // ── Remitir síntesis ──────────────────────────────────────────

        public async Task<bool> RemitirAsync(int id)
        {
            using var db = _factory.Create();
            var s = await db.Sintesis.Include(x => x.NotasIncluidas)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (s == null || s.Estado != "Generada" || string.IsNullOrEmpty(s.PDFPath)) return false;
            s.Estado = "Remitida";

            // Marcar notas de la síntesis como "Remitida"
            var notaIds = s.NotasIncluidas.Select(sn => sn.NotaPrensaId).ToList();
            var versiones = await db.NotasVersion
                .Where(v => notaIds.Contains(v.NotaId) && v.EsActual && v.EstadoRevision == "Borrador")
                .ToListAsync();
            foreach (var v in versiones)
                v.EstadoRevision = "Remitida";

            await db.SaveChangesAsync();
            return true;
        }

        // ── Convertir síntesis a borrador de sesión ───────────────────
        // Crea una SesionPrensa nueva, mueve las notas a ella y elimina la síntesis.
        // Retorna el Id de la nueva sesión, o -1 si no es posible.

        public async Task<int> ConvertirABorradorAsync(int id, string usuario, int? delegacionId)
        {
            using var db = _factory.Create();
            var s = await db.Sintesis
                .Include(x => x.NotasIncluidas)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (s == null || s.Estado == "Remitida") return -1;

            var sesion = new SesionPrensa
            {
                Fecha = s.Fecha,
                Turno = s.Tipo,
                Estado = "Borrador",
                FechaCreacion = DateTime.Now,
                UsuarioCarga = usuario,
                DelegacionId = delegacionId
            };
            db.SesionesPrensas.Add(sesion);
            await db.SaveChangesAsync();

            var notaIds = s.NotasIncluidas.Select(sn => sn.NotaPrensaId).ToList();
            var notas = await db.NotasPrensa.Where(n => notaIds.Contains(n.Id)).ToListAsync();
            foreach (var nota in notas)
                nota.SesionPrensaId = sesion.Id;

            // Notas vuelven a "Borrador"
            var versionesNotas = await db.NotasVersion
                .Where(v => notaIds.Contains(v.NotaId) && v.EsActual)
                .ToListAsync();
            foreach (var v in versionesNotas)
                if (v.EstadoRevision != "Aprobada" && v.EstadoRevision != "Descartada")
                    v.EstadoRevision = "Borrador";

            // Eliminar archivo PDF físico si existe
            if (!string.IsNullOrEmpty(s.PDFPath))
            {
                var rutaFisica = Path.Combine(_env.WebRootPath, s.PDFPath.TrimStart('/'));
                if (File.Exists(rutaFisica))
                    File.Delete(rutaFisica);
            }

            db.SintesisNotas.RemoveRange(s.NotasIncluidas);
            db.Sintesis.Remove(s);
            await db.SaveChangesAsync();

            return sesion.Id;
        }
    }

    public class GrupoNotaNacProv
    {
        public string Ambito { get; set; } = "";
        public string Titulo { get; set; } = "";
        public string? Url { get; set; }
        public List<NotaEnGrupo> Variantes { get; set; } = new();
        public int NotaIdSeleccionada { get; set; }
    }

    public class NotaEnGrupo
    {
        public int NotaId { get; set; }
        public int VersionId { get; set; }
        public string Titulo { get; set; } = "";
        public string? Fuente { get; set; }
        public string? Url { get; set; }
        public string Delegacion { get; set; } = "";
        public string Ambito { get; set; } = "";
        public string? Categoria { get; set; }
    }
}
