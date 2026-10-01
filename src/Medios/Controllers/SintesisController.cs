using Microsoft.AspNetCore.Mvc;
using Medios.Security;
using Medios.Services;
using System.Security.Claims;

namespace Medios.Controllers
{
    public class SintesisController : Controller
    {
        private readonly SintesisService _sintesisService;
        private readonly SesionService _sesionService;
        private readonly AuditoriaService _auditoria;
        private readonly MenuXmlService _menuService;
        private readonly EmailService _email;
        private readonly TrazaService _traza;
        private readonly DelegacionService _delegService;

        public SintesisController(SintesisService sintesisService, SesionService sesionService,
            AuditoriaService auditoria, MenuXmlService menuService, EmailService email, TrazaService traza,
            DelegacionService delegService)
        {
            _sintesisService = sintesisService;
            _sesionService = sesionService;
            _auditoria = auditoria;
            _menuService = menuService;
            _email = email;
            _traza = traza;
            _delegService = delegService;
        }

        private string GetRol() => User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
        private string GetUsuario() => User.FindFirst("Usuario")?.Value ?? User.Identity?.Name ?? "";
        private string GetNombre() => User.Identity?.Name ?? "";
        private string GetIp() =>
            Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString()
            ?? "";
        private bool EsDelegacion() =>
            string.Equals(User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value,
                "DELEGACION", StringComparison.OrdinalIgnoreCase);

        // ── Listado de síntesis ───────────────────────────────────────

        [HasPermission("VER_SINTESIS")]
        public async Task<IActionResult> Listado()
        {
            ViewData["Title"] = "Síntesis";
            var operador = EsDelegacion() ? GetUsuario() : null;
            int? delegacionFiltro = null;
            if (User.IsInRole("MEDIOS"))
            {
                var delegMedios = await _sesionService.GetDelegacionDivisionMediosAsync();
                delegacionFiltro = delegMedios?.Id;
            }
            var lista = await _sintesisService.GetListadoAsync(operadorFiltro: operador, delegacionFiltro: delegacionFiltro);

            // Para DELEGACION: notas rechazadas (negadas por el operador). Ya no dependen de la
            // síntesis: quedan como notas "Sin Remitir" con MotivoDescarte para re-tratarlas.
            if (EsDelegacion())
            {
                var deleg = await _sesionService.GetDelegacionEfectivaAsync(User);
                var rechazadas = deleg == null
                    ? new List<Medios.Entities.NotaPrensa>()
                    : (await _sesionService.GetNotasSinRemitirByDelegacionAsync(deleg.Id))
                        .Where(n => !string.IsNullOrEmpty(n.VersionActual?.MotivoDescarte))
                        .ToList();

                var notasRechazadas = rechazadas
                    .Select(n => new {
                        Nota = n,
                        SintesisTipo = n.Sesion?.Turno ?? "",
                        SintesisFecha = n.Sesion?.Fecha ?? DateOnly.FromDateTime(DateTime.Today),
                        SintesisId = 0,
                        NotaVersionId = n.VersionActualId ?? 0
                    })
                    .ToList();
                ViewBag.NotasRechazadas = notasRechazadas;
            }

            return View(lista);
        }

        // ── MEDIOS: consolidar síntesis de delegaciones ──

        // Consolidar e Informar son responsabilidad de MEDIOS
        private bool PuedeConsolidar() =>
            User.IsInRole("MEDIOS");

        // Una delegación puede ver/descargar una síntesis si es la suya, si es una consolidada
        // publicada para Todos los Roles, o si está entre los destinos in-app seleccionados
        // (CanalTodasDelegaciones o destino específico para su delegación).
        private async Task<bool> DelegacionPuedeVerAsync(Medios.Entities.Sintesis s)
        {
            if (string.Equals(s.OperadorGenera, GetUsuario(), StringComparison.OrdinalIgnoreCase)) return true;
            if (s.DelegacionId != null || s.Estado != "Informada") return false;
            if (s.CanalTodosRoles || s.CanalTodasDelegaciones) return true;
            var deleg = await _sesionService.GetDelegacionEfectivaAsync(User);
            return deleg != null && s.DelegacionesDestino.Any(d => d.DelegacionId == deleg.Id);
        }

