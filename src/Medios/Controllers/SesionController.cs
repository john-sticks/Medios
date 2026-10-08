using Microsoft.AspNetCore.Mvc;
using Medios.Security;
using Medios.Services;
using System.Security.Claims;
using ClosedXML.Excel;

namespace Medios.Controllers
{
    public class SesionController : Controller
    {
        private readonly SesionService _sesionService;
        private readonly NotaService _notaService;
        private readonly SintesisService _sintesisService;
        private readonly AuditoriaService _auditoria;
        private readonly TrazaService _traza;
        private readonly ImagenService _imagen;

        public SesionController(SesionService sesionService, NotaService notaService,
            SintesisService sintesisService, AuditoriaService auditoria, TrazaService traza,
            ImagenService imagen)
        {
            _sesionService = sesionService;
            _notaService = notaService;
            _sintesisService = sintesisService;
            _auditoria = auditoria;
            _traza = traza;
            _imagen = imagen;
        }

        private string GetRol() => User.FindFirst(ClaimTypes.Role)?.Value ?? "";
        private string GetUsuario() => User.FindFirst("Usuario")?.Value ?? User.Identity?.Name ?? "";
        private string GetNombre() => User.Identity?.Name ?? "";
        private string GetIp() =>
            Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString()
            ?? "";

        // ── DELEGACION: mis sesiones ──────────────────────────────────

        [HasPermission("VER_SESIONES_PROPIAS")]
        public async Task<IActionResult> Mias()
        {
            ViewData["Title"] = "Borradores";
            var usuario = GetUsuario();
            var sesiones = await _sesionService.GetMiasAsync(usuario);

            var deleg = await _sesionService.GetDelegacionEfectivaAsync(User);
            if (deleg != null)
            {
                var sinRemitir = await _sesionService.GetNotasSinRemitirByDelegacionAsync(deleg.Id);
                var rechazadas = sinRemitir
                    .Where(n => !string.IsNullOrEmpty(n.VersionActual?.MotivoDescarte))
                    .ToList();
                var enBorrador = await _sesionService.GetBorradorPorNotaOrigenAsync(
                    rechazadas.Select(n => n.Id), usuario);
                // Solo mostrar banner por rechazadas que aún no fueron agregadas a un borrador
                ViewBag.CantNotasRechazadas = rechazadas.Count(n => !enBorrador.ContainsKey(n.Id));
                // Excluir rechazadas del banner "sin informar" para evitar redundancia
                ViewBag.CantNotasSinRemitir = sinRemitir
                    .Count(n => string.IsNullOrEmpty(n.VersionActual?.MotivoDescarte));
            }
            else
            {
                ViewBag.CantNotasSinRemitir = 0;
                ViewBag.CantNotasRechazadas = 0;
            }

            return View(sesiones.Where(s => s.Estado == "Borrador").ToList());
        }

        // ── DELEGACION: notas sin remitir ─────────────────────────────

        [HasPermission("VER_SESIONES_PROPIAS")]
        [HttpPost]
        public async Task<IActionResult> EliminarNotaSinRemitir(int notaId)
        {
            var nota = await _notaService.GetDetalleAsync(notaId);
            if (nota == null)
            {
                TempData["Error"] = "Nota no encontrada";
                return RedirectToAction(nameof(NotasSinRemitir));
            }

            // Verificar que pertenece a la delegación del usuario
            var usuario = GetUsuario();
            var deleg = await _sesionService.GetDelegacionEfectivaAsync(User);
            bool esValida = nota.DelegacionId == deleg?.Id
                || nota.Sesion?.DelegacionId == deleg?.Id;

            if (!esValida)
            {
                TempData["Error"] = "No tenés permiso para eliminar esta nota";
                return RedirectToAction(nameof(NotasSinRemitir));
            }

            // Eliminar: desvincular de sintesis_notas + borrar nota
            await _notaService.EliminarNotaConSintesisAsync(notaId);
            TempData["Ok"] = "Nota eliminada";
            return RedirectToAction(nameof(NotasSinRemitir));
        }

