using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Medios.Entities;
using Medios.Infrastructure.Data;
using Medios.Services;
using Microsoft.EntityFrameworkCore;

static class ScrapingTitulosChecks
{
    static List<(string Dominio,string XPath)> Rules() => Regex.Matches(
        File.ReadAllText("sql/38_scraping_titulos_portales.sql"),
        @"\('(?<d>[^']+)','(?<x>(?:''|[^'])*)',1,1,")
        .Select(m=>(m.Groups["d"].Value,m.Groups["x"].Value.Replace("''","'"))).ToList();

    public static async Task<int> RunAsync()
    {
        var rules=Rules();
        if(rules.Count!=12)throw new Exception("Se esperan doce reglas de título");
        string[] headings=[
            "<h1 class='entry-title'>TÍTULO</h1>",
            "<div id='noticia'><div class='titulo_noticia'>TÍTULO</div></div>",
            "<h1 class='cm-entry-title'>TÍTULO</h1>",
            "<div data-id='b7fd84e'><div class='jet-listing-dynamic-field__content'>TÍTULO</div></div>",
            "<div class='cuadro'><h2>TÍTULO</h2></div>",
            "<h1 class='articulo__titulo'>TÍTULO</h1>",
            "<h1 class='entry-title'>TÍTULO</h1>",
            "<h1 class='entry-title'>TÍTULO</h1>",
            "<h1 class='vw-post-title'>TÍTULO</h1>",
            "<h1 class='entry-title'>TÍTULO</h1>",
            "<div id='destacada0'><h3>TÍTULO</h3></div>",
            "<h1 class='post-title entry-title'>TÍTULO</h1>"
        ];
        var expected="San Nicolás: título legítimo | dos partes - con tildes";
        for(int i=0;i<rules.Count;i++)
        {
            var html="<html><head><title>Medio | Título SEO | Diario</title></head><body><header>"+
                headings[i].Replace("TÍTULO",expected)+"</header><article><p>Primer párrafo suficientemente largo para ser considerado parte de la noticia.</p><p>Segundo párrafo suficientemente largo para ser considerado parte de la noticia.</p></article></body></html>";
            var result=await Import(rules[i],html);
            if(result.Error!=null || result.Titulo!=expected)throw new Exception("Título incorrecto: "+rules[i].Dominio+" / "+result.Titulo+" / "+result.Error);
        }
        // Sin regla: el h1 de la noticia dentro de header también debe conservarse.
        var general=await Import(null,"<title>Título SEO | Diario</title><header><h1>Encabezado editorial</h1></header>");
        if(general.Titulo!="Encabezado editorial")throw new Exception("Se perdió el h1 dentro de header");
        return 14;
    }

    public static async Task SamplesAsync(string root)
    {
        var rules=Rules().ToDictionary(r=>r.Dominio);
        using var samples=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"scraping-titulos-html.json")));
        foreach(var sample in samples.RootElement.EnumerateArray())
        {
            if(!sample.TryGetProperty("file",out var file))continue;
            var url=sample.GetProperty("url").GetString()!;
            var domain=ScrapingReglaService.NormalizarDominio(url);
            var html=File.ReadAllText(Path.Combine(root,"scraping-html",file.GetString()!));
            var result=await Import(rules[domain],html);
            Console.WriteLine(JsonSerializer.Serialize(new{Dominio=domain,Url=url,Titulo=result.Titulo,Error=result.Error}));
            if(result.Error!=null || string.IsNullOrWhiteSpace(result.Titulo) || result.Titulo.Contains('|'))throw new Exception("Revisar muestra "+domain);
        }
    }
    static async Task<(string? Titulo,string? Error)> Import((string Dominio,string XPath)? rule,string html)
    {
        var factory=new Factory();
        if(rule is {} r)
        {
            using var db=factory.Create();
            db.ScrapingReglas.Add(new ScrapingRegla{Dominio=r.Dominio,XPathTitulo=r.XPath,Activo=true});
            await db.SaveChangesAsync();
        }
        using var client=new HttpClient(new Handler(html));
        var scraper=new ScraperService(client,new ScrapingReglaService(factory));
        var result=await scraper.ScrapearAsync("https://"+(rule?.Dominio??"ejemplo.test")+"/noticia");
        return(result.Titulo,result.Error);
    }
    sealed class Handler(string html):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(html,Encoding.UTF8,"text/html")});
    }
    sealed class Factory:IMediosDbContextFactory
    {
        readonly string name=Guid.NewGuid().ToString();
        public MediosDbContext Create()=>new(new DbContextOptionsBuilder<MediosDbContext>().UseInMemoryDatabase(name).Options);
    }
}
