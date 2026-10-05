using Microsoft.AspNetCore.Mvc;
using Medios.Security;
using Medios.Services;

namespace Medios.Controllers
{
    public class NotaController : Controller
    {
        private readonly NotaService _notaService;
        private readonly SesionService _sesionService;
        private readonly AuditoriaService _auditoria;
        private readonly IAService _ia;
        private readonly ConfiguracionService _config;
        private readonly ScraperService _scraper;
        private readonly TrazaService _traza;
        private readonly ImagenService _imagen;

        public NotaController(NotaService notaService, SesionService sesionService,
            AuditoriaService auditoria, IAService ia, ConfiguracionService config,
            ScraperService scraper, TrazaService traza,
            ImagenService imagen)
        {
            _notaService = notaService;
            _sesionService = sesionService;
            _auditoria = auditoria;
            _ia = ia;
            _config = config;
            _scraper = scraper;
            _traza = traza;
            _imagen = imagen;
        }

        private string GetRol() => User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
        private string GetNombre() => User.Identity?.Name ?? "";
        private string GetIp() =>
            Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString()
            ?? "";

        private string GetUsuario() => User.FindFirst("Usuario")?.Value ?? User.Identity?.Name ?? "";

        // ── Notas libres (sin sesión) ──────────────────────────────────

        [HasPermission("VER_NOTAS_LIBRES")]
        [HttpGet]
        public async Task<IActionResult> Listado()
        {
            ViewData["Title"] = "Notas";
            ViewBag.Categorias = await _notaService.GetCategoriasAsync();

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

            ViewBag.Partidos  = partidos;
            ViewBag.Caratulas = await _notaService.GetCaratulasAsync();
            var notas = await _notaService.GetNotasLibresAsync(GetNombre());

            if (User.IsInRole("DELEGACION"))
            {
                var deleg = await _sesionService.GetDelegacionEfectivaAsync(User);
                ViewBag.NotasDescartadas = deleg != null
                    ? await _notaService.GetDescartadasSinVincularAsync(deleg.Id)
                    : new List<Medios.Entities.NotaPrensa>();
            }

            return View(notas);
        }

        [HasPermission("VER_NOTAS_LIBRES")]
        [HttpPost]
        public async Task<IActionResult> AgregarLibre(
            int? categoriaId, int? partidoId, int? localidadId,
            string titulo, string texto, string? fuente, string? link,
            bool esRepercusion = false,
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
                return RedirectToAction(nameof(Listado));
            }

            int? delegId = null;
            var esMedios = User.IsInRole("MEDIOS");
            if (esMedios)
                delegId = (await _sesionService.GetDelegacionEfectivaAsync(User))?.Id;
            else if (int.TryParse(User.FindFirst("DelegacionId")?.Value, out var dId))
                delegId = dId;

            var imagenLocal = await _imagen.ProcesarDesdeUrlAsync(imagenUrl);
            var extrasJson = await _imagen.ProcesarVariasJsonAsync(imagenesExtra);
            DateTime? fn = DateTime.TryParse(fechaNoticia, out var fnParsed) ? fnParsed : null;
            var nuevaNotaId = await _notaService.AgregarLibreAsync(
                categoriaId, partidoId, localidadId, titulo, texto,
                fuente, link, esRepercusion, GetNombre(),
                direccion, latitud, longitud, delegId, sintesis, fn,
                caratulaQuironId, modalidadQuironId, ambitoNota, imagenLocal, videoUrl, extrasJson, otrosMedios,
                aprobadaDirectamente: esMedios);

            await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "notas_prensa", "POST", "agregar_libre",
                new { categoriaId, titulo });
            await _traza.RegistrarAsync(TrazaService.NotaCreada, GetNombre(), GetRol(),
                notaId: nuevaNotaId, version: 1, estado: esMedios ? "Aprobada" : "Sin Remitir", detalle: "Nota libre");

