using System.Net;
using System.Text;
using Medios.Entities;
using Medios.Services;
using Microsoft.Extensions.Configuration;

static class IARutaAmbienteChecks
{
    public static async Task<int> RunAsync()
    {
        int count = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); count++; }
        const string publica = "https://ollama.ejemplo.invalid";
        const string local = "http://10.10.10.41:11434";
        var factory = new MemoriaFactory();
        using (var db = factory.Create())
        {
            db.Configuraciones.AddRange(new Configuracion { Clave = "ia_proveedor", Valor = "ollama" },
                new Configuracion { Clave = "ia_ollama_url", Valor = publica });
            await db.SaveChangesAsync();
        }
        foreach (var sample in new[] {
            (Environment: "Test", Local: local + "/", Expected: local),
            (Environment: "Production", Local: local, Expected: publica),
            (Environment: "", Local: local, Expected: publica),
            (Environment: "Test", Local: "", Expected: publica)
        })
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["Deployment:Environment"] = sample.Environment, ["TestBackup:OllamaBaseUrl"] = sample.Local
            }).Build();
            using var handler = new RespuestaIA();
            using var http = new HttpClient(handler);
            var service = new IAService(http, config, new ConfiguracionService(factory));
            var result = await service.GenerarSintesisAsync("Texto ficticio", "Resumir");
            Check(result.Resultado == "OK" && result.Error == null, "IA: conservar el resultado del proveedor");
            Check(handler.Destino?.ToString() == sample.Expected + "/v1/chat/completions", "IA: elegir la ruta según el ambiente");
            using var db = factory.Create();
            Check((await db.Configuraciones.FindAsync("ia_ollama_url"))!.Valor == publica, "IA: no modificar la URL de la copia productiva");
        }
        var prod = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Deployment:Environment"] = "Production", ["TestBackup:OllamaBaseUrl"] = "ruta-invalida"
        }).Build();
        Check(IAService.UrlLocalDeTest(prod) == null, "IA: producción ignora incluso una ruta de test inválida");
        return count;
    }
    sealed class RespuestaIA : HttpMessageHandler
    {
        public Uri? Destino { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Destino = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"OK\"}}]}", Encoding.UTF8, "application/json")
            });
        }
    }
}