        [HasPermission("CREAR_SINTESIS")]
        [HttpGet]
        public async Task<IActionResult> Consolidar(string? fecha)
        {
            if (!PuedeConsolidar()) return Forbid();
            ViewData["Title"] = "Consolidar Síntesis";

            // Fecha es un filtro OPCIONAL para acotar el listado (vacío = todas las finalizadas).
            DateOnly? fechaFiltro = DateOnly.TryParse(fecha, out var f) ? f : (DateOnly?)null;
            var sesiones = await _sintesisService.GetSesionesFinalizadasParaConsolidarAsync(fechaFiltro);

            ViewBag.Fecha = fechaFiltro?.ToString("yyyy-MM-dd") ?? "";
            ViewBag.Destinos = new[] { "Matutina", "Vespertina", "Especial" };
            ViewBag.FechaHoy = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");

            // Para avisar en el Paso 3 si la combinación Fecha+Tipo elegida ya tiene una
            // consolidada generada, antes de que el guard de CrearAsync la rechace recién al enviar.
            var fechasTipoConsolidados = await _sintesisService.GetFechasTipoConsolidadosAsync();
            ViewBag.FechasTipoConsolidadosJson = System.Text.Json.JsonSerializer.Serialize(
                fechasTipoConsolidados.Select(x => new { fecha = x.Fecha.ToString("yyyy-MM-dd"), tipo = x.Tipo }));

            // Agrupar por delegación
            var porDelegacion = sesiones
                .GroupBy(s => s.Delegacion?.Nombre ?? "División Medios")
                .OrderBy(g => g.Key)
                .ToList();
            ViewBag.PorDelegacion = porDelegacion;

            return View(sesiones);
        }

        // Endpoint AJAX: devuelve grupos de notas Nacional/Provincial para las sesiones elegidas
        [HasPermission("CREAR_SINTESIS")]
        [HttpGet]
        public async Task<IActionResult> NotasNacProv([FromQuery] string sesionIdsJson)
        {
            if (!PuedeConsolidar()) return Forbid();
            List<int> ids;
            try { ids = System.Text.Json.JsonSerializer.Deserialize<List<int>>(sesionIdsJson) ?? new(); }
            catch { return BadRequest(); }
            var grupos = await _sintesisService.GetNotasNacProvParaConsolidarAsync(ids);
            return Ok(grupos);
        }