        [HasPermission("VER_SESIONES_PROPIAS")]
        [HttpGet]
        public async Task<IActionResult> NotasSinRemitir()
        {
            ViewData["Title"] = "Notas sin informar";
            var usuario = GetUsuario();
            var deleg = await _sesionService.GetDelegacionEfectivaAsync(User);
            if (deleg == null) return RedirectToAction(nameof(Mias));

            var notas = await _sesionService.GetNotasSinRemitirByDelegacionAsync(deleg.Id);
            var borradores = (await _sesionService.GetMiasAsync(usuario))
                .Where(s => s.Estado == "Borrador").ToList();

            var borradorPorNota = await _sesionService.GetBorradorPorNotaOrigenAsync(
                notas.Select(n => n.Id), usuario);

            ViewBag.Borradores = borradores;
            ViewBag.BorradorPorNota = borradorPorNota;
            return View(notas);
        }

        // ── DELEGACION: cambiar nota sin remitir a otro borrador ──────

        [HasPermission("CREAR_SESION")]
        [HttpPost]
        public async Task<IActionResult> CambiarBorrador(int notaOrigenId, int nuevaSesionId)
        {
            var ok = await _sesionService.CambiarBorradorNotaAsync(notaOrigenId, nuevaSesionId, GetUsuario());
            if (!ok)
                TempData["Error"] = "No se pudo cambiar el borrador";
            else
                TempData["Ok"] = "Nota movida al nuevo borrador";

            return RedirectToAction(nameof(NotasSinRemitir));
        }

        // ── DELEGACION: eliminar borrador ─────────────────────────────

        [HasPermission("CREAR_SESION")]
        [HttpPost]
        public async Task<IActionResult> EliminarBorrador(int id, bool eliminarNotas = true)
        {
            var ok = await _sesionService.EliminarBorradorAsync(id, GetUsuario(), eliminarNotas);
            if (!ok)
            {
                TempData["Error"] = "No se puede eliminar esta sesión";
                return RedirectToAction(nameof(Mias));
            }

            await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "sesiones_prensa", "POST", "eliminar_borrador",
                new { sesionId = id, eliminarNotas });

            TempData["Ok"] = eliminarNotas ? "Borrador y notas eliminados" : "Borrador eliminado. Las notas quedaron disponibles en el módulo Notas.";
            return RedirectToAction(nameof(Mias));
        }

        // ── DELEGACION: agregar nota libre a sesión ───────────────────

        [HasPermission("CREAR_SESION")]
        [HttpPost]
        public async Task<IActionResult> AgregarNotaExistente(int sesionId, int notaId)
        {
            // Verificar si la nota es una ampliación (está en otra sesión) o una nota libre
            var nota = await _notaService.GetDetalleAsync(notaId);
            bool esAmpliacion = nota?.SesionPrensaId != null && nota.SesionPrensaId != sesionId;

            if (esAmpliacion)
            {
                // Nota ampliada: crear copia en el borrador (sin mover la original)
                var copiaId = await _notaService.AgregarDesdeVersionAsync(sesionId, notaId, GetNombre());
                if (copiaId == 0)
                    TempData["Error"] = "No se pudo agregar la nota al borrador";
                else
                    TempData["Ok"] = "Nota agregada al borrador";
            }
            else
            {
                // Nota libre: mover al borrador
                var ok = await _sesionService.AgregarNotaExistenteAsync(sesionId, notaId, GetUsuario());
                if (!ok)
                    TempData["Error"] = "No se pudo agregar la nota al borrador";
                else
                    TempData["Ok"] = "Nota agregada al borrador";
            }

            return RedirectToAction(nameof(Detalle), new { id = sesionId });
        }

        // ── DELEGACION: nueva sesión ──────────────────────────────────

        [HasPermission("CREAR_SESION")]
        [HttpGet]
        public IActionResult Nueva()
        {
            ViewData["Title"] = "Nueva Sesión de Prensa";
            ViewBag.Turnos = new[] { "Matutina", "Vespertina", "Ampliacion Matutina", "Ampliacion Vespertina", "Especial" };
            ViewBag.FechaHoy = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
            return View();
        }

