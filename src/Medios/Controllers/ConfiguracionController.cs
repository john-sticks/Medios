using Microsoft.AspNetCore.Mvc;
using Medios.Security;
using Medios.Services;

namespace Medios.Controllers;

public class ConfiguracionController : Controller
{
    private readonly ConfiguracionService _config;
    private readonly AuditoriaService _auditoria;
    private readonly EmailService _email;

    public ConfiguracionController(ConfiguracionService config, AuditoriaService auditoria, EmailService email)
    {
        _config = config;
        _auditoria = auditoria;
        _email = email;
    }

    private string GetNombre() => User.Identity?.Name ?? "";
    private string GetIp() =>
        Request.Headers["X-Forwarded-For"].FirstOrDefault()
        ?? HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "";

    [HasPermission("ADMINISTRAR_CONFIGURACION")]
    [HttpGet]
    public async Task<IActionResult> PromptIA()
    {
        ViewData["Title"] = "Configuración IA";
        var cfg = await _config.GetAllAsync();
        ViewBag.Prompt            = cfg.GetValueOrDefault("ia_prompt") ?? "";
        ViewBag.Proveedor         = cfg.GetValueOrDefault("ia_proveedor") ?? "openrouter";
        ViewBag.OrModelo          = cfg.GetValueOrDefault("ia_modelo") ?? "google/gemma-3-27b-it:free";
        ViewBag.OrApiKey          = cfg.GetValueOrDefault("ia_openrouter_apikey") ?? "";
        ViewBag.OllamaUrl         = cfg.GetValueOrDefault("ia_ollama_url") ?? "http://localhost:11434";
        ViewBag.OllamaModelo      = cfg.GetValueOrDefault("ia_ollama_modelo") ?? "qwen2.5:7b-instruct";
        return View();
    }

    [HasPermission("ADMINISTRAR_CONFIGURACION")]
    [HttpPost]
    public async Task<IActionResult> PromptIA(string prompt, string proveedor,
        string? orModelo, string? orApiKey, string? ollamaUrl, string? ollamaModelo)
    {
        await _config.SetAsync("ia_prompt",           prompt?.Trim());
        await _config.SetAsync("ia_proveedor",        proveedor?.Trim() ?? "openrouter");
        await _config.SetAsync("ia_modelo",           orModelo?.Trim());
        if (!string.IsNullOrWhiteSpace(orApiKey))
            await _config.SetAsync("ia_openrouter_apikey", orApiKey.Trim());
        await _config.SetAsync("ia_ollama_url",       ollamaUrl?.Trim());
        await _config.SetAsync("ia_ollama_modelo",    ollamaModelo?.Trim());
        await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "configuracion", "POST", "prompt_ia",
            new { proveedor, orModelo, ollamaUrl, ollamaModelo });
        TempData["Ok"] = "Configuración guardada";
        return RedirectToAction(nameof(PromptIA));
    }

    // Devuelve la lista de modelos instalados en Ollama (para el picker de la UI)
    [HasPermission("ADMINISTRAR_CONFIGURACION")]
    [HttpGet]
    public async Task<IActionResult> OllamaModelos([FromQuery] string? url)
    {
        var baseUrl = (url ?? "http://localhost:11434").TrimEnd('/');
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var json = await http.GetStringAsync($"{baseUrl}/api/tags");
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var modelos = doc.RootElement
                .GetProperty("models")
                .EnumerateArray()
                .Select(m => m.GetProperty("name").GetString())
                .Where(n => n != null)
                .ToList();
            return Json(new { modelos });
        }
        catch { return Json(new { modelos = Array.Empty<string>() }); }
    }

    // ── Configuración de correo (SMTP) ────────────────────────────────

    [HasPermission("ADMINISTRAR_CONFIGURACION")]
    [HttpGet]
    public async Task<IActionResult> Mail()
    {
        ViewData["Title"] = "Configuración Mail";
        var cfg = await _config.GetAllAsync();
        ViewBag.Host      = cfg.GetValueOrDefault("smtp_host") ?? "";
        ViewBag.Port      = cfg.GetValueOrDefault("smtp_port") ?? "587";
        ViewBag.User      = cfg.GetValueOrDefault("smtp_user") ?? "";
        ViewBag.HasPass   = !string.IsNullOrEmpty(cfg.GetValueOrDefault("smtp_pass"));
        ViewBag.From      = cfg.GetValueOrDefault("smtp_from") ?? "";
        ViewBag.FromName  = cfg.GetValueOrDefault("smtp_fromname") ?? "División Medios O.S.INT";
        ViewBag.EnableSsl = (cfg.GetValueOrDefault("smtp_enablessl") ?? "true") == "true";
        return View();
    }

    [HasPermission("ADMINISTRAR_CONFIGURACION")]
    [HttpPost]
    public async Task<IActionResult> Mail(string? host, string? port, string? user, string? pass,
        string? from, string? fromName, bool enableSsl = false)
    {
        await _config.SetAsync("smtp_host", host?.Trim());
        await _config.SetAsync("smtp_port", string.IsNullOrWhiteSpace(port) ? "587" : port.Trim());
        await _config.SetAsync("smtp_user", user?.Trim());
        // Solo actualizar la contraseña si se ingresó una nueva (campo vacío = mantener)
        if (!string.IsNullOrEmpty(pass))
            await _config.SetAsync("smtp_pass", pass);
        await _config.SetAsync("smtp_from", from?.Trim());
        await _config.SetAsync("smtp_fromname", fromName?.Trim());
        await _config.SetAsync("smtp_enablessl", enableSsl ? "true" : "false");

        await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "configuracion", "POST", "smtp",
            new { host, port, user, from });
        TempData["Ok"] = "Configuración de correo guardada";
        return RedirectToAction(nameof(Mail));
    }

    // Envía un correo de prueba a la dirección indicada
    [HasPermission("ADMINISTRAR_CONFIGURACION")]
    [HttpPost]
    public async Task<IActionResult> MailPrueba(string? destino)
    {
        if (string.IsNullOrWhiteSpace(destino) || !destino.Contains('@'))
        {
            TempData["Error"] = "Ingresá un correo de destino válido para la prueba.";
            return RedirectToAction(nameof(Mail));
        }

        var (ok, error) = await _email.EnviarAsync(
            new[] { destino.Trim() },
            "Correo de prueba — División Medios O.S.INT",
            "<p>Este es un correo de prueba del sistema <strong>División Medios O.S.INT</strong>.</p>" +
            "<p>Si lo recibiste, la configuración SMTP funciona correctamente.</p>");

        if (ok) TempData["Ok"] = $"Correo de prueba enviado a {destino}.";
        else    TempData["Error"] = error;
        return RedirectToAction(nameof(Mail));
    }
}
