using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Medios.Services.Respaldos;

// Usa las mismas opciones de conexión directa que el agente de respaldos.
// La autorización se transmite por stdin y no aparece en la línea de comandos.
public sealed class ContencionCurlHandler : HttpMessageHandler
{
    // curl interpreta los escapes de comillas y barras, pero no las secuencias \u de JSON.
    private static readonly JsonSerializerOptions ConfigJson = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri;
        if (uri == null || uri.Scheme != "https" || uri.Host != "medios.crmai.net.ar" || !uri.IsDefaultPort
            || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0
            || !(request.Method == HttpMethod.Get && uri.AbsolutePath == "/api/testbackup/contencion"
                || request.Method == HttpMethod.Post && uri.AbsolutePath == "/api/testbackup/respaldo-previo"))
            throw new HttpRequestException("Destino de contención no permitido.");

        var start = new ProcessStartInfo("curl")
        {
            UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in new[] { "--disable", "--config", "-", "--proto", "=https", "--noproxy", "*",
            "--silent", "--show-error", "--connect-timeout", "5", "--max-time", "8",
            "--max-filesize", "1048576", "--write-out", "\n%{http_code}", "--url", uri.AbsoluteUri })
            start.ArgumentList.Add(arg);

        var headers = new[] { "Authorization: " + request.Headers.Authorization, "Content-Type: application/json" };
        if (request.Headers.Authorization?.Scheme != "Bearer" || headers.Any(h => h.Contains('\r') || h.Contains('\n')))
            throw new HttpRequestException("Autorización de contención no válida.");
        var config = string.Join("", headers.Select(h => "header = " + JsonSerializer.Serialize(h, ConfigJson) + "\n"));
        if (request.Content != null)
        {
            var body = await request.Content.ReadAsStringAsync(ct);
            if (body.Length > 1024) throw new HttpRequestException("Solicitud de respaldo demasiado grande.");
            config += "request = \"POST\"\ndata = " + JsonSerializer.Serialize(body, ConfigJson) + "\n";
        }

        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new HttpRequestException("No se pudo iniciar el transporte de contención.");
        }
        catch (System.ComponentModel.Win32Exception)
        { throw new HttpRequestException("No está disponible el transporte de contención."); }
        using var cancellation = ct.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(true); }
            catch (InvalidOperationException) { }
        });
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var errors = process.StandardError.ReadToEndAsync(ct);
        await process.StandardInput.WriteAsync(config.AsMemory(), ct);
        process.StandardInput.Close();
        await process.WaitForExitAsync(ct);
        var result = await output;
        await errors;
        if (process.ExitCode != 0)
            throw new HttpRequestException("Falló el transporte HTTPS de contención (curl " + process.ExitCode + ").");
        var lastLine = result.LastIndexOf('\n');
        if (lastLine < 0 || !int.TryParse(result[(lastLine + 1)..], NumberStyles.None,
            CultureInfo.InvariantCulture, out var status) || status is < 100 or > 599)
            throw new HttpRequestException("Respuesta de contención inválida.");
        return new HttpResponseMessage((HttpStatusCode)status)
        {
            RequestMessage = request,
            Content = new StringContent(result[..lastLine], System.Text.Encoding.UTF8, "application/json")
        };
    }
}
