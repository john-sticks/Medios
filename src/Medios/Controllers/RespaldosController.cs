using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Medios.Security;
using Medios.Services.Respaldos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;

namespace Medios.Controllers;

[Authorize(Roles = "DESARROLLADOR")]
[HasPermission("ADMINISTRAR_RESPALDOS_TEST")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class RespaldosController(RespaldoStore store, DatosTestContext datos, AccesoEmergenciaService acceso,
    IDataProtectionProvider protection, IConfiguration config) : Controller
{
    bool Disponible() => store.Habilitado && User.FindFirst("RolReal")?.Value == "DESARROLLADOR";
    string Actor => User.FindFirst("UsuarioReal")?.Value ?? User.Identity?.Name ?? "";
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (!Disponible()) return NotFound(); ViewData["Title"] = "Respaldos y contingencia";
        var state = await store.LeerAsync();
        state.MantenimientoDelegaciones = (await HttpContext.RequestServices.GetRequiredService<ModoOperativoStore>().LeerAsync()).Mantenimiento;
        var profile = Request.Cookies.ContainsKey(RespaldoContextMiddleware.CookiePerfil) ? "Copia seleccionada para esta sesión" : "Test actual";
        var model = new RespaldoVista { Estado = state, BaseActual = profile, AgenteConfigurado = await store.SecretosAsync() != null };
        try { model.Usuarios = await acceso.UsuariosAsync(); } catch { model.Aviso = "No se pudo leer la copia. Revisá la restauración y su conexión."; }
        return View(model);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Programar(ProgramacionRespaldo form)
    {
        if (!Disponible()) return NotFound(); if (!ModelState.IsValid) { TempData["Error"] = "Ingresá una hora válida y una retención entre 2 y 90 días."; return RedirectToAction(nameof(Index)); }
        await store.Cambiar(s => { s.Programado = form.Programado; s.Hora = form.Hora; s.RetencionDias = form.RetencionDias; RespaldoStore.Evento(s, Actor, "Programación de respaldo actualizada"); return true; });
        TempData["Ok"] = "Programación guardada. El agente productivo debe estar instalado y conectado."; return RedirectToAction(nameof(Index));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Solicitar()
    {
        if (!Disponible()) return NotFound();
        await store.Cambiar(s => { s.SolicitudPendiente ??= Guid.NewGuid().ToString("N"); s.SolicitadaUtc = DateTimeOffset.UtcNow; RespaldoStore.Evento(s, Actor, "Respaldo solicitado"); return true; });
        TempData["Ok"] = "Solicitud registrada. El agente la recogerá en su próxima conexión."; return RedirectToAction(nameof(Index));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Agente()
    {
        if (!Disponible()) return NotFound();
        await store.Cambiar(s => { RespaldoStore.Evento(s, Actor, "Instalación privada de agente descargada"); return true; });
        return File(await AgenteInstalador.CrearAsync(store, config), "application/zip", "keryx-agente-privado.zip");
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PrepararInstalacion()
    {
        if (!Disponible()) return NotFound();
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await store.Cambiar(s =>
        {
            s.InstalacionHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code)));
            s.InstalacionHastaUtc = DateTimeOffset.UtcNow.AddMinutes(10);
            s.SolicitudPendiente ??= Guid.NewGuid().ToString("N"); s.SolicitadaUtc = DateTimeOffset.UtcNow;
            RespaldoStore.Evento(s, Actor, "Instalación preparada: código de un uso, diez minutos"); return true;
        });
        TempData["CodigoInstalacion"] = code;
        return RedirectToAction(nameof(Index));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReabrirCopia(bool confirmar)
    {
        if (!Disponible()) return NotFound();
        if (!confirmar) return BadRequest();
        var use = HttpContext.RequestServices.GetRequiredService<CopiaUso>();
        await use.Operacion.WaitAsync(HttpContext.RequestAborted);
        try
        {
        await store.Cambiar(s =>
        {
            if (s.Restaurando || s.RestauracionPendiente != null) throw new InvalidOperationException("La copia se está restaurando.");
            var package = Path.Combine(store.Directorio, "retorno-produccion.kbk");
            if (System.IO.File.Exists(package)) System.IO.File.Delete(package);
            s.RetornoSoloLectura = false;
            RespaldoStore.Evento(s, Actor, "Escritura de copia reabierta. El paquete de retorno anterior queda obsoleto."); return true;
        });
        }
        finally { use.Operacion.Release(); }
        TempData["Ok"] = "Copia reabierta. Descartá cualquier paquete descargado antes de esta reapertura y generá uno nuevo al terminar.";
        return RedirectToAction(nameof(Index), new { seccion = "contingencia" });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Restaurar(string id, bool confirmar)
    {
        if (!Disponible()) return NotFound();
        try
        {
            if (!confirmar) throw new InvalidOperationException("Confirmá que se reemplazarán únicamente la base y los archivos de la copia.");
            await store.Cambiar(s =>
            {
                if (s.ContingenciaActiva || s.Restaurando || s.RestauracionPendiente != null) throw new InvalidOperationException("Desactivá la contingencia o esperá la restauración actual.");
                if (!s.Archivos.Any(a => a.Id == id)) throw new InvalidOperationException("Respaldo no encontrado.");
                _ = store.Archivo(id); s.RestauracionPendiente = id; RespaldoStore.Evento(s, Actor, "Restauración solicitada: " + id); return true;
            });
            TempData["Ok"] = "Restauración solicitada. Actualizá la vista para ver el resultado.";
        }
        catch (InvalidOperationException e) { TempData["Error"] = e.Message; }
        return RedirectToAction(nameof(Index));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Seleccionar(string baseDatos)
    {
        if (!Disponible()) return NotFound();
        if (baseDatos == "test") Response.Cookies.Delete(RespaldoContextMiddleware.CookiePerfil);
        else if (baseDatos == "copia")
        {
            var s = await store.LeerAsync(); if (!s.CopiaLista || s.Restaurando || s.RestauracionPendiente != null) { TempData["Error"] = "La copia todavía no está restaurada."; return RedirectToAction(nameof(Index)); }
            var value = protection.CreateProtector("Keryx.TestDatabase.v1").Protect(JsonSerializer.Serialize(new PerfilDatos(Actor, s.Generacion, DateTimeOffset.UtcNow.AddHours(8))));
            Response.Cookies.Append(RespaldoContextMiddleware.CookiePerfil, value, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, MaxAge = TimeSpan.FromHours(8) });
        }
        else return BadRequest();
        TempData["Ok"] = "Selección aplicada a esta sesión de desarrollador. Los otros usuarios de test conservan su base."; return RedirectToAction("Index", "Home");
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PrepararUsuario(string usuario)
    {
        if (!Disponible()) return NotFound();
        try { TempData["ClaveEmergencia"] = await acceso.PrepararAsync(usuario, Actor); TempData["UsuarioEmergencia"] = usuario; }
        catch (InvalidOperationException e) { TempData["Error"] = e.Message; }
        return RedirectToAction(nameof(Index));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RevocarUsuario(string usuario)
    {
        if (!Disponible()) return NotFound();
        await store.Cambiar(s => { s.Accesos.RemoveAll(a => a.Usuario.Equals(usuario, StringComparison.OrdinalIgnoreCase)); RespaldoStore.Evento(s, Actor, "Credencial de emergencia revocada: " + usuario); return true; });
        return RedirectToAction(nameof(Index));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Mantenimiento(bool activar)
    {
        if (!Disponible()) return NotFound();
        await HttpContext.RequestServices.GetRequiredService<ModoOperativoStore>().GuardarAsync(activar, Actor);
        await store.Cambiar(s => { s.MantenimientoDelegaciones = activar; RespaldoStore.Evento(s, Actor, activar ? "Mantenimiento de delegaciones activado" : "Modo normal para delegaciones"); return true; });
        return RedirectToAction(nameof(Index), new { seccion = "contingencia" });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PrepararRetorno(bool confirmar)
    {
        if (!Disponible()) return NotFound();
        try
        {
            if (!confirmar) throw new InvalidOperationException("Confirmá el cierre de escritura de la copia para preparar el retorno.");
            var servicio = HttpContext.RequestServices.GetRequiredService<RetornoProduccionService>();
            var archivo = await servicio.PrepararAsync(Actor, HttpContext.RequestAborted);
            return PhysicalFile(archivo, "application/octet-stream", "keryx-retorno-produccion.kbk");
        }
        catch (InvalidOperationException e) { TempData["Error"] = e.Message; return RedirectToAction(nameof(Index), new { seccion = "contingencia" }); }
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Contingencia(bool activar, bool confirmar)
    {
        if (!Disponible()) return NotFound();
        try
        {
            var use = HttpContext.RequestServices.GetRequiredService<CopiaUso>();
            if (activar)
            {
                var state = await store.LeerAsync();
                if (!confirmar || !state.CopiaLista || state.Restaurando || state.RestauracionPendiente != null)
                    throw new InvalidOperationException("Restaurá la copia y confirmá la activación.");
                await use.Operacion.WaitAsync(HttpContext.RequestAborted);
                use.Bloquear();
                try
                {
                    while (use.Activas > 0) await Task.Delay(200, HttpContext.RequestAborted);
                    await HttpContext.RequestServices.GetRequiredService<RetornoProduccionService>().ValidarInicioAsync(HttpContext.RequestAborted);
                }
                catch { use.Liberar(); use.Operacion.Release(); throw; }
            }
            try
            {
                await store.Cambiar(s =>
                {
                    if (activar && (!confirmar || !s.CopiaLista || s.Restaurando || s.RestauracionPendiente != null || !s.Accesos.Any(a => a.HastaUtc > DateTimeOffset.UtcNow)))
                        throw new InvalidOperationException("Restaurá la copia, prepará credenciales y confirmá la activación.");
                    s.ContingenciaHastaUtc = activar ? DateTimeOffset.UtcNow.AddHours(24) : null; s.EpocaContingencia = Guid.NewGuid().ToString("N");
                    RespaldoStore.Evento(s, Actor, activar ? "Contingencia activada por 24 horas" : "Contingencia desactivada"); return true;
                });
            }
            finally { if (activar) { use.Liberar(); use.Operacion.Release(); } }
        }
        catch (InvalidOperationException e) { TempData["Error"] = e.Message; }
        return RedirectToAction(nameof(Index), new { seccion = "contingencia" });
    }
}

[AllowAnonymous]
[Route("api/testbackup")]
public class RespaldoAgenteController(RespaldoStore store, IConfiguration config) : ControllerBase
{
    async Task<bool> Autorizado() => await store.TokenValidoAsync(Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.Ordinal) ? Request.Headers.Authorization.ToString()[7..] : null);
    [HttpPost("install")]
    public async Task<IActionResult> Instalar()
    {
        if (!store.Habilitado || !Request.IsHttps) return NotFound();
        var auth = Request.Headers.Authorization.ToString();
        if (!auth.StartsWith("Bearer ", StringComparison.Ordinal) || auth.Length != 71) return NotFound();
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(auth[7..])));
        var ok = await store.Cambiar(s =>
        {
            if (s.InstalacionHash == null || s.InstalacionHastaUtc == null || s.InstalacionHastaUtc <= DateTimeOffset.UtcNow) return false;
            if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(s.InstalacionHash), System.Text.Encoding.ASCII.GetBytes(hash))) return false;
            s.InstalacionHash = null; s.InstalacionHastaUtc = null;
            RespaldoStore.Evento(s, "agente", "Código de instalación utilizado"); return true;
        });
        if (!ok) return NotFound();
        Response.Headers.CacheControl = "no-store";
        return File(await AgenteInstalador.CrearAsync(store, config), "application/zip", "keryx-agente-privado.zip");
    }
    [HttpGet("plan")]
    public async Task<IActionResult> Plan()
    {
        if (!await Autorizado()) return NotFound();
        var result = await store.Cambiar(s =>
        {
            var now = DateTimeOffset.UtcNow; s.UltimoAgenteUtc = now;
            var local = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(now, "America/Argentina/Buenos_Aires");
            var day = local.ToString("yyyy-MM-dd");
            if (s.Programado && s.SolicitudPendiente == null && s.UltimoDiaProgramado != day && TimeOnly.FromDateTime(local.DateTime) >= TimeOnly.Parse(s.Hora))
            { s.SolicitudPendiente = Guid.NewGuid().ToString("N"); s.SolicitadaUtc = now; s.UltimoDiaProgramado = day; }
            return new { requestId = s.SolicitudPendiente, maxBytes = 536870912 };
        });
        return Ok(result);
    }
    [HttpPost("chunk/{requestId}/{index:int}"), RequestSizeLimit(8388608)]
    public async Task<IActionResult> Chunk(string requestId, int index, CancellationToken ct)
    {
        if (!await Autorizado() || !Guid.TryParseExact(requestId, "N", out _)) return NotFound();
        if (!int.TryParse(Request.Headers["X-Backup-Parts"], out var parts) || parts is < 1 or > 64 || index < 0 || index >= parts) return BadRequest();
        var state = await store.LeerAsync(); if (state.UltimaSolicitudAtendida == requestId) return Ok(new { received = true });
        if (state.SolicitudPendiente != requestId) return Conflict();
        var root = Path.Combine(store.Directorio, "entrada", requestId); Directory.CreateDirectory(root);
        var destination = Path.Combine(root, index.ToString("D3")); var temp = destination + "." + Guid.NewGuid().ToString("N");
        try
        {
            long size = 0; await using (var output = new FileStream(temp, FileMode.CreateNew))
            {
                RespaldoStore.Privado(temp); var buffer = new byte[81920]; int n;
                while ((n = await Request.Body.ReadAsync(buffer, ct)) > 0) { size += n; if (size > 8388608) return StatusCode(413); await output.WriteAsync(buffer.AsMemory(0, n), ct); }
            }
            if (size == 0) return BadRequest(); System.IO.File.Move(temp, destination, true);
            if (index != parts - 1) return Ok(new { part = index });
            var assembled = Path.Combine(root, "completo.kbk");
            await using (var output = new FileStream(assembled, FileMode.Create))
            {
                RespaldoStore.Privado(assembled);
                for (int i = 0; i < parts; i++)
                {
                    var piece = Path.Combine(root, i.ToString("D3")); if (!System.IO.File.Exists(piece)) return Conflict();
                    await using var input = System.IO.File.OpenRead(piece); await input.CopyToAsync(output, ct);
                }
            }
            await using var body = System.IO.File.OpenRead(assembled); var original = Request.Body; Request.Body = body;
            IActionResult result;
            try { result = await Upload(requestId, ct); } finally { Request.Body = original; }
            if (result is OkObjectResult) { body.Close(); Directory.Delete(root, true); }
            return result;
        }
        finally { if (System.IO.File.Exists(temp)) System.IO.File.Delete(temp); }
    }
    [HttpPost("error")]
    public async Task<IActionResult> ErrorAgente()
    {
        if (!await Autorizado()) return NotFound();
        await store.Cambiar(s => { s.ErrorAgente = "El agente no pudo generar o enviar la copia. Revisá su servicio en producción."; return true; }); return Ok();
    }
    [HttpPost("upload/{requestId}"), RequestSizeLimit(536870912)]
    public async Task<IActionResult> Upload(string requestId, CancellationToken ct)
    {
        if (!await Autorizado() || !Guid.TryParseExact(requestId, "N", out _)) return NotFound();
        var state = await store.LeerAsync(); if (state.UltimaSolicitudAtendida == requestId) return Ok(new { received = true });
        if (state.SolicitudPendiente != requestId) return Conflict();
        var id = Guid.NewGuid().ToString("N"); var path = store.Archivo(id); var temp = path + ".partial";
        try
        {
            long size = 0; await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                RespaldoStore.Privado(temp); var buffer = new byte[81920]; int n;
                while ((n = await Request.Body.ReadAsync(buffer, ct)) > 0) { size += n; if (size > 536870912) return StatusCode(413); await output.WriteAsync(buffer.AsMemory(0, n), ct); }
            }
            var secrets = (await store.SecretosAsync())!; await RespaldoCifrado.VerificarAsync(temp, secrets.Clave, ct);
            string sha; await using (var file = System.IO.File.OpenRead(temp)) sha = Convert.ToHexString(await SHA256.HashDataAsync(file, ct));
            System.IO.File.Move(temp, path);
            await store.Cambiar(s =>
            {
                if (s.UltimaSolicitudAtendida == requestId) { System.IO.File.Delete(path); return false; }
                if (s.SolicitudPendiente != requestId) throw new InvalidOperationException("La solicitud cambió.");
                s.Archivos.Add(new() { Id = id, FechaUtc = DateTimeOffset.UtcNow, Bytes = size, Sha256 = sha, Solicitud = requestId });
                s.SolicitudPendiente = null; s.UltimaSolicitudAtendida = requestId; s.ErrorAgente = null;
                RespaldoStore.Evento(s, "agente", "Respaldo recibido y verificado: " + id);
                foreach (var old in s.Archivos.Where(a => a.FechaUtc < DateTimeOffset.UtcNow.AddDays(-s.RetencionDias) && a.Id != s.CopiaOrigen && a.Id != s.RestauracionPendiente).ToList())
                { System.IO.File.Delete(store.Archivo(old.Id)); s.Archivos.Remove(old); }
                return true;
            }); return Ok(new { received = true });
        }
        catch (InvalidOperationException) { return BadRequest(new { error = "El respaldo no supera las validaciones." }); }
        finally { if (System.IO.File.Exists(temp)) System.IO.File.Delete(temp); }
    }
}