        [HasPermission("CREAR_SESION")]
        [HttpPost]
        public async Task<IActionResult> Nueva(string fecha, string turno)
        {
            if (!DateOnly.TryParse(fecha, out var fechaParsed))
            {
                TempData["Error"] = "Fecha inválida";
                return RedirectToAction(nameof(Nueva));
            }

            var usuario = GetUsuario();
            var delegacion = await _sesionService.GetDelegacionEfectivaAsync(User);

            var sesionId = await _sesionService.CrearAsync(delegacion?.Id, fechaParsed, turno, usuario);

            if (sesionId == 0)
            {
                TempData["Error"] = $"Ya existe una sesión {turno} para el {fechaParsed:dd/MM/yyyy}. No se pueden crear dos sesiones del mismo turno en el mismo día.";
                return RedirectToAction(nameof(Nueva));
            }

            await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "sesiones_prensa", "POST", "crear",
                new { sesionId, fecha, turno, delegacion = delegacion?.Nombre });

            return RedirectToAction(nameof(Detalle), new { id = sesionId });
        }

        // ── DELEGACION: detalle de sesión (agregar notas) ─────────────

        [HasPermission("CREAR_SESION", "REVISAR_SESION")]
        public async Task<IActionResult> Detalle(int id)
        {
            var sesion = await _sesionService.GetByIdAsync(id);
            if (sesion == null) return NotFound();

            // Solo puede ver sus propias sesiones
            var usuario = GetUsuario();
            // OrdinalIgnoreCase: hay UsuarioCarga históricos con distinta capitalización
            // ("Delegacion" vs "delegacion") — MySQL ya compara sin distinguir mayúsculas,
            // esta comparación en memoria tiene que ser consistente con eso.
            var esPropietario = string.Equals(sesion.UsuarioCarga, usuario, StringComparison.OrdinalIgnoreCase)
                             || string.Equals(sesion.UsuarioCarga, GetNombre(), StringComparison.OrdinalIgnoreCase);
            var puedeVerTodas = User.IsInRole("MEDIOS");

            if (!esPropietario && !puedeVerTodas) return Forbid();

            ViewData["Title"] = $"Sesión {sesion.Turno} — {sesion.Fecha:dd/MM/yyyy}";
            ViewBag.Categorias = await _notaService.GetCategoriasAsync();
            ViewBag.Caratulas = await _notaService.GetCaratulasAsync();

            // Para DELEGACION: filtrar partidos por su propia delegación (vínculo directo)
            List<Medios.Entities.Partido> partidos;
            if (User.IsInRole("DELEGACION"))
            {
                var delegacion = await _sesionService.GetDelegacionEfectivaAsync(User);
                partidos = delegacion != null
                    ? await _notaService.GetPartidosByDelegacionAsync(delegacion.Id)
                    : await _notaService.GetPartidosAsync();
            }
            else
            {
                partidos = await _notaService.GetPartidosAsync();
            }

            ViewBag.Partidos = partidos;
            ViewBag.EsPropietario = esPropietario;

            var origenIds = sesion.Notas
                .Where(n => n.NotaOrigenId.HasValue)
                .Select(n => n.NotaOrigenId!.Value)
                .ToList();
            ViewBag.SintesisPorOrigen = await _sintesisService.GetSintesisPorNotaOrigenAsync(origenIds);

            // Snapshot de versiones para sesiones remitidas/revisadas
            // Muestra la versión que fue capturada en la síntesis, no la actual
            if (sesion.Estado is "Remitida" or "Finalizada")
            {
                var notaIds = sesion.Notas.Select(n => n.Id).ToList();
                var snapshots = await _sintesisService.GetSnapshotVersionesPorNotaAsync(notaIds);
                ViewBag.SnapshotVersiones = snapshots;

                // Línea de tiempo: por cada nota, la versión "vista" en esta síntesis
                // (snapshot congelado si existe, sino la versión actual) marca el corte temporal
                var versionPorNota = new Dictionary<int, Medios.Entities.NotaVersion>();
                foreach (var n in sesion.Notas)
                {
                    var vShown = snapshots.TryGetValue(n.Id, out var snap) ? snap : n.VersionActual;
                    if (vShown != null) versionPorNota[n.Id] = vShown;
                }
                ViewBag.LineasTiempo = await _notaService.GetLineasTiempoAsync(versionPorNota);
            }

            if (esPropietario && sesion.Estado == "Borrador")
            {
                // 1. Notas libres (sin sesión)
                var libres = await _notaService.GetNotasLibresAsync(GetNombre());

                // 2. Notas "Sin Remitir" en sesiones de la delegación
                //    (ampliaciones pendientes de envío, no vinculadas a ningún borrador activo)
                var notasEnSesionActual = sesion.Notas.Select(n => n.Id).ToHashSet();
                var deleg2 = await _sesionService.GetDelegacionEfectivaAsync(User);
                var sinRemitirDeleg = deleg2 != null
                    ? await _sesionService.GetNotasSinRemitirByDelegacionAsync(deleg2.Id)
                    : new List<Medios.Entities.NotaPrensa>();

                // Excluir las que ya están en este borrador y las que tienen MotivoDescarte
                var sinRemitirDisponibles = sinRemitirDeleg
                    .Where(n => !notasEnSesionActual.Contains(n.Id)
                             && string.IsNullOrEmpty(n.VersionActual?.MotivoDescarte))
                    .ToList();

                // Combinar sin duplicados
                var todas = libres
                    .Concat(sinRemitirDisponibles)
                    .DistinctBy(n => n.Id)
                    .ToList();

                ViewBag.NotasLibres = todas;
            }
            else
                ViewBag.NotasLibres = new List<Medios.Entities.NotaPrensa>();

            return View(sesion);
        }

        // ── DELEGACION: agregar nota a sesión ─────────────────────────

        [HasPermission("CREAR_SESION")]
        [HttpPost]
        public async Task<IActionResult> AgregarNota(
            int sesionId, int? categoriaId, int? partidoId, int? localidadId,
            string titulo, string texto, string? fuente, string? link, bool esRepercusion = false,
            string? direccion = null, double? latitud = null, double? longitud = null,
            string? sintesis = null, string? fechaNoticia = null,
            int? caratulaQuironId = null, int? modalidadQuironId = null,
            string? ambitoNota = null, string? imagenUrl = null, string? videoUrl = null,
            List<string>? imagenesExtra = null, string? otrosMedios = null)
        {
            if (string.IsNullOrWhiteSpace(titulo) || string.IsNullOrWhiteSpace(texto)
                || string.IsNullOrWhiteSpace(fuente))
            {
                TempData["Error"] = "Título, texto y fuente son obligatorios";
                return RedirectToAction(nameof(Detalle), new { id = sesionId });
            }

            var imagenLocal = await _imagen.ProcesarDesdeUrlAsync(imagenUrl);
            var extrasJson = await _imagen.ProcesarVariasJsonAsync(imagenesExtra);
            DateTime? fn = DateTime.TryParse(fechaNoticia, out var fnParsed) ? fnParsed : null;
            var nuevaNotaId = await _notaService.AgregarAsync(sesionId, categoriaId, partidoId, localidadId,
                titulo, texto, fuente, link, esRepercusion, GetNombre(),
                direccion, latitud, longitud, sintesis, fn,
                caratulaQuironId, modalidadQuironId, ambitoNota, imagenLocal, videoUrl, extrasJson, otrosMedios);
            await _traza.RegistrarAsync(TrazaService.NotaCreada, GetNombre(), GetRol(),
                notaId: nuevaNotaId, sesionId: sesionId, version: 1, estado: "Borrador");

            TempData["Ok"] = "Nota agregada";
            return RedirectToAction(nameof(Detalle), new { id = sesionId });
        }

        // ── DELEGACION: agregar nota actualizada desde síntesis ──────────

        [HasPermission("CREAR_SESION")]
        [HttpPost]
        public async Task<IActionResult> AgregarNotaActualizada(int sesionId, int notaOrigenId, int sintesisId = 0, string? returnUrl = null)
        {
            var usuario = GetUsuario();
            var sesion = await _sesionService.GetByIdAsync(sesionId);

            IActionResult Redirigir() =>
                !string.IsNullOrEmpty(returnUrl) ? Redirect(returnUrl) :
                sintesisId > 0 ? RedirectToAction("Detalle", "Sintesis", new { id = sintesisId }) :
                RedirectToAction(nameof(Mias));

            if (sesion == null || sesion.UsuarioCarga != usuario || sesion.Estado != "Borrador")
            {
                TempData["Error"] = "Sesión no válida o no pertenece a tu usuario";
                return Redirigir();
            }

            var notaId = await _notaService.AgregarDesdeVersionAsync(sesionId, notaOrigenId, GetNombre());
            if (notaId == 0)
            {
                TempData["Error"] = "No se pudo agregar la nota";
                return Redirigir();
            }

            await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "sesiones_prensa", "POST", "agregar_nota_actualizada",
                new { sesionId, notaOrigenId, notaId });

            TempData["Ok"] = "Nota agregada al borrador";
            return Redirigir();
        }

        // ── DELEGACION: eliminar nota (solo borrador) ─────────────────

        [HasPermission("CREAR_SESION")]
        [HttpPost]
        public async Task<IActionResult> EliminarNota(int notaId, int sesionId)
        {
            var usuario = GetUsuario();
            await _notaService.EliminarAsync(notaId, usuario);
            return RedirectToAction(nameof(Detalle), new { id = sesionId });
        }

        // ── DELEGACION: publicar borrador como síntesis ───────────────

        [HasPermission("CREAR_SESION")]
        [HttpPost]
        public async Task<IActionResult> GenerarPdf(int id)
        {
            try
            {
                var sintesisId = await _sesionService.ConvertirASintesisAsync(id, GetUsuario());
                if (sintesisId == -2)
                {
                    TempData["Error"] = "Ya existe una síntesis de ese tipo para esta delegación en la fecha indicada. Solo se permite una Matutina y una Vespertina por delegación por día.";
                    return RedirectToAction(nameof(Detalle), new { id });
                }
                if (sintesisId <= 0)
                {
                    TempData["Error"] = "No se pudo publicar: verificá que el borrador sea propio y tenga notas completas.";
                    return RedirectToAction(nameof(Detalle), new { id });
                }

                await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "sesiones_prensa", "POST", "publicar_sintesis",
                    new { sesionId = id, sintesisId });

                if (User.IsInRole("MEDIOS"))
                {
                    TempData["Ok"] = "Síntesis finalizada. Ya está disponible para consolidar.";
                    return Redirect("/Sesion/Bandeja#finalizadas");
                }
                TempData["Ok"] = "Síntesis creada. En Bandeja podés generar el PDF y luego remitirla.";
                return Redirect("/Sintesis/Listado#generadas");
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error al publicar síntesis: {ex.Message}";
                return RedirectToAction(nameof(Detalle), new { id });
            }
        }

        // ── MEDIOS: revertir una Finalizada propia a Borrador para editarla ──

        [HasPermission("CREAR_SESION")]
        [HttpPost]
        public async Task<IActionResult> RevertirFinalizada(int id)
        {
            if (!User.IsInRole("MEDIOS")) return Forbid();
            var ok = await _sesionService.RevertirFinalizadaABorradorAsync(id);
            if (ok)
            {
                await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "sesiones_prensa", "POST", "revertir_finalizada",
                    new { sesionId = id });
                TempData["Ok"] = "Síntesis devuelta a Borrador. Ya podés editarla.";
            }
            else
            {
                TempData["Error"] = "No se pudo revertir: ya fue informada/consolidada, o no es una finalizada propia de MEDIOS.";
            }
            return Redirect(ok ? "/Sesion/Bandeja#borradores" : "/Sesion/Bandeja#finalizadas");
        }

        [HasPermission("CREAR_SESION")]
        public async Task<IActionResult> DescargarPdf(int id)
        {
            var sesion = await _sesionService.GetByIdAsync(id);
            if (sesion?.PDFPath == null) return NotFound();
            var filePath = Path.Combine(_env.WebRootPath, sesion.PDFPath.TrimStart('/'));
            if (!System.IO.File.Exists(filePath)) return NotFound();
            var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
            var nombre = $"sesion_{sesion.Turno}_{sesion.Fecha:ddMMyyyy}.pdf";
            return File(bytes, "application/pdf", nombre);
        }

        [HasPermission("REVISAR_SESION")]
        [HttpPost]
        public async Task<IActionResult> MarcarFinalizada(int id)
        {
            var ok = await _sesionService.MarcarFinalizadaAsync(id);
            if (ok)
            {
                await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "sesiones_prensa", "POST", "marcar_finalizada", new { sesionId = id });
                await _traza.RegistrarAsync(TrazaService.SesionFinalizada, GetNombre(), GetRol(),
                    sesionId: id, estado: "Finalizada", detalle: "Analista finalizó la revisión");
                TempData["Ok"] = "Síntesis marcada como finalizada. Las notas rechazadas volvieron a la delegación.";
            }
            else
            {
                TempData["Error"] = "No se puede finalizar: quedan notas sin atender.";
            }
            return RedirectToAction(nameof(Detalle), new { id });
        }

        [HasPermission("REMITIR_SESION")]
        [HttpPost]
        public async Task<IActionResult> Remitir(int id)
        {
            var usuario = GetUsuario();
            var ok = await _sesionService.RemitirAsync(id, usuario);

            if (!ok)
            {
                TempData["Error"] = "No se puede remitir: primero generá el PDF de la sesión";
                return RedirectToAction(nameof(Detalle), new { id });
            }

            await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "sesiones_prensa", "POST", "remitir",
                new { sesionId = id });
            await _traza.RegistrarAsync(TrazaService.SesionRemitida, GetNombre(), GetRol(),
                sesionId: id, estado: "Remitida", detalle: "Elevada al Operador");

            TempData["Ok"] = "Sesión remitida correctamente";
            return RedirectToAction(nameof(Mias));
        }

        // ── OPERADOR/ANALISTA: bandeja de sesiones con filtros ────────

        [HasPermission("VER_SESIONES")]
        public async Task<IActionResult> Bandeja(
            string? estado = null,
            int? delegacionId = null,
            string? fechaDesde = null,
            string? fechaHasta = null,
            string? turno = null)
        {
            ViewBag.FiltroEstado      = estado;
            ViewBag.FiltroDelegacionId = delegacionId;
            ViewBag.FiltroFechaDesde  = fechaDesde ?? DateOnly.FromDateTime(DateTime.Today.AddDays(-7)).ToString("yyyy-MM-dd");
            ViewBag.FiltroFechaHasta  = fechaHasta ?? DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
            ViewBag.FiltroTurno       = turno;
            ViewBag.Delegaciones      = await _sesionService.GetDelegacionesAsync();
            ViewBag.Turnos            = new[] { "", "Matutina", "Vespertina", "Ampliacion Matutina", "Ampliacion Vespertina", "Especial" };

            // MEDIOS: bandeja única (Remitidas → Procesando → Finalizadas → Consolidadas → Informadas).
            // Trae siempre las tres etapas de SesionPrensa (filtroEstado = null) y filtra por
            // pestaña client-side; Consolidadas/Informadas vienen aparte como entidades Sintesis.
            if (User.IsInRole("MEDIOS"))
            {
                ViewData["Title"] = "Bandeja de Síntesis";
                DateOnly? desdeM = DateOnly.TryParse(fechaDesde, out var dm) ? dm : null;
                DateOnly? hastaM = DateOnly.TryParse(fechaHasta, out var hm) ? hm : null;
                var sesionesM = await _sesionService.GetBandejaAnalistaAsync(null, delegacionId, desdeM, hastaM, turno, GetUsuario());
                ViewBag.SintesisConsolidadas = await _sintesisService.GetConsolidasAsync();
                ViewBag.SintesisInformadas   = await _sintesisService.GetInformadasAsync();
                ViewBag.Destinos   = new[] { "Matutina", "Vespertina", "Especial" };
                ViewBag.Categorias = await _notaService.GetCategoriasAsync();
                ViewBag.Caratulas  = await _notaService.GetCaratulasAsync();
                ViewBag.Partidos   = await _notaService.GetPartidosAsync();
                ViewBag.DivisionMediosId = (await _sesionService.GetDelegacionDivisionMediosAsync())?.Id;
                return View("BandejaMedios", sesionesM);
            }

            ViewData["Title"] = "Bandeja de Sesiones";

            DateOnly? desde = DateOnly.TryParse(fechaDesde, out var d) ? d : null;
            DateOnly? hasta = DateOnly.TryParse(fechaHasta, out var h) ? h : null;

            var sesiones = await _sesionService.GetBandejaAsync(estado, delegacionId, desde, hasta, turno);
            return View(sesiones);
        }

        // ── ANALISTA: finalizar sesión ────────────────────────────────

        [HasPermission("REVISAR_SESION")]
        [HttpPost]
        public async Task<IActionResult> FinalizarSesion(int id)
        {
            var ok = await _sesionService.MarcarFinalizadaAsync(id);
            if (!ok) TempData["Error"] = "No se pudo finalizar: quedan notas sin atender.";
            else
            {
                await _traza.RegistrarAsync(TrazaService.SesionFinalizada, GetNombre(), GetRol(),
                    sesionId: id, estado: "Finalizada", detalle: "Analista finalizó la sesión");
                TempData["Ok"] = "Sesión finalizada.";
            }
            return RedirectToAction(nameof(Bandeja));
        }

        // ── ANALISTA: agregar nota a sesión Revisada/Finalizada ───────

        [HasPermission("ANALIZAR_SESION")]
        [HttpPost]
        public async Task<IActionResult> AgregarNotaAnalista(
            int sesionId, int? categoriaId, int? partidoId, int? localidadId,
            string titulo, string texto, string? fuente, string? link,
            bool esRepercusion = false,
            string? sintesis = null, string? fechaNoticia = null,
            int? caratulaQuironId = null, int? modalidadQuironId = null,
            string? direccion = null, double? latitud = null, double? longitud = null,
            string? ambitoNota = null, string? imagenUrl = null, string? videoUrl = null,
            List<string>? imagenesExtra = null, string? otrosMedios = null)
        {
            if (string.IsNullOrWhiteSpace(titulo) || string.IsNullOrWhiteSpace(texto))
            {
                TempData["Error"] = "Título y texto son obligatorios.";
                return RedirectToAction(nameof(Detalle), new { id = sesionId });
            }

            var imagenLocal = await _imagen.ProcesarDesdeUrlAsync(imagenUrl);
            var extrasJson = await _imagen.ProcesarVariasJsonAsync(imagenesExtra);
            DateTime? fn = DateTime.TryParse(fechaNoticia, out var fnP) ? fnP : null;
            var notaId = await _notaService.AgregarAnalistaAsync(
                sesionId, categoriaId, partidoId, localidadId,
                titulo, texto, fuente, link, esRepercusion, GetUsuario(),
                sintesis, fn, caratulaQuironId, modalidadQuironId,
                direccion, latitud, longitud, ambitoNota, imagenLocal, videoUrl, extrasJson, otrosMedios);

            if (notaId == 0)
                TempData["Error"] = "No se pudo agregar la nota (la sesión debe estar Remitida o Finalizada).";
            else
            {
                await _traza.RegistrarAsync(TrazaService.NotaAgregada, GetNombre(), GetRol(),
                    notaId: notaId, sesionId: sesionId, version: 1, estado: "Agregada",
                    detalle: "Nota agregada por el Analista");
                TempData["Ok"] = "Nota agregada.";
            }

            return RedirectToAction(nameof(Detalle), new { id = sesionId });
        }

        // ── ANALISTA: eliminar nota Agregada ──────────────────────────

        [HasPermission("ANALIZAR_SESION")]
        [HttpPost]
        public async Task<IActionResult> EliminarNotaAgregada(int notaId, int sesionId)
        {
            var ok = await _notaService.EliminarNotaAgregadaAsync(notaId, GetUsuario());
            if (!ok) TempData["Error"] = "No se pudo eliminar la nota.";
            else     TempData["Ok"]    = "Nota eliminada.";
            return RedirectToAction(nameof(Detalle), new { id = sesionId });
        }

        // ── Export Excel de sesión ────────────────────────────────────

        [HasPermission("VER_SESIONES")]
        public async Task<IActionResult> ExportarExcel(int id)
        {
            var sesion = await _sesionService.GetByIdAsync(id);
            if (sesion == null) return NotFound();

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Notas");

            ws.Cell(1, 1).Value = $"Sesión {sesion.Turno} — {sesion.Fecha:dd/MM/yyyy}";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 10).Merge();

            ws.Cell(2, 1).Value = "Delegación:";
            ws.Cell(2, 2).Value = sesion.Delegacion?.Nombre ?? "División Medios (interno)";
            ws.Cell(3, 1).Value = "Estado:";
            ws.Cell(3, 2).Value = sesion.Estado;

            var headers = new[] { "Categoría", "Partido", "Localidad", "Título", "Texto", "Fuente", "Link", "Estado", "Carátula", "Modalidad" };
            for (int i = 0; i < headers.Length; i++)
            {
                ws.Cell(5, i + 1).Value = headers[i];
                ws.Cell(5, i + 1).Style.Font.Bold = true;
                ws.Cell(5, i + 1).Style.Fill.BackgroundColor = XLColor.LightBlue;
            }

            int row = 6;
            foreach (var nota in sesion.Notas.OrderBy(n => n.VersionActual?.Categoria?.Orden ?? 99))
            {
                var v = nota.VersionActual;
                ws.Cell(row, 1).Value = v?.Categoria?.Nombre ?? "";
                ws.Cell(row, 2).Value = v?.Partido?.Nombre ?? "";
                ws.Cell(row, 3).Value = v?.Localidad?.Nombre ?? "";
                ws.Cell(row, 4).Value = v?.Titulo ?? "";
                ws.Cell(row, 5).Value = v?.Texto ?? "";
                ws.Cell(row, 6).Value = v?.Fuente ?? "";
                ws.Cell(row, 7).Value = v?.Link ?? "";
                ws.Cell(row, 8).Value = v?.EstadoRevision ?? "";
                ws.Cell(row, 9).Value = v?.CaratulaQuiron?.Nombre ?? "";
                ws.Cell(row, 10).Value = v?.ModalidadQuiron?.Nombre ?? "";

                ws.Range(row, 1, row, 10).Style.Fill.BackgroundColor = v?.EstadoRevision switch {
                    "Aprobada"   => XLColor.LightGreen,
                    "Descartada" => XLColor.LightCoral,
                    _            => XLColor.LightYellow
                };
                row++;
            }

            ws.Columns().AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            var nombre = $"sesion_{sesion.Turno}_{sesion.Fecha:yyyyMMdd}_{id}.xlsx";
            return File(ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                nombre);
        }

        // ── API: localidades por partido (para select dinámico) ───────

        [HttpGet]
        public async Task<IActionResult> LocalidadesByPartido(int partidoId)
        {
            var localidades = await _notaService.GetLocalidadesByPartidoAsync(partidoId);
            return Json(localidades.Select(l => new { l.Id, l.Nombre }));
        }

        [HttpGet]
        public async Task<IActionResult> ModalidadesByCaratula(int caratulaId)
        {
            var modalidades = await _notaService.GetModalidadesByCaratulaAsync(caratulaId);
            return Json(modalidades.Select(m => new { m.Id, m.Nombre }));
        }

        private IWebHostEnvironment _env =>
            HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
    }
}
