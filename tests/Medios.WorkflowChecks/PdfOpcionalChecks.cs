using System.Security.Claims;
using System.Text;
using Medios.Controllers;
using Medios.Entities;
using Medios.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using QuestPDF.Drawing;
using QuestPDF.Infrastructure;

static class PdfOpcionalChecks
{
    public static async Task<int> RunAsync()
    {
        int checks = 0;
        void Check(bool ok, string mensaje) { if (!ok) throw new InvalidOperationException(mensaje); checks++; }
        var root = Path.Combine(Path.GetTempPath(), "keryx-pdf-opcional-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var env = new Env { WebRootPath = root };
        var factory = new MemoriaFactory();
        var notas = new NotaService(factory);
        var sintesis = new SintesisService(factory, env, notas);
        var sesiones = new SesionService(factory, env, sintesis, notas);
        using var servicios = new ServiceCollection().AddSingleton<IWebHostEnvironment>(env).BuildServiceProvider();
        QuestPDF.Settings.License = LicenseType.Community;
        // Fuentes portables para comprobar el renderer también en runners sin Office.
        foreach (var familia in new[] { "Arial", SintesisFuentes.Bookman })
        foreach (var nombre in new[] { "Regular", "Bold", "Italic", "BoldItalic" })
        {
            using var fuente = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "LatoFont", $"Lato-{nombre}.ttf"));
            FontManager.RegisterFontWithCustomName(familia, fuente);
        }
        int sesionId, delegacionId;
        try
        {
            using (var db = factory.Create())
            {
                var delegacion = new Delegacion { Nombre = "Delegación PDF opcional" };
                var sesion = new SesionPrensa { Delegacion = delegacion, UsuarioCarga = "operador", Fecha = new(2026, 11, 18), Turno = "Vespertina" };
                var nota = new NotaPrensa { Sesion = sesion };
                var version = new NotaVersion { Nota = nota, EsActual = true, Titulo = "Noticia de prueba", Sintesis = "Contenido conservado en la base de datos.", EstadoRevision = "Borrador" };
                nota.VersionActual = version;
                db.NotasPrensa.Add(nota);
                await db.SaveChangesAsync();
                sesionId = sesion.Id; delegacionId = delegacion.Id;
            }
            var id = await sesiones.ConvertirASintesisAsync(sesionId, "operador");
            var publicada = (await sintesis.GetByIdAsync(id))!;
            Check(publicada.Estado == "Generada" && publicada.PDFPath == null, "Publicar deja la síntesis en Bandeja sin archivo PDF");
            Check(SintesisService.PuedeRemitir(publicada), "Remitir está disponible sin descargar PDF");
            Check((await sintesis.GenerarPdfAsync(id)) == $"/Sintesis/DescargarPdf/{id}" && !Directory.Exists(Path.Combine(root, "pdf")), "La generación normal no almacena PDF ni crea directorios");
            SintesisController Controller(string rol, int? delegacion = null) => new(sintesis, sesiones, null!, null!, null!, null!, null!, notas, null!)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, rol), new Claim("Usuario", "operador"), new Claim("DelegacionId", (delegacion ?? delegacionId).ToString()) }, "test")),
                    RequestServices = servicios
                } }
            };
            foreach (var rol in new[] { "DELEGACION", "MEDIOS", "DESARROLLADOR" })
            {
                var descarga = await Controller(rol).DescargarPdf(id) as FileContentResult;
                Check(descarga != null && Encoding.ASCII.GetString(descarga.FileContents, 0, 4) == "%PDF", "Descarga desde DB sin archivo para " + rol);
            }
            Check(await Controller("DELEGACION", delegacionId + 999).DescargarPdf(id) is ForbidResult, "La regeneración no permite descargar síntesis de otra delegación");
            var despues = (await sintesis.GetByIdAsync(id))!;
            Check(despues.PDFPath == null && despues.Estado == publicada.Estado && despues.FechaGeneracion == publicada.FechaGeneracion, "Descargar no cambia ruta, estado ni fecha");
            Check(!Directory.Exists(Path.Combine(root, "pdf")), "Descargar no deja archivos en el servidor");
            Directory.CreateDirectory(Path.Combine(root, "pdf"));
            var obsoleto = Path.Combine(root, "pdf", "anterior.pdf");
            await File.WriteAllTextAsync(obsoleto, "Archivo anterior que no debe utilizarse");
            using (var db = factory.Create())
            {
                (await db.Sintesis.FindAsync(id))!.PDFPath = "/pdf/anterior.pdf";
                await db.SaveChangesAsync();
            }
            var regenerado = (FileContentResult)await Controller("MEDIOS").DescargarPdf(id);
            Check(Encoding.ASCII.GetString(regenerado.FileContents, 0, 4) == "%PDF", "Un PDF normal existente tampoco sustituye la generación desde DB");
            Check(await File.ReadAllTextAsync(obsoleto) == "Archivo anterior que no debe utilizarse", "La descarga no modifica los archivos antiguos");
            File.Delete(obsoleto);
            Check(await Controller("MEDIOS").DescargarPdf(id) is FileContentResult, "Una referencia antigua a un archivo eliminado sigue permitiendo descargar desde DB");
            using (var db = factory.Create())
            {
                (await db.Sintesis.FindAsync(id))!.PDFPath = null;
                await db.SaveChangesAsync();
            }
            var archivos = new ArchivoPdfService(factory, env);
            Check((await archivos.ListarAsync(publicada.Fecha, publicada.Fecha, delegacionId)).Single().Disponible, "Archivo de PDF ofrece la síntesis sin archivo físico");
            Check(await archivos.PuedeRegenerarAsync(id, false), "Archivo de PDF deriva a generación desde DB");
            Check(await sintesis.RemitirAsync(id), "El backend permite remitir sin PDF");
            Check(!await sintesis.RemitirAsync(id), "No se puede remitir una síntesis dos veces");
            using (var db = factory.Create())
            {
                var anterior = (await db.Sintesis.FindAsync(id))!;
                anterior.Estado = "Borrador";
                await db.SaveChangesAsync();
            }
            Check(await sintesis.RemitirAsync(id), "Las síntesis anteriores en Bandeja tampoco requieren PDF");
            Check(!SintesisService.PuedeRemitir(new Sintesis { DelegacionId = delegacionId, Estado = "Generada" }), "No se remite una síntesis vacía");
            Check(!SintesisService.PuedeRemitir(new Sintesis { Estado = "Generada", NotasIncluidas = [new()] }), "El flujo consolidado conserva su validación de PDF");
            Directory.CreateDirectory(Path.Combine(root, "pdf"));
            var bytes = Encoding.UTF8.GetBytes("%PDF-consolidado-conservado");
            await File.WriteAllBytesAsync(Path.Combine(root, "pdf", "consolidado.pdf"), bytes);
            int consolidado;
            using (var db = factory.Create())
            {
                var registro = new Sintesis { Estado = "Informada", PDFPath = "/pdf/consolidado.pdf" };
                db.Sintesis.Add(registro); await db.SaveChangesAsync(); consolidado = registro.Id;
            }
            var original = (FileContentResult)await Controller("MEDIOS").DescargarPdf(consolidado);
            Check(original.FileContents.SequenceEqual(bytes), "Descargar consolidado conserva el documento almacenado");
            Check(!await archivos.PuedeRegenerarAsync(consolidado, false), "Archivo de PDF no regenera los consolidados");
        }
        finally { Directory.Delete(root, true); }
        return checks;
    }

    sealed class Env : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = "";
        public string ContentRootPath { get; set; } = "";
        public string ApplicationName { get; set; } = "PdfOpcionalChecks";
        public string EnvironmentName { get; set; } = "Testing";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
