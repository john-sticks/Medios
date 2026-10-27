using System.Security.Claims;
using Medios.Controllers;
using Medios.Entities;
using Medios.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

static class ArchivoPdfChecks
{
    public static async Task<int> RunAsync()
    {
        var count = 0;
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
            count++;
        }
        async Task Rechazar(Func<Task> accion, string message)
        {
            try { await accion(); }
            catch (ArgumentException) { Check(true, message); return; }
            throw new InvalidOperationException(message);
        }
        var root = Path.Combine(Path.GetTempPath(), "medios-pdf-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "pdf"));
        try
        {
            var factory = new MemoriaFactory();
            var service = new ArchivoPdfService(factory, new PdfEnvironment { WebRootPath = root });
            var fecha = new DateOnly(2026, 10, 16);
            int consolidadaId, delegacionId, filtroDelegacionId;
            using (var db = factory.Create())
            {
                var delegacion = new Delegacion { Nombre = "Delegación de prueba" };
                var consolidada = new Sintesis { Fecha = fecha, Estado = "Informada", PDFPath = "/pdf/original.pdf", FechaGeneracion = new DateTime(2026, 10, 16, 12, 0, 0) };
                var propia = new Sintesis { Fecha = fecha.AddDays(-1), Delegacion = delegacion, PDFPath = "/pdf/delegacion.pdf" };
                db.Sintesis.AddRange(consolidada, propia);
                db.SesionesPrensas.AddRange(
                    new SesionPrensa { Fecha = fecha, Delegacion = delegacion, PDFPath = "/pdf/sesion.pdf" },
                    new SesionPrensa { Fecha = fecha.AddDays(-1), Delegacion = delegacion, PDFPath = "/pdf/delegacion.pdf" },
                    new SesionPrensa { Fecha = fecha, Delegacion = delegacion, PDFPath = "/pdf/faltante.pdf" },
                    new SesionPrensa { Fecha = fecha, Delegacion = delegacion });
                await db.SaveChangesAsync();
                consolidadaId = consolidada.Id;
                delegacionId = propia.Id;
                filtroDelegacionId = delegacion.Id;
            }
            QuestPDF.Settings.License = LicenseType.Community;
            var pdf = Document.Create(d => d.Page(p => p.Content().Text("Documento de prueba"))).GeneratePdf();
            foreach (var nombre in new[] { "original.pdf", "delegacion.pdf", "sesion.pdf" })
                await File.WriteAllBytesAsync(Path.Combine(root, "pdf", nombre), pdf);
            IFormFile Upload(byte[] bytes, string name = "modificado.pdf") => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "archivo", name);

            async Task<List<ArchivoPdf>> TodosAsync()
            {
                var documentos = await service.ListarAsync(fecha.AddDays(-1), fecha, filtroDelegacionId);
                documentos.AddRange(await service.ListarAsync(fecha.AddDays(-1), fecha, consolidados: true));
                return documentos;
            }
            var lista = await TodosAsync();
            Check(lista.Count == 4, "Listado incluye sesiones y síntesis, sin repetir rutas ni entradas sin PDF");
            Check(lista.Count(d => d.Consolidado) == 1 && lista.Count(d => !d.Consolidado) == 3, "Separa consolidados de documentos de delegaciones");
            Check(lista.Where(d => !d.Consolidado).All(d => d.Delegacion == "Delegación de prueba"), "Conserva la delegación para agrupar");
            Check(lista.Single(d => d.Consolidado).Origen == "Generado por el sistema", "PDF anterior se identifica como generado");
            Check(lista.Count(d => !d.Disponible) == 1, "Evidencia archivos faltantes sin ofrecer una descarga rota");
            Check((await service.ListarAsync(fecha, fecha, filtroDelegacionId)).Count == 2, "Filtro por fecha de síntesis");
            Check((await service.ListarAsync(fecha.AddDays(-14), fecha, consolidados: true)).Count == 1, "Permite exactamente quince días inclusivos en consolidados");
            Check(!(await service.ListarAsync(fecha.AddDays(-14), fecha, filtroDelegacionId)).Any(d => d.Consolidado), "La consulta de delegación excluye consolidados");
            Check(!(await service.ListarAsync(fecha.AddDays(-14), fecha, 9999)).Any(), "La consulta no incluye otra delegación");
            await Rechazar(() => service.ListarAsync(fecha.AddDays(-15), fecha, filtroDelegacionId), "Bloquea dieciséis días en delegaciones");
            await Rechazar(() => service.ListarAsync(fecha.AddDays(-15), fecha, consolidados: true), "Bloquea dieciséis días en consolidados");
            await Rechazar(() => service.ListarAsync(fecha, fecha.AddDays(-1), consolidados: true), "Bloquea período invertido");
            await Rechazar(() => service.ListarAsync(fecha, fecha), "No permite consultas de todas las delegaciones");
            var total = service.ObtenerAlmacenamiento();
            Check(total.Cantidad == 3 && total.Bytes == 3L * pdf.Length, "Tamaño global cuenta archivos físicos, sin duplicar referencias ni incluir faltantes");
            Check(total.Megabytes == total.Bytes / 1_000_000m, "Convierte bytes a MB");
            Check(total.DesdeUtc.HasValue && total.HastaUtc >= total.DesdeUtc, "Informa fechas globales de archivos");
            var controllerMedios = new ArchivoPdfController(service, null!) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
            controllerMedios.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "MEDIOS") }, "test"));
            var inicial = (ViewResult)await controllerMedios.Index(null, null);
            Check(((List<ArchivoPdf>)inicial.Model!).Count == 0, "La pantalla inicial no consulta PDF sin seleccionar delegación");
            Check((string)controllerMedios.ViewBag.Desde == DateOnly.FromDateTime(DateTime.Today).AddDays(-14).ToString("yyyy-MM-dd"), "Predetermina últimos quince días");
            var fueraDeRango = (ViewResult)await controllerMedios.Index(fecha.AddDays(-15), fecha, "consolidados");
            Check(!controllerMedios.ModelState.IsValid && ((List<ArchivoPdf>)fueraDeRango.Model!).Count == 0, "La URL no evade el límite de fechas");
            Check(service.RutaFisica("/pdf/../secreto.pdf") == null && service.RutaFisica("/pdf/..\\secreto.pdf") == null, "Bloquea escapes de la carpeta PDF");
            Check(service.RutaFisica("/uploads/otro.pdf") == null, "No permite descargar otra carpeta");
            await Rechazar(() => service.ReemplazarAsync(consolidadaId, "/pdf/original.pdf", Upload("no es pdf"u8.ToArray())), "Rechaza contenido ajeno a PDF");
            await Rechazar(() => service.ReemplazarAsync(consolidadaId, "/pdf/original.pdf", Upload(pdf, "archivo.html")), "Rechaza otra extensión");
            await Rechazar(() => service.ReemplazarAsync(consolidadaId, "/pdf/original.pdf", Upload("%PDF-1.7 incompleto"u8.ToArray())), "Rechaza un PDF truncado sin final");
            var grande = new FormFile(Stream.Null, 0, ArchivoPdfService.MaxBytes + 1, "archivo", "grande.pdf");
            await Rechazar(() => service.ReemplazarAsync(consolidadaId, "/pdf/original.pdf", grande), "Limita el tamaño antes de leer el archivo");
            await Rechazar(() => service.ReemplazarAsync(delegacionId, "/pdf/delegacion.pdf", Upload(pdf)), "No reemplaza un PDF de delegación");
            await Rechazar(() => service.ReemplazarAsync(9999, "", Upload(pdf)), "No crea síntesis inexistentes");

            var antes = DateTime.UtcNow.AddSeconds(-1);
            await service.ReemplazarAsync(consolidadaId, "/pdf/original.pdf", Upload(pdf, "../../modificado.pdf"));
            var actual = (await TodosAsync()).Single(d => d.Consolidado);
            Check(actual.Origen == "Subido manualmente" && actual.FechaCargaUtc >= antes, "Registra origen y fecha de carga sin depender del nombre original");
            Check(actual.Disponible && File.ReadAllBytes(service.RutaFisica(actual.Ruta)!).SequenceEqual(pdf), "La descarga usa exactamente el PDF subido");
            Check(File.Exists(Path.Combine(root, "pdf", "original.pdf")), "Conserva el archivo anterior");
            Check(Directory.GetFiles(Path.Combine(root, "pdf", "respaldos")).Length == 1,
                "Respaldo independiente conserva el original aunque se regenere la síntesis más adelante");
            Check(actual.Estado == "Informada" && actual.FechaGeneracion == new DateTime(2026, 10, 16, 12, 0, 0), "Carga no modifica el estado ni la fecha de generación original");
            await Rechazar(() => service.ReemplazarAsync(consolidadaId, "/pdf/original.pdf", Upload(pdf)), "Bloquea reemplazo desde una página desactualizada");
            await service.ReemplazarAsync(consolidadaId, actual.Ruta, Upload(pdf));
            var segunda = (await TodosAsync()).Single(d => d.Consolidado);
            Check(segunda.Ruta != actual.Ruta && File.Exists(service.RutaFisica(actual.Ruta)), "Cada reemplazo conserva el anterior y usa una ruta única");
            var conRespaldos = service.ObtenerAlmacenamiento();
            Check(conRespaldos.Cantidad == 7 && conRespaldos.Bytes == 7L * pdf.Length, "Total del servidor incluye respaldos y versiones anteriores fuera del filtro");
            Check((await TodosAsync()).Count == 4, "Respaldos no se duplican en el listado vigente");
            Check(await service.ObtenerRutaAsync(consolidadaId, false) == service.RutaFisica(segunda.Ruta), "Descarga obtiene la última versión de la DB");

            using (var db1 = factory.Create())
            using (var db2 = factory.Create())
            {
                var primera = (await db1.Sintesis.FindAsync(consolidadaId))!;
                var otra = (await db2.Sintesis.FindAsync(consolidadaId))!;
                primera.PDFPath = "/pdf/concurrente.pdf";
                await db1.SaveChangesAsync();
                otra.PDFPath = "/pdf/perdida.pdf";
                try { await db2.SaveChangesAsync(); throw new InvalidOperationException("No detectó concurrencia"); }
                catch (DbUpdateConcurrencyException) { Check(true, "Concurrencia evita perder un reemplazo simultáneo"); }
            }
            foreach (var rol in new[] { "DELEGACION", "CONSULTA" })
            {
                var controller = new ArchivoPdfController(service, null!) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
                controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, rol) }, "test"));
                Check(await controller.Index(null, null) is ForbidResult, "Listado exclusivo MEDIOS: " + rol);
                Check(await controller.Descargar(consolidadaId) is ForbidResult, "Descarga exclusiva MEDIOS: " + rol);
                Check(await controller.Reemplazar(consolidadaId, segunda.Ruta, Upload(pdf)) is ForbidResult, "Reemplazo exclusivo MEDIOS: " + rol);
            }
            Check(typeof(ArchivoPdfController).GetMethod("Reemplazar")!.IsDefined(typeof(ValidateAntiForgeryTokenAttribute), true), "Carga requiere token antifalsificación");
            Check(ArchivoPdfService.FechaCargaUtc("/pdf/sintesis_matutina_20261016_1.pdf") == null, "PDF histórico no se confunde con una carga manual");
        }
        finally { Directory.Delete(root, true); }
        return count;
    }

    private sealed class PdfEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Medios.PdfChecks";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = "Testing";
    }
}
