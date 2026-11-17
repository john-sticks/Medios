using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Medios.Services.Respaldos;

public sealed record EstadoContencionRemota(bool Activa, bool SoloLectura, bool Conocido,
    string? OrigenRespaldo = null, string? RespaldoPrevioId = null, string? RespaldoPrevioSha256 = null, string? ErrorRespaldo = null);

public sealed class ContencionProduccionService(HttpClient client, IConfiguration configuration,
    ILogger<ContencionProduccionService> logger)
{
    public bool Habilitado => configuration["Deployment:Environment"] == "Production";
    private sealed record Agente(string receiver, string token, string secret);
    private Agente LeerAgente()
    {
        var path = configuration["RetornoProduccion:AgenteConfig"] ?? "/run/keryx-backup/agent.json";
        var agente = JsonSerializer.Deserialize<Agente>(File.ReadAllText(path))
            ?? throw new InvalidOperationException("No se pudo leer la configuración del agente de respaldo.");
        if (!Uri.TryCreate(agente.receiver, UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || uri.Host != "medios.crmai.net.ar" || !uri.IsDefaultPort
            || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || agente.token?.Length != 64 || !agente.token.All(Uri.IsHexDigit)
            || string.IsNullOrWhiteSpace(agente.secret))
            throw new InvalidOperationException("El receptor de contención no tiene una configuración válida.");
        return agente;
    }

    public string ClaveCifrado => LeerAgente().secret;
    public bool TieneClaveConfigurada
    {
        get
        {
            try { return !string.IsNullOrWhiteSpace(ClaveCifrado); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { return false; }
        }
    }

    public async Task<EstadoContencionRemota> ConsultarAsync(CancellationToken ct, string? solicitud = null)
    {
        if (!Habilitado) return new(false, false, true);
        try
        {
            var agente = LeerAgente();
            using var request = Crear(agente, HttpMethod.Get, "contencion" +
                (solicitud == null ? "" : "?solicitud=" + Uri.EscapeDataString(solicitud)));
            using var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var estado = await response.Content.ReadFromJsonAsync<EstadoContencionRemota>(ct);
            return estado is { Conocido: true } ? estado : new(true, false, false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or OperationCanceledException)
        {
            logger.LogWarning("No se pudo consultar la contención ({Tipo}).", e.GetType().Name);
            return new(true, false, false);
        }
    }

    public async Task<EstadoContencionRemota> CrearRespaldoPrevioAsync(string origen, CancellationToken ct)
    {
        var estado = await ConsultarAsync(ct);
        if (!estado.Conocido || !estado.Activa || !estado.SoloLectura || estado.OrigenRespaldo != origen)
            throw new InvalidOperationException("Cerrá la escritura de la contingencia y revisá que el paquete corresponda a la copia activa.");
        var agente = LeerAgente();
        using var request = Crear(agente, HttpMethod.Post, "respaldo-previo");
        request.Content = JsonContent.Create(new { OrigenRespaldo = origen });
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<Solicitud>(ct)
            ?? throw new InvalidOperationException("No se recibió la solicitud de respaldo previo.");
        if (!Guid.TryParseExact(result.RequestId, "N", out _)) throw new InvalidOperationException("Solicitud de respaldo inválida.");
        var limite = DateTimeOffset.UtcNow.AddMinutes(15);
        while (DateTimeOffset.UtcNow < limite)
        {
            var progreso = await ConsultarAsync(ct, result.RequestId);
            if (!progreso.Conocido || !progreso.Activa || !progreso.SoloLectura || progreso.OrigenRespaldo != origen)
                throw new InvalidOperationException("Cambió el estado de contención. La importación se detuvo.");
            if (progreso.RespaldoPrevioId != null && progreso.RespaldoPrevioSha256?.Length == 64) return progreso;
            if (!string.IsNullOrEmpty(progreso.ErrorRespaldo))
                throw new InvalidOperationException("El respaldo previo falló. Revisá el agente productivo antes de reintentar.");
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }
        throw new InvalidOperationException("El respaldo previo sigue pendiente. La importación no se aplicó.");
    }

    private static HttpRequestMessage Crear(Agente agente, HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, agente.receiver.TrimEnd('/') + "/api/testbackup/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agente.token);
        return request;
    }

    private sealed record Solicitud(string RequestId);
}