        [HasPermission("CREAR_SINTESIS")]
        [HttpPost]
        public async Task<IActionResult> Consolidar(string fecha, string destino, string sesionIdsJson,
            string? notaNacProvIdsJson = null, string? notaIdsJson = null)
        {
            if (!PuedeConsolidar()) return Forbid();

            if (!DateOnly.TryParse(fecha, out var fechaParsed))
                fechaParsed = DateOnly.FromDateTime(DateTime.Today);

            List<int> sesionIds;
            try
            {
                sesionIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(sesionIdsJson) ?? new();
            }
            catch
            {
                TempData["Error"] = "Error al procesar las sesiones seleccionadas";
                return RedirectToAction(nameof(Consolidar), new { fecha });
            }

            if (!sesionIds.Any())
            {
                TempData["Error"] = "Seleccioná al menos una síntesis de delegación";
                return RedirectToAction(nameof(Consolidar), new { fecha });
            }

            // Cargar las sesiones seleccionadas (de cualquier fecha/tipo)
            var sesiones = await _sintesisService.GetSesionesParaConsolidarByIdsAsync(sesionIds);

            // Notas de las sesiones elegidas que van a la consolidada (aprobadas/finalizadas/
            // agregadas). Se excluyen las descartadas (Descartada o "Sin Remitir" con motivo).
            var notas = sesiones.SelectMany(s => s.Notas)
                .Where(n => n.VersionActual != null
                    && (n.VersionActual.EstadoRevision == "Aprobada"
                     || n.VersionActual.EstadoRevision == "Finalizada"
                     || n.VersionActual.EstadoRevision == "Agregada"))
                .ToList();
            var notaIds = notas.Select(n => n.Id).ToList();
            if (!notaIds.Any())
            {
                TempData["Error"] = "Las sesiones seleccionadas no tienen notas";
                return RedirectToAction(nameof(Consolidar), new { fecha });
            }

            // Versión a congelar = snapshot Finalizada (sino la actual)
            var snaps = await _sintesisService.GetSnapshotVersionesPorNotaAsync(notaIds);
            Medios.Entities.NotaVersion? VerDe(Medios.Entities.NotaPrensa n)
                => snaps.TryGetValue(n.Id, out var sv) ? sv : n.VersionActual;

            // Selección por nota (Paso 1 desplegado) — fuente de verdad de qué entra a la
            // consolidada. Si no viene (compatibilidad), se incluyen todas las notas.
            HashSet<int>? sel = null;
            if (!string.IsNullOrWhiteSpace(notaIdsJson))
            {
                try { sel = System.Text.Json.JsonSerializer.Deserialize<List<int>>(notaIdsJson)?.ToHashSet(); }
                catch { sel = null; }
            }

            var notasCombinadas = notas.Where(n => sel == null || sel.Contains(n.Id)).ToList();
            var notaIdsCombinados = notasCombinadas.Select(n => n.Id).ToList();
            if (!notaIdsCombinados.Any())
            {
                TempData["Error"] = "Seleccioná al menos una nota para la consolidada";
                return RedirectToAction(nameof(Consolidar), new { fecha });
            }

            // Deduplicar por linaje: una misma noticia (base + ampliación) = una sola entrada
            var roots = await _sintesisService.GetLineageRootsAsync(notaIdsCombinados);
            var finalNotaIds = new List<int>();
            var versionOverride = new Dictionary<int, int>();
            foreach (var grupo in notasCombinadas.GroupBy(n => roots.TryGetValue(n.Id, out var r) ? r : n.Id))
            {
                var elegida = grupo
                    .OrderByDescending(n => VerDe(n)?.FechaVersion ?? DateTime.MinValue)
                    .First();
                var ver = VerDe(elegida);
                if (ver == null) continue;
                finalNotaIds.Add(elegida.Id);
                versionOverride[elegida.Id] = ver.Id;
            }

            var sintesisId = await _sintesisService.CrearAsync(
                fechaParsed, destino, GetNombre(), finalNotaIds,
                delegacionId: null, versionOverride: versionOverride, consolidada: true);

            if (sintesisId == -2)
            {
                TempData["Error"] = $"Ya existe una síntesis consolidada {destino} para esa fecha.";
                return RedirectToAction(nameof(Consolidar), new { fecha });
            }

            // Marcar las sesiones fuente como "Consolidada" (vinculadas a la síntesis, para revertir)
            await _sesionService.MarcarConsolidadasAsync(sesionIds, sintesisId);

            // Generar el PDF de la consolidada automáticamente (queda lista para informar)
            try { await _sintesisService.GenerarPdfAsync(sintesisId); } catch { }

            await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "sintesis", "POST", "consolidar",
                new { sintesisId, fecha, destino, sesiones = sesionIds.Count, notas = finalNotaIds.Count });
            await _traza.RegistrarAsync(TrazaService.SintesisConsolidada, GetNombre(), GetRol(),
                sintesisId: sintesisId, estado: "Generada",
                detalle: $"{destino} — {fechaParsed:dd/MM/yyyy} · {finalNotaIds.Count} notas de {sesionIds.Count} sesiones");
            // Registrar el evento también a nivel de cada sesión fuente (para su traza)
            foreach (var sid in sesionIds)
                await _traza.RegistrarAsync(TrazaService.SintesisConsolidada, GetNombre(), GetRol(),
                    sesionId: sid, sintesisId: sintesisId, estado: "Consolidada", detalle: $"Consolidada en síntesis {destino}");

            TempData["Ok"] = $"Síntesis consolidada {destino} creada con {finalNotaIds.Count} notas.";
            return Redirect("/Sesion/Bandeja#consolidadas");
        }

        // ── Informar síntesis consolidada ────────────────────────────

        [HasPermission("CREAR_SINTESIS")]
        [HttpGet]
        public async Task<IActionResult> Informar(int id)
        {
            if (!PuedeConsolidar()) return Forbid();
            var sintesis = await _sintesisService.GetByIdAsync(id);
            if (sintesis == null || sintesis.DelegacionId != null) return NotFound();

            ViewData["Title"] = "Informar Síntesis Consolidada";
            ViewBag.SmtpConfigurado = await _email.EstaConfiguradoAsync();
            var delegacionesActivas = await _delegService.GetTodasAsync(soloActivas: true);
            // Delegaciones con correo configurado (campo Email del ABM de Delegaciones)
            ViewBag.DelegacionesMail = delegacionesActivas
                .Where(d => !string.IsNullOrWhiteSpace(d.Email))
                .OrderBy(d => d.Nombre)
                .ToList();
            // Todas las delegaciones activas, para elegir visibilidad in-app (independiente del mail)
            ViewBag.Delegaciones = delegacionesActivas.OrderBy(d => d.Nombre).ToList();
            ViewBag.DelegacionesSeleccionadas = sintesis.DelegacionesDestino
                .Select(d => d.DelegacionId).ToHashSet();
            return View(sintesis);
        }

