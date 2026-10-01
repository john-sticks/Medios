using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Medios.Services;

public class IAService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ConfiguracionService _cfg;

    public IAService(HttpClient http, IConfiguration config, ConfiguracionService cfg)
    {
        _http = http;
        _config = config;
        _cfg = cfg;
    }

    private async Task<(string Endpoint, string? ApiKey, string Modelo)> ResolverProveedorAsync()
    {
        var proveedor = await _cfg.GetAsync("ia_proveedor") ?? "openrouter";

        if (proveedor == "ollama")
        {
            var url = (await _cfg.GetAsync("ia_ollama_url") ?? "http://localhost:11434").TrimEnd('/');
            var modelo = await _cfg.GetAsync("ia_ollama_modelo") ?? "qwen2.5:7b-instruct";
            return ($"{url}/v1/chat/completions", null, modelo);
        }

        // openrouter: DB tiene prioridad sobre env var
        var apiKey = await _cfg.GetAsync("ia_openrouter_apikey");
        if (string.IsNullOrWhiteSpace(apiKey))
            apiKey = _config["OpenRouter:ApiKey"];
        var modeloOr = await _cfg.GetAsync("ia_modelo") ?? "google/gemma-3-27b-it:free";
        return ("https://openrouter.ai/api/v1/chat/completions", apiKey, modeloOr);
    }

    private HttpRequestMessage BuildRequest(string endpoint, string? apiKey, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Headers.Add("HTTP-Referer", "https://medios.crmai.net.ar");
            req.Headers.Add("X-Title", "Medios");
        }
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return req;
    }

    private static string? ExtraerContent(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
    }

    private static string? ExtraerErrorMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("error").GetProperty("message").GetString();
        }
        catch { return null; }
    }

    public async Task<(string? Resultado, string? Error)> GenerarSintesisAsync(string texto, string prompt)
    {
        var (endpoint, apiKey, modelo) = await ResolverProveedorAsync();

        if (endpoint.Contains("openrouter") && string.IsNullOrWhiteSpace(apiKey))
            return (null, "API key de OpenRouter no configurada. Configurala en Ajustes → IA.");

        var body = new
        {
            model = modelo,
            messages = new[]
            {
                new { role = "system", content = prompt },
                new { role = "user",   content = texto  }
            }
        };

        HttpResponseMessage resp;
        try { resp = await _http.SendAsync(BuildRequest(endpoint, apiKey, body)); }
        catch (Exception ex) { return (null, $"Error de red: {ex.Message}"); }

        var json = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            return (null, ExtraerErrorMessage(json) ?? $"Error {(int)resp.StatusCode}");

        try { return (ExtraerContent(json), null); }
        catch { return (null, "Respuesta inesperada del servicio de IA."); }
    }

    public async Task<(int? CaratulaId, int? ModalidadId, string? Error)> SugerirCaratulaAsync(
        string texto, string caratulasJson)
    {
        var (endpoint, apiKey, modelo) = await ResolverProveedorAsync();

        if (endpoint.Contains("openrouter") && string.IsNullOrWhiteSpace(apiKey))
            return (null, null, "API key de OpenRouter no configurada.");

        var systemPrompt =
            "Sos un asistente de clasificación de noticias policiales argentinas. " +
            "Dado el texto de una noticia, seleccioná la carátula penal más adecuada y su modalidad " +
            "de la lista JSON proporcionada. " +
            "Respondé ÚNICAMENTE con un objeto JSON válido con las claves \"caratulaId\" (número entero) " +
            "y \"modalidadId\" (número entero o null si no aplica). No incluyas texto adicional ni markdown.\n\n" +
            "Lista de carátulas y modalidades disponibles:\n" + caratulasJson;

        var body = new
        {
            model = modelo,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = texto }
            },
            temperature = 0.1
        };

        HttpResponseMessage resp;
        try { resp = await _http.SendAsync(BuildRequest(endpoint, apiKey, body)); }
        catch (Exception ex) { return (null, null, $"Error de red: {ex.Message}"); }

        var json = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            return (null, null, $"Error {(int)resp.StatusCode}");

        string content;
        try { content = ExtraerContent(json) ?? ""; }
        catch { return (null, null, "El modelo de IA no devolvió una respuesta utilizable. Probá de nuevo."); }

        content = content.Trim();
        if (content.StartsWith("```")) content = content.Split('\n', 2).Last().TrimStart();
        if (content.EndsWith("```")) content = content[..content.LastIndexOf("```")].TrimEnd();
        content = content.Trim();

        if (string.IsNullOrWhiteSpace(content))
            return (null, null, "El modelo de IA no devolvió respuesta (puede estar saturado). Probá de nuevo en unos segundos.");

        try
        {
            using var result = JsonDocument.Parse(content);
            var root = result.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return (null, null, "El modelo de IA no devolvió un resultado válido. Probá de nuevo.");

            int? caratulaId = root.TryGetProperty("caratulaId", out var cEl) && cEl.ValueKind == JsonValueKind.Number
                ? cEl.GetInt32() : null;
            int? modalidadId = root.TryGetProperty("modalidadId", out var mEl) && mEl.ValueKind == JsonValueKind.Number
                ? mEl.GetInt32() : null;
            return (caratulaId, modalidadId, null);
        }
        catch (JsonException) { return (null, null, "El modelo de IA no devolvió un JSON válido. Probá de nuevo."); }
    }
}