            TempData["Ok"] = "Nota agregada";
            return RedirectToAction(nameof(Listado));
        }

        [HasPermission("VER_NOTAS_LIBRES")]
        [HttpPost]
        public async Task<IActionResult> EliminarLibre(int notaId)
        {
            var ok = await _notaService.EliminarLibreAsync(notaId, GetNombre());
            if (!ok)
                TempData["Error"] = "No se pudo eliminar la nota";
            else
            {
                await _traza.RegistrarAsync(TrazaService.NotaEliminada, GetNombre(), GetRol(),
                    notaId: notaId, estado: "Eliminada");
                TempData["Ok"] = "Nota eliminada";
            }

            return RedirectToAction(nameof(Listado));
        }

        [HasPermission("VER_NOTAS_LIBRES")]
        [HttpGet]
        public async Task<IActionResult> EditarLibre(int id)
        {
            var nota = await _notaService.GetDetalleAsync(id);
            if (nota == null || nota.SesionPrensaId != null) return NotFound();

            if (!await PuedeEditarNotaAsync(nota)) return Forbid();

            ViewData["Title"] = "Editar Nota";
            ViewBag.Categorias           = await _notaService.GetCategoriasAsync();
            ViewBag.Caratulas            = await _notaService.GetCaratulasAsync();
            ViewBag.CurrentCaratulaId    = nota.VersionActual?.CaratulaQuironId;
            ViewBag.CurrentModalidadId   = nota.VersionActual?.ModalidadQuironId;
            ViewBag.Partidos = await _notaService.GetPartidosAsync();
            return View(nota);
        }

        private async Task<bool> PuedeEditarNotaAsync(Medios.Entities.NotaPrensa nota)
        {
            // El creador siempre puede editar
            if (nota.VersionActual?.Usuario == GetNombre()) return true;

            // DELEGACION: puede editar si la nota pertenece a su propia delegación
            if (User.IsInRole("DELEGACION"))
            {
                var userDeleg = await _sesionService.GetDelegacionEfectivaAsync(User);
                if (userDeleg == null) return false;

                // Nota libre: verificar via nota.DelegacionId
                if (nota.DelegacionId.HasValue)
                    return nota.DelegacionId == userDeleg.Id;
                // Nota en sesión: verificar via sesion.DelegacionId
                if (nota.Sesion?.DelegacionId.HasValue == true)
                    return nota.Sesion.DelegacionId == userDeleg.Id;
            }
            return false;
        }

        [HasPermission("VER_NOTAS_LIBRES")]
        [HttpPost]
        public async Task<IActionResult> EditarLibre(
            int notaId,
            int? categoriaId, int? partidoId, int? localidadId,
            string titulo, string texto, string? fuente, string? link,
            bool esRepercusion = false,
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
                return RedirectToAction(nameof(EditarLibre), new { id = notaId });
            }

            int? userDelegacionId = null;
            if (User.IsInRole("DELEGACION"))
            {
                var ud = await _sesionService.GetDelegacionEfectivaAsync(User);
                userDelegacionId = ud?.Id;
            }

            var imagenLocal = await _imagen.ProcesarDesdeUrlAsync(imagenUrl);
            var extrasJson = await _imagen.ProcesarVariasJsonAsync(imagenesExtra);
            DateTime? fnEl = DateTime.TryParse(fechaNoticia, out var fnElP) ? fnElP : null;
            var ok = await _notaService.EditarLibreAsync(
                notaId, GetNombre(), categoriaId, partidoId, localidadId,
                titulo, texto, fuente, link, esRepercusion,
                direccion, latitud, longitud, sintesis, userDelegacionId, fechaNoticia: fnEl,
                caratulaQuironId: caratulaQuironId, modalidadQuironId: modalidadQuironId,
                ambitoNota: ambitoNota, imagenUrl: imagenLocal, videoUrl: videoUrl,
                imagenesExtraJson: extrasJson, otrosMedios: otrosMedios);

            if (!ok)
            {
                TempData["Error"] = "No se pudo editar la nota";
                return RedirectToAction(nameof(Listado));
            }

            await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "notas_prensa", "POST", "editar_libre",
                new { notaId });
            await _traza.RegistrarAsync(TrazaService.NotaModificada, GetNombre(), GetRol(),
                notaId: notaId, detalle: "Edición de nota libre");

            TempData["Ok"] = "Nota actualizada";
            return RedirectToAction(nameof(Listado));
        }

        // ── Aprobar directamente ──────────────────────────────────────

        [HasPermission("APROBAR_NOTA")]
        [HttpPost]
        public async Task<IActionResult> Aprobar(int notaId, int sesionId)
        {
            await _notaService.AprobarAsync(notaId, GetNombre());
            await _sesionService.ActualizarEstadoSiCompletaAsync(sesionId);
            await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "notas_prensa", "POST", "aprobar",
                new { notaId, sesionId });
            await _traza.RegistrarAsync(TrazaService.NotaAprobada, GetNombre(), GetRol(),
                notaId: notaId, sesionId: sesionId, estado: "Aprobada");
            TempData["Ok"] = "Nota aprobada";
            return RedirectToAction("Detalle", "Sesion", new { id = sesionId });
        }

        // ── Descartar ─────────────────────────────────────────────────

        [HasPermission("DESCARTAR_NOTA")]
        [HttpPost]
        public async Task<IActionResult> Descartar(int notaId, int sesionId, string? motivo)
        {
            await _notaService.DescartarAsync(notaId, GetNombre(), motivo);
            await _sesionService.ActualizarEstadoSiCompletaAsync(sesionId);
            await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "notas_prensa", "POST", "descartar",
                new { notaId, sesionId, motivo });
            await _traza.RegistrarAsync(TrazaService.NotaDescartada, GetNombre(), GetRol(),
                notaId: notaId, sesionId: sesionId, estado: "Descartada",
                detalle: string.IsNullOrWhiteSpace(motivo) ? null : $"Motivo: {motivo}");
            TempData["Ok"] = "Nota descartada";
            return RedirectToAction("Detalle", "Sesion", new { id = sesionId });
        }

        // ── Modificar y aprobar: GET ──────────────────────────────────

        [HasPermission("MODIFICAR_NOTA")]
        [HttpGet]
        public async Task<IActionResult> Editar(int id, int sesionId)
        {
            var sesion = await _sesionService.GetByIdAsync(sesionId);
            if (sesion == null) return NotFound();

            var nota = sesion.Notas.FirstOrDefault(n => n.Id == id);
            if (nota == null) return NotFound();

            ViewData["Title"] = "Editar Nota";
            ViewBag.Categorias           = await _notaService.GetCategoriasAsync();
            ViewBag.Caratulas            = await _notaService.GetCaratulasAsync();
            ViewBag.CurrentCaratulaId    = nota.VersionActual?.CaratulaQuironId;
            ViewBag.CurrentModalidadId   = nota.VersionActual?.ModalidadQuironId;
            ViewBag.Partidos = await _notaService.GetPartidosAsync();
            ViewBag.SesionId = sesionId;
            return View(nota);
        }

        // ── Modificar y aprobar: POST ─────────────────────────────────

        [HasPermission("MODIFICAR_NOTA")]
        [HttpPost]
        public async Task<IActionResult> Editar(
            int notaId, int sesionId,
            int? categoriaId, int? partidoId, int? localidadId,
            string titulo, string texto, string? fuente, string? link,
            bool esRepercusion = false,
            string? direccion = null, double? latitud = null, double? longitud = null,
            string? sintesis = null, string? fechaNoticia = null,
            int? caratulaQuironId = null, int? modalidadQuironId = null,
            string? ambitoNota = null, string? imagenUrl = null, string? videoUrl = null,
            List<string>? imagenesExtra = null, string? otrosMedios = null)
        {
            if (string.IsNullOrWhiteSpace(titulo) || string.IsNullOrWhiteSpace(texto))
            {
                TempData["Error"] = "Título y texto son obligatorios";
                return RedirectToAction(nameof(Editar), new { id = notaId, sesionId });
            }

            var imagenLocal = await _imagen.ProcesarDesdeUrlAsync(imagenUrl);
            var extrasJson = await _imagen.ProcesarVariasJsonAsync(imagenesExtra);
            DateTime? fn = DateTime.TryParse(fechaNoticia, out var fnP) ? fnP : null;
            await _notaService.ModificarYAprobarAsync(
                notaId, GetNombre(), categoriaId, partidoId, localidadId,
                titulo, texto, fuente, link, esRepercusion,
                direccion, latitud, longitud, sintesis, fn,
                caratulaQuironId, modalidadQuironId, ambitoNota, imagenLocal, videoUrl, extrasJson, otrosMedios);

            // Operador o superior: la síntesis toma la última versión como referencia
            if (!User.IsInRole("DELEGACION"))
                await _notaService.ActualizarReferenciaSintesisAsync(notaId);

            await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "notas_prensa", "POST", "modificar",
                new { notaId, sesionId });
            await _traza.RegistrarAsync(TrazaService.NotaModificada, GetNombre(), GetRol(),
                notaId: notaId, sesionId: sesionId, detalle: "Nota modificada (nueva versión)");

            TempData["Ok"] = "Nota modificada. Revisá la última versión y aprobala si corresponde.";
            return RedirectToAction("Detalle", "Sesion", new { id = sesionId });
        }

        // ── Editar versión Sin Remitir (en-place): GET ───────────────

        [HasPermission("AMPLIAR_NOTA")]
        [HttpGet]
        public async Task<IActionResult> EditarSinRemitir(int id, string? returnUrl = null)
        {
            var nota = await _notaService.GetDetalleAsync(id);
            if (nota == null) return NotFound();

            if (nota.VersionActual?.EstadoRevision is not ("Sin Remitir" or "Borrador"))
                return Redirect(returnUrl ?? $"/NotaBuscador/Detalle/{id}");

            ViewData["Title"] = "Modificar versión sin informar";
            ViewBag.Categorias           = await _notaService.GetCategoriasAsync();
            ViewBag.Caratulas            = await _notaService.GetCaratulasAsync();
            ViewBag.CurrentCaratulaId    = nota.VersionActual?.CaratulaQuironId;
            ViewBag.CurrentModalidadId   = nota.VersionActual?.ModalidadQuironId;
            ViewBag.Partidos = await _notaService.GetPartidosAsync();
            ViewBag.ReturnUrl = returnUrl;
            return View(nota);
        }

        // ── Editar versión Sin Remitir (en-place): POST ──────────────

        [HasPermission("AMPLIAR_NOTA")]
        [HttpPost]
        public async Task<IActionResult> EditarSinRemitir(
            int notaId,
            int? categoriaId, int? partidoId, int? localidadId,
            string titulo, string texto, string? fuente, string? link,
            bool esRepercusion, string resumenCambio,
            string? direccion = null, double? latitud = null, double? longitud = null,
            string? sintesis = null, string? fechaNoticia = null,
            string? returnUrl = null,
            int? caratulaQuironId = null, int? modalidadQuironId = null,
            string? ambitoNota = null, string? imagenUrl = null, string? videoUrl = null,
            List<string>? imagenesExtra = null, string? otrosMedios = null)
        {
            if (string.IsNullOrWhiteSpace(titulo) || string.IsNullOrWhiteSpace(texto)
                || string.IsNullOrWhiteSpace(fuente))
            {
                TempData["Error"] = "Título, texto y fuente son obligatorios";
                return RedirectToAction(nameof(EditarSinRemitir), new { id = notaId, returnUrl });
            }

            if (string.IsNullOrWhiteSpace(resumenCambio))
            {
                TempData["Error"] = "Describí qué se cambió (Resumen del cambio)";
                return RedirectToAction(nameof(EditarSinRemitir), new { id = notaId, returnUrl });
            }

            var imagenLocal = await _imagen.ProcesarDesdeUrlAsync(imagenUrl);
            var extrasJson = await _imagen.ProcesarVariasJsonAsync(imagenesExtra);
            DateTime? fn = DateTime.TryParse(fechaNoticia, out var fnP) ? fnP : null;
            var ok = await _notaService.EditarVersionSinRemitirAsync(
                notaId, GetNombre(), categoriaId, partidoId, localidadId,
                titulo, texto, fuente, link, esRepercusion, resumenCambio,
                direccion, latitud, longitud, sintesis, fn,
                caratulaQuironId, modalidadQuironId, ambitoNota, imagenLocal, videoUrl, extrasJson, otrosMedios);

            if (!ok)
            {
                TempData["Error"] = "No se pudo modificar: la versión ya no está en estado Sin Remitir";
                return Redirect(returnUrl ?? $"/NotaBuscador/Detalle/{notaId}");
            }

            await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "notas_prensa", "POST", "editar_sin_remitir",
                new { notaId });
            await _traza.RegistrarAsync(TrazaService.NotaModificada, GetNombre(), GetRol(),
                notaId: notaId, estado: "Sin Remitir", detalle: "Modificación de versión sin remitir");

            TempData["Ok"] = "Versión actualizada";
            return Redirect(returnUrl ?? $"/NotaBuscador/Detalle/{notaId}");
        }

        // ── Ampliar nota: GET ─────────────────────────────────────────

        [HasPermission("AMPLIAR_NOTA")]
        [HttpGet]
        public async Task<IActionResult> Ampliar(int id, int sesionId = 0, string? returnUrl = null)
        {
            // Cuando viene del buscador no hay sesionId; cargamos la nota directamente
            Medios.Entities.NotaPrensa? nota;
            if (sesionId > 0)
            {
                var sesion = await _sesionService.GetByIdAsync(sesionId);
                if (sesion == null) return NotFound();
                nota = sesion.Notas.FirstOrDefault(n => n.Id == id);
            }
            else
            {
                nota = await _notaService.GetDetalleAsync(id);
            }

            if (nota == null) return NotFound();

            if (nota.VersionActual?.EstadoRevision == "Sin Remitir")
            {
                TempData["Error"] = "Esta nota ya tiene una versión sin informar. Primero remitila en un borrador antes de agregar nueva información.";
                return Redirect(returnUrl ?? $"/NotaBuscador/Detalle/{id}");
            }

            ViewData["Title"] = "Ampliar Nota";
            ViewBag.Categorias           = await _notaService.GetCategoriasAsync();
            ViewBag.Caratulas            = await _notaService.GetCaratulasAsync();
            ViewBag.CurrentCaratulaId    = nota.VersionActual?.CaratulaQuironId;
            ViewBag.CurrentModalidadId   = nota.VersionActual?.ModalidadQuironId;
            ViewBag.Partidos = await _notaService.GetPartidosAsync();
            ViewBag.SesionId = sesionId;
            ViewBag.ReturnUrl = returnUrl;
            return View(nota);
        }

        // ── Ampliar nota: POST ────────────────────────────────────────

        [HasPermission("AMPLIAR_NOTA")]
        [HttpPost]
        public async Task<IActionResult> Ampliar(
            int notaId, int sesionId,
            int? categoriaId, int? partidoId, int? localidadId,
            string titulo, string texto, string? fuente, string? link,
            bool esRepercusion, string resumenCambio,
            string? direccion = null, double? latitud = null, double? longitud = null,
            string? sintesis = null, string? fechaNoticia = null,
            string? returnUrl = null,
            int? caratulaQuironId = null, int? modalidadQuironId = null,
            string? ambitoNota = null, string? imagenUrl = null, string? videoUrl = null,
            List<string>? imagenesExtra = null, string? otrosMedios = null)
        {
            if (string.IsNullOrWhiteSpace(titulo) || string.IsNullOrWhiteSpace(texto))
            {
                TempData["Error"] = "Título y texto son obligatorios";
                return RedirectToAction(nameof(Ampliar), new { id = notaId, sesionId, returnUrl });
            }

            if (string.IsNullOrWhiteSpace(resumenCambio))
            {
                TempData["Error"] = "Describí qué información se agrega (Resumen del cambio)";
                return RedirectToAction(nameof(Ampliar), new { id = notaId, sesionId, returnUrl });
            }

            var imagenLocal = await _imagen.ProcesarDesdeUrlAsync(imagenUrl);
            var extrasJson = await _imagen.ProcesarVariasJsonAsync(imagenesExtra);
            DateTime? fn = DateTime.TryParse(fechaNoticia, out var fnP) ? fnP : null;
            await _notaService.AmpliarAsync(
                notaId, GetNombre(), categoriaId, partidoId, localidadId,
                titulo, texto, fuente, link, esRepercusion, resumenCambio,
                direccion, latitud, longitud, sintesis, fn,
                caratulaQuironId, modalidadQuironId, ambitoNota, imagenLocal, videoUrl, extrasJson, otrosMedios);

            // Operador o superior: la síntesis toma la última versión como referencia
            if (!User.IsInRole("DELEGACION"))
                await _notaService.ActualizarReferenciaSintesisAsync(notaId);

            await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "notas_prensa", "POST", "ampliar",
                new { notaId, sesionId, resumenCambio });
            await _traza.RegistrarAsync(TrazaService.NotaAmpliada, GetNombre(), GetRol(),
                notaId: notaId, sesionId: sesionId == 0 ? null : sesionId, estado: "Sin Remitir",
                detalle: resumenCambio);

            TempData["Ok"] = "Nota ampliada correctamente";

            if (!string.IsNullOrEmpty(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction("Detalle", "Sesion", new { id = sesionId });
        }

        // ── Editar nota en borrador (delegación): GET ────────────────

        [HasPermission("CREAR_SESION")]
        [HttpGet]
        public async Task<IActionResult> EditarBorrador(int id, int sesionId)
        {
            var sesion = await _sesionService.GetByIdAsync(sesionId);
            if (sesion == null || sesion.Estado != "Borrador") return NotFound();

            // Verificar que el usuario puede editar notas de esta sesión
            if (User.IsInRole("DELEGACION"))
            {
                var userDeleg = await _sesionService.GetDelegacionEfectivaAsync(User);
                // Comparar contra nombre Y usuario (sesiones viejas pueden tener cualquiera)
                bool esPropietario = string.Equals(sesion.UsuarioCarga, GetNombre(), StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(sesion.UsuarioCarga, GetUsuario(), StringComparison.OrdinalIgnoreCase);
                bool mismaDelegacion = userDeleg != null && sesion.DelegacionId == userDeleg.Id;
                if (!esPropietario && !mismaDelegacion) return Forbid();
            }

            var nota = sesion.Notas.FirstOrDefault(n => n.Id == id);
            if (nota == null) return NotFound();

            ViewData["Title"] = "Editar Nota";
            ViewBag.Categorias           = await _notaService.GetCategoriasAsync();
            ViewBag.Caratulas            = await _notaService.GetCaratulasAsync();
            ViewBag.CurrentCaratulaId    = nota.VersionActual?.CaratulaQuironId;
            ViewBag.CurrentModalidadId   = nota.VersionActual?.ModalidadQuironId;
            ViewBag.Partidos = await _notaService.GetPartidosAsync();
            ViewBag.SesionId = sesionId;
            return View(nota);
        }

        // ── Editar nota en borrador (delegación): POST ───────────────

        [HasPermission("CREAR_SESION")]
        [HttpPost]
        public async Task<IActionResult> EditarBorrador(
            int notaId, int sesionId,
            int? categoriaId, int? partidoId, int? localidadId,
            string titulo, string texto, string? fuente, string? link,
            bool esRepercusion = false,
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
                return RedirectToAction(nameof(EditarBorrador), new { id = notaId, sesionId });
            }

            int? delegIdEB = null;
            if (User.IsInRole("DELEGACION"))
            {
                var ud = await _sesionService.GetDelegacionEfectivaAsync(User);
                delegIdEB = ud?.Id;
            }

            var imagenLocal = await _imagen.ProcesarDesdeUrlAsync(imagenUrl);
            var extrasJson = await _imagen.ProcesarVariasJsonAsync(imagenesExtra);
            DateTime? fnEB = DateTime.TryParse(fechaNoticia, out var fnEBP) ? fnEBP : null;
            var ok = await _notaService.EditarBorradorAsync(
                notaId, GetNombre(), categoriaId, partidoId, localidadId,
                titulo, texto, fuente, link, esRepercusion,
                direccion, latitud, longitud, sintesis,
                userDelegacionId: delegIdEB, usuarioLogin: GetUsuario(),
                fechaNoticia: fnEB,
                caratulaQuironId: caratulaQuironId, modalidadQuironId: modalidadQuironId,
                ambitoNota: ambitoNota, imagenUrl: imagenLocal, videoUrl: videoUrl,
                imagenesExtraJson: extrasJson, otrosMedios: otrosMedios);

            if (!ok)
            {
                TempData["Error"] = "No se pudo editar la nota (sesión no está en borrador o no sos el propietario)";
                return RedirectToAction("Detalle", "Sesion", new { id = sesionId });
            }

            await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "notas_prensa", "POST", "editar_borrador",
                new { notaId, sesionId });

            TempData["Ok"] = "Nota actualizada";
            return RedirectToAction("Detalle", "Sesion", new { id = sesionId });
        }

        // ── Carga directa División Medios: GET ────────────────────────

        [HasPermission("CREAR_NOTA_DIVISION")]
        [HttpGet]
        public IActionResult Nueva()
        {
            return RedirectToAction(nameof(Listado));
        }

        // ── Carga directa División Medios: POST ───────────────────────

        [HasPermission("CREAR_NOTA_DIVISION")]
        [HttpPost]
        public async Task<IActionResult> Nueva(
            int? categoriaId, int? partidoId, int? localidadId,
            string titulo, string texto, string? fuente, string? link,
            bool esRepercusion, string fecha, string turno,
            string? direccion = null, double? latitud = null, double? longitud = null,
            string? sintesis = null, string? fechaNoticia = null,
            int? caratulaQuironId = null, int? modalidadQuironId = null,
            string? ambitoNota = null, string? imagenUrl = null, string? videoUrl = null,
            List<string>? imagenesExtra = null, string? otrosMedios = null)
        {
            // Formularios abiertos antes del cambio también guardan una nota libre.
            return await AgregarLibre(categoriaId, partidoId, localidadId, titulo, texto, fuente, link,
                esRepercusion, direccion, latitud, longitud, sintesis, fechaNoticia,
                caratulaQuironId, modalidadQuironId, ambitoNota, imagenUrl, videoUrl, imagenesExtra, otrosMedios);
        }

        // ── Sugerencia de carátula/modalidad con IA ───────────────────

        [HasPermission("USAR_IA")]
        [HttpPost]
        public async Task<IActionResult> SugerirCaratula([FromBody] SugerirCaratulaRequest req)
        {
            if (string.IsNullOrWhiteSpace(req?.Texto))
                return BadRequest(new { error = "Texto vacío" });

            var caratulas = await _notaService.GetCaratulasConModalidadesAsync();

            var lista = caratulas.Select(c => new
            {
                id = c.Id,
                nombre = c.Nombre,
                modalidades = c.Modalidades.OrderBy(m => m.Nombre).Select(m => new { id = m.Id, nombre = m.Nombre })
            });
            var caratulasJson = System.Text.Json.JsonSerializer.Serialize(lista);

            var (caratulaId, modalidadId, error) = await _ia.SugerirCaratulaAsync(req.Texto, caratulasJson);
            if (error != null)
                return StatusCode(503, new { error });

            return Ok(new { caratulaId, modalidadId });
        }

        // ── Generación de síntesis IA (AJAX) ──────────────────────────

        [HasPermission("USAR_IA")]
        [HttpPost]
        public async Task<IActionResult> GenerarSintesisIA([FromBody] GenerarSintesisRequest req)
        {
            if (string.IsNullOrWhiteSpace(req?.Texto))
                return BadRequest(new { error = "Texto vacío" });

            var prompt = await _config.GetAsync("ia_prompt") ?? "";

            if (string.IsNullOrWhiteSpace(prompt))
                return BadRequest(new { error = "El prompt de IA no está configurado. Contactá al administrador." });

            var (resultado, error) = await _ia.GenerarSintesisAsync(req.Texto, prompt);

            if (error != null)
                return StatusCode(503, new { error });

            return Ok(new { sintesis = resultado });
        }

        // ── Scraping de URL (AJAX) ────────────────────────────────────

        [HasPermission("CREAR_SESION", "CREAR_NOTA_DIVISION", "ANALIZAR_SESION")]
        [HttpPost]
        public async Task<IActionResult> ScrapearUrl([FromBody] ScrapearUrlRequest req)
        {
            if (string.IsNullOrWhiteSpace(req?.Url))
                return BadRequest(new { error = "URL vacía" });

            var (titulo, texto, direccion, fuente, fechaNoticia, imagenUrl, videoUrl, error) = await _scraper.ScrapearAsync(req.Url);

            if (error != null)
                return StatusCode(503, new { error });

            string? fechaNoticiaStr = fechaNoticia?.ToString("yyyy-MM-ddTHH:mm");
            return Ok(new { titulo, texto, direccion, fuente, fechaNoticia = fechaNoticiaStr, imagenUrl, videoUrl });
        }
    }

    public class GenerarSintesisRequest
    {
        public string? Texto { get; set; }
    }

    public class SugerirCaratulaRequest
    {
        public string? Texto { get; set; }
    }

    public class ScrapearUrlRequest
    {
        public string? Url { get; set; }
    }
}