        // POST: procesa los canales de información final
        [HasPermission("CREAR_SINTESIS")]
        [HttpPost]
        [ActionName("Informar")]
        public async Task<IActionResult> InformarPost(int id, bool todosRoles = false,
            List<string>? delegacionMails = null, string? mails = null,
            bool todasDelegaciones = false, List<int>? delegacionIds = null)
        {
            if (!PuedeConsolidar()) return Forbid();
            var sintesis = await _sintesisService.GetByIdAsync(id);
            if (sintesis == null || sintesis.DelegacionId != null) return NotFound();

            // Destinatarios = delegaciones tildadas + mails escritos a mano (sin duplicados)
            var listaMails = EmailService.ParseMails(mails);
            if (delegacionMails != null)
                foreach (var m in delegacionMails)
                    foreach (var unico in EmailService.ParseMails(m))
                        if (!listaMails.Any(x => string.Equals(x, unico, StringComparison.OrdinalIgnoreCase)))
                            listaMails.Add(unico);

            var hayDelegaciones = todasDelegaciones || (delegacionIds != null && delegacionIds.Count > 0);
            if (!todosRoles && listaMails.Count == 0 && !hayDelegaciones)
            {
                TempData["Error"] = "Seleccioná al menos un canal: Todos los Roles, delegaciones y/o correos.";
                return RedirectToAction(nameof(Informar), new { id });
            }

            // Envío de mails con el PDF adjunto
            if (listaMails.Count > 0)
            {
                if (string.IsNullOrEmpty(sintesis.PDFPath))
                {
                    try { await _sintesisService.GenerarPdfAsync(id); sintesis = await _sintesisService.GetByIdAsync(id); }
                    catch { }
                }
                string? pdfFisico = null;
                if (!string.IsNullOrEmpty(sintesis!.PDFPath))
                    pdfFisico = Path.Combine(_env.WebRootPath, sintesis.PDFPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

                var asunto = $"Síntesis de Prensa {sintesis.Tipo} — {sintesis.Fecha:dd/MM/yyyy}";
                var cuerpo = $"<p>Se adjunta la Síntesis de Prensa <strong>{sintesis.Tipo}</strong> del " +
                             $"{sintesis.Fecha:dd/MM/yyyy}.</p><p>División Medios O.S.INT — Superintendencia de Inteligencia Criminal.</p>";
                var (okMail, errMail) = await _email.EnviarAsync(listaMails, asunto, cuerpo,
                    pdfFisico, $"sintesis_{sintesis.Tipo.ToLower()}_{sintesis.Fecha:yyyyMMdd}.pdf");

                if (!okMail)
                {
                    await _traza.RegistrarAsync(TrazaService.SintesisInformada, GetNombre(), GetRol(),
                        sintesisId: id, estado: "Error",
                        detalle: $"Falló el envío por correo a: {string.Join("; ", listaMails)} · Error: {errMail}");
                    TempData["Error"] = $"No se pudo enviar por correo: {errMail}";
                    return RedirectToAction(nameof(Informar), new { id });
                }
            }

            await _sintesisService.InformarConCanalesAsync(id, todosRoles, string.Join("; ", listaMails),
                todasDelegaciones, delegacionIds);
            await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "sintesis", "POST", "informar",
                new { id, todosRoles, mails = listaMails.Count, todasDelegaciones, delegaciones = delegacionIds?.Count ?? 0 });

            var canales = new List<string>();
            if (todosRoles) canales.Add("Todos los Roles");
            if (todasDelegaciones) canales.Add("Todas las delegaciones");
            else if (delegacionIds is { Count: > 0 }) canales.Add($"{delegacionIds.Count} delegación(es)");
            if (listaMails.Count > 0) canales.Add($"{listaMails.Count} correo(s)");
            // Registro de los destinatarios efectivos en la traza
            var detalleTraza = $"Canales: {string.Join(" + ", canales)}"
                + (listaMails.Count > 0 ? $" · Correos: {string.Join("; ", listaMails)}" : "");
            await _traza.RegistrarAsync(TrazaService.SintesisInformada, GetNombre(), GetRol(),
                sintesisId: id, estado: "Informada", detalle: detalleTraza);
            TempData["Ok"] = $"Síntesis informada por: {string.Join(" + ", canales)}.";
            return Redirect("/Sesion/Bandeja#informadas");
        }

