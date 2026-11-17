using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Medios.Infrastructure.Middleware;
using Medios.Services.Respaldos;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.DataProtection;

internal static class ContencionProduccionChecks
{
    public static async Task<int> RunAsync()
    {
        var count = 0;
        void Check(bool ok, string mensaje)
        {
            if (!ok) throw new InvalidOperationException("Contención: " + mensaje);
            count++;
        }
        var root = Directory.CreateTempSubdirectory("keryx-contencion-checks-");
        try
        {
            var file = Path.Combine(root.FullName, "agent.json");
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new
            { receiver = "https://medios.crmai.net.ar", token = new string('A',64), secret = "clave-prueba" }));
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
            {
                ["Deployment:Environment"] = "Production", ["RetornoProduccion:AgenteConfig"] = file,
                ["Operations:Directory"] = Path.Combine(root.FullName, "estado")
            }).Build();
            using var handler = new Handler();
            using var client = new HttpClient(handler);
            var protection = new EphemeralDataProtectionProvider();
            var memoria = new EstadoContencionProduccionStore(config, protection);
            var remoto = new ContencionProduccionService(client, config, NullLogger<ContencionProduccionService>.Instance, memoria);
            Check((await remoto.ConsultarAsync(default)) is { Activa: true, Conocido: true }, "estado autenticado del NAS");
            var uso = new RetornoProduccionUso();
            var passed = false;
            var middleware = new ContencionProduccionMiddleware(_ => { passed=true; return Task.CompletedTask; });
            var http = Contexto("DELEGACION", "/Nota/Listado", "GET");
            await middleware.InvokeAsync(http,remoto,uso);
            Check(!passed && http.Response.StatusCode==302 && http.Response.Headers.Location=="/Login", "sesión existente redirigida durante contención");
            http = Contexto("MEDIOS", "/Nota/Editar", "POST");
            await middleware.InvokeAsync(http,remoto,uso);
            Check(!passed && http.Response.StatusCode==423, "POST de una sesión existente bloqueado");
            http = Contexto("DESARROLLADOR", "/Monitor", "GET");
            await middleware.InvokeAsync(http,remoto,uso);
            Check(passed, "el desarrollador conserva acceso a mantenimiento");

            passed=false;
            handler.Fallar=true;
            http = Contexto("MEDIOS", "/Nota/Listado", "GET");
            await middleware.InvokeAsync(http,remoto,uso);
            Check(!passed && http.Response.StatusCode==302, "NAS no disponible bloquea la sesión operativa");
            Check(await remoto.ConsultarAsync(default) is { Conocido: false }, "fallo de red no equivale a contención desactivada");
            Check((await remoto.ConsultarAsync(default)).MensajeAcceso.Contains("Contactar con el administrador"),
                "contención activa sin test informa al usuario que contacte al administrador");
            handler.Fallar=false;
            handler.Activa=false;
            http = Contexto("DELEGACION", "/Nota/Listado", "GET");
            await middleware.InvokeAsync(http,remoto,uso);
            Check(passed, "se recupera el acceso al desactivar contención");
            handler.Fallar = true;
            var despuesDeReinicio = new ContencionProduccionService(client,config,
                NullLogger<ContencionProduccionService>.Instance, new EstadoContencionProduccionStore(config,protection));
            Check(await despuesDeReinicio.ConsultarAsync(default) is { Activa: false, Conocido: false },
                "recuerda modo normal al reiniciar producción con test fuera de línea");
            foreach (var method in new[] { "GET", "POST" })
            {
                passed = false;
                http = Contexto("DELEGACION", "/Nota/Listado", method);
                await middleware.InvokeAsync(http,despuesDeReinicio,uso);
                Check(passed, "modo normal permite trabajar aunque test no responda: " + method);
            }
            handler.Fallar = false;

            passed=false;
            uso.Bloquear();
            http = Contexto("DESARROLLADOR", "/Nota/Editar", "POST");
            await middleware.InvokeAsync(http,remoto,uso);
            Check(!passed && http.Response.StatusCode==503, "la importación pausa también escrituras de desarrollador");
            uso.Liberar();

            handler.Activa=true;
            await remoto.ConsultarAsync(default);
            handler.Fallar=true;
            var reinicioEnContencion = new ContencionProduccionService(client,config,
                NullLogger<ContencionProduccionService>.Instance, new EstadoContencionProduccionStore(config,protection));
            Check(await reinicioEnContencion.ConsultarAsync(default) is { Activa: true, Conocido: false },
                "recuerda contención activa al reiniciar sin test");
            handler.Fallar=false;
            var origin = Guid.NewGuid().ToString("N");
            handler.Origen=origin;
            var backup = await remoto.CrearRespaldoPrevioAsync(origin,default);
            Check(handler.Solicitudes==1 && backup.RespaldoPrevioSha256?.Length==64, "espera un respaldo confirmado del NAS");
            handler.SoloLectura=false;
            try
            {
                await remoto.CrearRespaldoPrevioAsync(origin,default);
                Check(false,"copia aún abierta");
            }
            catch (InvalidOperationException) { Check(handler.Solicitudes==1,"no solicita respaldo ni importa mientras test acepta cargas"); }

            config["Deployment:Environment"]="Test";
            Check(!remoto.Habilitado && (await remoto.ConsultarAsync(default)) is { Activa: false, Conocido: true },
                "el bloqueo productivo no afecta test");
            return count;
        }
        finally { root.Delete(true); }
    }

    private static DefaultHttpContext Contexto(string rol,string path,string method)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.Role,rol),new("RolReal",rol)],"test"))
        };
        http.Request.Path=path;
        http.Request.Method=method;
        http.Response.Body=new MemoryStream();
        return http;
    }

    private sealed class Handler : HttpMessageHandler
    {
        public bool Fallar { get; set; }
        public bool Activa { get; set; } = true;
        public bool SoloLectura { get; set; } = true;
        public string? Origen { get; set; }
        public int Solicitudes { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            if (request.Headers.Authorization?.ToString()!="Bearer " + new string('A',64)
                || request.RequestUri?.Host!="medios.crmai.net.ar")
                throw new InvalidOperationException("Consulta sin autorización");
            if (Fallar) throw new HttpRequestException("fallo simulado");
            if (request.Method==HttpMethod.Post)
            {
                Solicitudes++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content=JsonContent.Create(new { requestId=new string('a',32) }) });
            }
            var confirmado=request.RequestUri.Query.Contains("solicitud=");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content=JsonContent.Create(new EstadoContencionRemota(Activa,SoloLectura,true,Origen,
                    confirmado ? new string('b',32) : null,confirmado ? new string('C',64) : null))
            });
        }
    }
}
