using System.IO.Compression;
using System.Security.Claims;
using Medios.Controllers;
using Medios.Entities;
using Medios.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

static class SesionWordChecks
{
    public static async Task<int> RunAsync()
    {
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); checks++; }
        var factory = new MemoriaFactory();
        var root = Path.Combine(Path.GetTempPath(), "keryx-word-finalizada-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var env = new Env { WebRootPath = root, ContentRootPath = root };
        var notas = new NotaService(factory);
        var sintesis = new SintesisService(factory, env, notas);
        var sesiones = new SesionService(factory, env, sintesis, notas);
        using var services = new ServiceCollection().AddSingleton<IWebHostEnvironment>(env).BuildServiceProvider();
        SesionController Controller(string role) => new(sesiones, notas, sintesis, null!, null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext {
                User = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.Role, role)], "test")), RequestServices = services } }
        };
        int id;
        try
        {
            using (var db = factory.Create())
            {
                var sesion = new SesionPrensa { Estado = "Finalizada", Fecha = new DateOnly(2026,10,25), Turno = "Vespertina", UsuarioCarga = "otro-operador" };
                foreach (var state in new[] { "Finalizada", "Descartada", "Sin Remitir" })
                {
                    var nota = new NotaPrensa { Sesion = sesion };
                    nota.VersionActual = new NotaVersion { Nota = nota, Version = 1, EstadoRevision = state,
                        Titulo = "NOTA-" + state, Texto = "Contenido editable", Sintesis = "Resumen editable",
                        AmbitoNota = "Nacional", Fuente = "Medio QA", Link = "https://example.test/noticia" };
                    sesion.Notas.Add(nota);
                }
                db.SesionesPrensas.Add(sesion); await db.SaveChangesAsync(); id = sesion.Id;
            }
            foreach (var role in new[] { "MEDIOS", "DESARROLLADOR" })
            {
                var result = await Controller(role).ExportarWord(id) as FileContentResult;
                Check(result != null && result.FileDownloadName.EndsWith(".docx"), "Word finalizada disponible para " + role);
                using var zip = new ZipArchive(new MemoryStream(result!.FileContents));
                using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
                var xml = await reader.ReadToEndAsync();
                Check(xml.Contains("NOTA-FINALIZADA") && xml.Contains("Resumen editable"), "Exporta contenido editable de las notas aptas");
                Check(!xml.Contains("NOTA-DESCARTADA") && !xml.Contains("NOTA-SIN REMITIR"), "No exporta descartadas ni pendientes");
                using var links = new StreamReader(zip.GetEntry("word/_rels/document.xml.rels")!.Open());
                Check((await links.ReadToEndAsync()).Contains("https://example.test/noticia"), "Conserva hipervínculos");
            }
            foreach (var role in new[] { "DELEGACION", "CONSULTA" })
                Check(await Controller(role).ExportarWord(id) is ForbidResult, "Bloquea descarga Word por URL para " + role);
            Check(await Controller("MEDIOS").ExportarWord(int.MaxValue) is NotFoundResult, "No exporta una sesión inexistente");
            using (var db = factory.Create())
            {
                Check(await db.Sintesis.CountAsync() == 0 && await db.SintesisNotas.CountAsync() == 0, "No requiere ni crea una consolidada");
                var s = (await db.SesionesPrensas.FindAsync(id))!;
                Check(s.Estado == "Finalizada" && s.PDFPath == null && await db.NotasVersion.CountAsync() == 3, "No genera PDF ni cambia estados o versiones");
                s.Estado = "Remitida"; await db.SaveChangesAsync();
            }
            Check(await Controller("MEDIOS").ExportarWord(id) is ConflictObjectResult, "Rechaza estados que no son Finalizada");
            using (var db = factory.Create())
            {
                (await db.SesionesPrensas.FindAsync(id))!.Estado = "Finalizada";
                foreach (var v in db.NotasVersion) v.EstadoRevision = "Descartada";
                await db.SaveChangesAsync();
            }
            Check(await Controller("MEDIOS").ExportarWord(id) is ConflictObjectResult, "No genera un Word vacío sin notas aptas");
            return checks;
        }
        finally { Directory.Delete(root, true); }
    }
    sealed class Env : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = "";
        public string ContentRootPath { get; set; } = "";
        public string ApplicationName { get; set; } = "Medios";
        public string EnvironmentName { get; set; } = "Testing";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