        // ── Revertir consolidación ────────────────────────────────────

        [HasPermission("CREAR_SINTESIS")]
        [HttpPost]
        public async Task<IActionResult> RevertirConsolidar(int id)
        {
            if (!PuedeConsolidar()) return Forbid();
            var sintesis = await _sintesisService.GetByIdAsync(id);
            if (sintesis == null || sintesis.DelegacionId != null) return NotFound();
            if (sintesis.Estado == "Informada")
            {
                TempData["Error"] = "No se puede revertir una síntesis que ya fue informada.";
                return RedirectToAction(nameof(Detalle), new { id });
            }

            // Devolver las sesiones a Finalizada y eliminar la síntesis consolidada
            await _sesionService.RevertirConsolidacionAsync(id);
            await _sintesisService.EliminarConsolidadaAsync(id);

            await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "sintesis", "POST", "revertir_consolidar",
                new { id });
            await _traza.RegistrarAsync(TrazaService.ConsolidacionRevertida, GetNombre(), GetRol(),
                sintesisId: id, estado: "Finalizada", detalle: "Las sesiones volvieron a Finalizada");
            TempData["Ok"] = "Consolidación revertida. Las sesiones volvieron a estado Finalizada.";
            return Redirect("/Sesion/Bandeja");
        }

        // ── Síntesis Publicadas (módulo visible para todos los roles) ──

        [HasPermission("VER_SINTESIS")]
        public async Task<IActionResult> Publicadas(string? tipo = null)
        {
            ViewData["Title"] = "Síntesis Publicadas";
            var esDelegacion = EsDelegacion();
            int? delegacionIdFiltro = null;
            if (esDelegacion)
            {
                var deleg = await _sesionService.GetDelegacionEfectivaAsync(User);
                delegacionIdFiltro = deleg?.Id;
            }
            var publicadas = await _sintesisService.GetPublicadasAsync(esDelegacion, delegacionIdFiltro);
            if (!string.IsNullOrEmpty(tipo))
            {
                publicadas = tipo == "Especiales"
                    ? publicadas.Where(s => s.Tipo != "Matutina" && s.Tipo != "Vespertina").ToList()
                    : publicadas.Where(s => s.Tipo == tipo).ToList();
            }
            ViewBag.FiltroTipo = tipo;
            return View(publicadas);
        }

        // ── Detalle de síntesis ───────────────────────────────────────

        [HasPermission("VER_SINTESIS")]
        public async Task<IActionResult> Detalle(int id)
        {
            var sintesis = await _sintesisService.GetByIdAsync(id);
            if (sintesis == null) return NotFound();
            if (EsDelegacion() && !await DelegacionPuedeVerAsync(sintesis)) return Forbid();

            ViewData["Title"] = $"Síntesis {sintesis.Tipo} — {sintesis.Fecha:dd/MM/yyyy}";

            if (_menuService.GetPermissions(User).Contains("CREAR_SESION"))
            {
                var borradores = await _sesionService.GetMiasAsync(GetUsuario());
                ViewBag.BorradoresPropios = borradores.Where(s => s.Estado == "Borrador").ToList();
            }

            // Para notas con versión Pendiente: obtener sesión en que fue remitida la actualización
            var notasConPendiente = sintesis.NotasIncluidas
                .Where(sn => sn.Nota.VersionActualId != sn.NotaVersionId
                          && sn.Nota.VersionActual?.EstadoRevision == "Pendiente")
                .Select(sn => sn.Nota.Id)
                .ToList();
            ViewBag.SesionPorNotaOrigen = await _sintesisService.GetSesionPorNotaOrigenAsync(notasConPendiente);

            return View(sintesis);
        }

        // ── Generar PDF ───────────────────────────────────────────────

        [HasPermission("GENERAR_PDF")]
        [HttpPost]
        public async Task<IActionResult> GenerarPdf(int id)
        {
            var sintesis = await _sintesisService.GetByIdAsync(id);
            if (sintesis == null) return NotFound();
            if (EsDelegacion() && !string.Equals(sintesis.OperadorGenera, GetUsuario(), StringComparison.OrdinalIgnoreCase)) return Forbid();
            if (sintesis.Estado == "Remitida")
            {
                TempData["Error"] = "No se puede regenerar el PDF de una síntesis ya remitida.";
                return RedirectToAction(nameof(Detalle), new { id });
            }
            try
            {
                var pdfUrl = await _sintesisService.GenerarPdfAsync(id);
                await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "sintesis", "POST", "generar_pdf",
                    new { sintesisId = id, pdfUrl });
                await _traza.RegistrarAsync(TrazaService.SintesisPdf, GetNombre(), GetRol(),
                    sintesisId: id, estado: sintesis.Estado, detalle: $"PDF {sintesis.Tipo} — {sintesis.Fecha:dd/MM/yyyy}");
                TempData["Ok"] = "PDF generado correctamente";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error al generar PDF: {ex.Message}";
            }

            return RedirectToAction(nameof(Detalle), new { id });
        }

        // ── Remitir síntesis ──────────────────────────────────────────

        [HasPermission("MODIFICAR_SINTESIS")]
        [HttpPost]
        public async Task<IActionResult> Remitir(int id)
        {
            var sintesis = await _sintesisService.GetByIdAsync(id);
            if (sintesis == null) return NotFound();
            if (EsDelegacion() && !string.Equals(sintesis.OperadorGenera, GetUsuario(), StringComparison.OrdinalIgnoreCase)) return Forbid();
            if (sintesis.Estado != "Generada")
            {
                TempData["Error"] = sintesis.Estado == "Remitida"
                    ? "La síntesis ya fue remitida."
                    : "Primero generá el PDF antes de remitir.";
                return RedirectToAction(nameof(Detalle), new { id });
            }
            var ok = await _sintesisService.RemitirAsync(id);
            if (ok)
            {
                await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "sintesis", "POST", "remitir",
                    new { sintesisId = id });
                await _traza.RegistrarAsync(TrazaService.SintesisRemitida, GetNombre(), GetRol(),
                    sintesisId: id, estado: "Remitida", detalle: $"{sintesis.Tipo} — {sintesis.Fecha:dd/MM/yyyy}");
                TempData["Ok"] = "Síntesis remitida correctamente.";
            }
            else
            {
                TempData["Error"] = "No se puede remitir: primero generá el PDF de la síntesis.";
            }
            return RedirectToAction(nameof(Detalle), new { id });
        }

        // ── Convertir síntesis a Borrador ─────────────────────────────

        [HasPermission("MODIFICAR_SINTESIS")]
        [HttpPost]
        public async Task<IActionResult> ConvertirABorrador(int id)
        {
            var sintesisCheck = await _sintesisService.GetByIdAsync(id);
            if (sintesisCheck == null) return NotFound();
            if (EsDelegacion() && !string.Equals(sintesisCheck.OperadorGenera, GetUsuario(), StringComparison.OrdinalIgnoreCase)) return Forbid();

            // La sesión nueva hereda la misma DelegacionId que ya tenía la síntesis (sea de una
            // Delegación, de MEDIOS ["División Medios"] o null en la consolidada real) — no hay
            // que re-derivarla del rol del usuario actual, que puede no coincidir con el dueño
            // original de la síntesis.
            var sesionId = await _sintesisService.ConvertirABorradorAsync(id, GetUsuario(), sintesisCheck.DelegacionId);
            if (sesionId > 0)
            {
                await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "sintesis", "POST", "convertir_borrador",
                    new { sintesisId = id, nuevaSesionId = sesionId });
                TempData["Ok"] = "Síntesis convertida a borrador de sesión.";
                return RedirectToAction("Detalle", "Sesion", new { id = sesionId });
            }
            else
            {
                TempData["Error"] = "No se puede modificar una síntesis ya remitida.";
                return RedirectToAction(nameof(Listado));
            }
        }

        // ── Descargar PDF ─────────────────────────────────────────────

        [HasPermission("VER_SINTESIS")]
        public async Task<IActionResult> DescargarPdf(int id)
        {
            var sintesis = await _sintesisService.GetByIdAsync(id);
            if (sintesis == null) return NotFound();
            if (EsDelegacion() && !await DelegacionPuedeVerAsync(sintesis)) return Forbid();
            if (sintesis.PDFPath == null) return NotFound();

            var filePath = Path.Combine(_env.WebRootPath, sintesis.PDFPath.TrimStart('/'));
            if (!System.IO.File.Exists(filePath)) return NotFound();

            var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
            var nombreDescarga = $"sintesis_{sintesis.Tipo}_{sintesis.Fecha:ddMMyyyy}.pdf";
            return File(bytes, "application/pdf", nombreDescarga);
        }

        private IWebHostEnvironment _env =>
            HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
    }
}
