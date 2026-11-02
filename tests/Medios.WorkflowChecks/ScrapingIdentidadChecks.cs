using System.Net;
using Medios.Services;

static class ScrapingIdentidadChecks
{
    public static async Task<int> RunAsync()
    {
        int count = 0;
        foreach (var dominio in new[] { "laopinion.com.ar", "www.laopinion.com.ar", "rumoresdepehuajo.com.ar", "www.rumoresdepehuajo.com.ar", "rumoresdepehuajo.com.ar.ejemplo.invalid", "otro.ejemplo.invalid", "laopinion.com.ar.ejemplo.invalid" })
        {
            using var handler = new Respuesta();
            using var http = new HttpClient(handler);
            var scraper = new ScraperService(http, new ScrapingReglaService(new MemoriaFactory()));
            var result = await scraper.ScrapearAsync("https://" + dominio + "/noticia/");
            bool identidadExplicita = dominio is "laopinion.com.ar" or "www.laopinion.com.ar" or "rumoresdepehuajo.com.ar" or "www.rumoresdepehuajo.com.ar";
            if (identidadExplicita != (handler.UserAgent == "Keryx/1.0")) throw new Exception("Identidad de scraping aplicada a otro portal");
            count++;
            if (result.Error != null || result.Titulo != "Título de prueba") throw new Exception("La identidad no debe alterar la extracción");
            count++;
        }
        var factory = new MemoriaFactory();
        using (var db = factory.Create())
        {
            db.ScrapingReglas.Add(new Medios.Entities.ScrapingRegla {
                Dominio = "rumoresdepehuajo.com.ar", Activo = true, XPathTitulo = "//h1",
                XPathCuerpo = "//div[contains(concat(' ',normalize-space(@class),' '),' articulo-cuerpo ')]"
            });
            await db.SaveChangesAsync();
        }
        using var cuerpoHandler = new Respuesta { Html = "<html><body><article><h1>Título principal</h1><div class='articulo-cuerpo'><p>Un importante operativo policial se desarrolló en General Villegas durante la madrugada de este domingo.</p><p>La investigación continúa bajo intervención de la autoridad judicial correspondiente.</p></div><div class='articulo-cuerpo-recomendado'><p>OTRA NOTICIA: daños de una tormenta en otra ciudad y otra vivienda.</p></div></article></body></html>" };
        using var cuerpoHttp = new HttpClient(cuerpoHandler);
        var importada = await new ScraperService(cuerpoHttp, new ScrapingReglaService(factory))
            .ScrapearAsync("https://rumoresdepehuajo.com.ar/contenido/19263/ejemplo");
        if (importada.Error != null || importada.Texto == null || !importada.Texto.Contains("La investigación continúa") || importada.Texto.Contains("OTRA NOTICIA"))
            throw new Exception("La regla de Rumores debe limitarse al cuerpo principal, sin recomendaciones");
        count++;
        return count;
    }
    sealed class Respuesta : HttpMessageHandler
    {
        public string? UserAgent { get; private set; }
        public string Html { get; set; } = "<html><head><meta charset='utf-8'></head><body><article><h1>Título de prueba</h1><p>Contenido suficientemente extenso de una noticia para verificar la extracción del cuerpo original.</p></article></body></html>";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            UserAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(Html)
            });
        }
    }
}
