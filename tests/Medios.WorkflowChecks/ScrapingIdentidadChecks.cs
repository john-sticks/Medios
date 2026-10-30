using System.Net;
using Medios.Services;

static class ScrapingIdentidadChecks
{
    public static async Task<int> RunAsync()
    {
        int count = 0;
        foreach (var dominio in new[] { "laopinion.com.ar", "www.laopinion.com.ar", "otro.ejemplo.invalid", "laopinion.com.ar.ejemplo.invalid" })
        {
            using var handler = new Respuesta();
            using var http = new HttpClient(handler);
            var scraper = new ScraperService(http, new ScrapingReglaService(new MemoriaFactory()));
            var result = await scraper.ScrapearAsync("https://" + dominio + "/noticia/");
            bool opinion = dominio is "laopinion.com.ar" or "www.laopinion.com.ar";
            if (opinion != (handler.UserAgent == "Keryx/1.0")) throw new Exception("Identidad de scraping aplicada a otro portal");
            count++;
            if (result.Error != null || result.Titulo != "Título de prueba") throw new Exception("La identidad no debe alterar la extracción");
            count++;
        }
        return count;
    }
    sealed class Respuesta : HttpMessageHandler
    {
        public string? UserAgent { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            UserAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent("<html><head><meta charset='utf-8'></head><body><article><h1>Título de prueba</h1><p>Contenido suficientemente extenso de una noticia para verificar la extracción del cuerpo original.</p></article></body></html>")
            });
        }
    }
}
