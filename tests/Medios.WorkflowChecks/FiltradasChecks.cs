using System.Security.Claims;
using DocumentFormat.OpenXml.Packaging;
using Medios.Controllers;
using Medios.Entities;
using Medios.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

static class FiltradasChecks
{
    public static async Task<int> RunAsync()
    {
        int count = 0;
        void Check(bool ok, string mensaje) { if (!ok) throw new InvalidOperationException(mensaje); count++; }
        var factory = new MemoriaFactory();
        var notas = new NotaService(factory);
        var service = new SintesisService(factory, new Entorno(), notas);
        int origenId, notaId, viejoId, actualId, primero, segundo, tercero;
        var fechaInformada = new DateTime(2026, 10, 26, 12, 0, 0);
        using (var db = factory.Create())
        {
            var origen = new Sintesis { Estado = "Informada", Tipo = "Vespertina", Fecha = new DateOnly(2026,10,26), FechaInformada = fechaInformada,
                PDFPath = "/pdf/original.pdf", CanalTodosRoles = true, CanalMails = "previo@example.test" };
            foreach (var orden in new[] { 0, 1, 2 })
            {
                var nota = new NotaPrensa();
                var vieja = new NotaVersion { Nota = nota, Version = 1, FechaVersion = fechaInformada.AddDays(-2),
                    Titulo = "SNAPSHOT-" + orden, Texto = "Contenido congelado " + orden, Fuente = "QA", AmbitoNota = "Nacional", EstadoRevision = "Aprobada" };
                nota.VersionActual = new NotaVersion { Nota = nota, Version = 2, ParentVersion = vieja, FechaVersion = fechaInformada.AddDays(1),
                    Titulo = "ACTUAL-" + orden, Texto = "Otra versión posterior", EstadoRevision = "Sin Remitir", MotivoDescarte = "Rechazo posterior" };
                db.NotasVersion.Add(vieja); db.NotasPrensa.Add(nota);
                origen.NotasIncluidas.Add(new SintesisNota { Nota = nota, NotaVersion = vieja, Orden = orden });
            }
            db.Sintesis.Add(origen); await db.SaveChangesAsync(); origenId = origen.Id;
            var sn = origen.NotasIncluidas.OrderBy(sn => sn.Orden).ToList();
            primero = sn[0].Id; segundo = sn[1].Id; tercero = sn[2].Id;
            notaId = sn[0].NotaPrensaId; viejoId = sn[0].NotaVersionId; actualId = sn[0].Nota.VersionActualId!.Value;
        }
        Check((await service.CrearFiltradaAsync(origenId, Array.Empty<int>(), "medios")).Id == 0, "Rechazar selección vacía");
        Check((await service.CrearFiltradaAsync(origenId, new[] { int.MaxValue }, "medios")).Id == 0, "Rechazar nota ajena al origen");
        Check((await service.CrearFiltradaAsync(origenId + 1000, new[] { primero }, "medios")).Id == 0, "Rechazar origen inexistente");
        using (var db = factory.Create())
        {
            (await db.Sintesis.FindAsync(origenId))!.Estado = "Generada"; await db.SaveChangesAsync();
        }
        Check((await service.CrearFiltradaAsync(origenId, new[] { primero }, "medios")).Id == 0, "Solo derivar de origen informado");
        using (var db = factory.Create())
        {
            var origen = (await db.Sintesis.FindAsync(origenId))!; origen.Estado = "Informada"; origen.DelegacionId = 9; await db.SaveChangesAsync();
        }
        Check((await service.CrearFiltradaAsync(origenId, new[] { primero }, "medios")).Id == 0, "No derivar de síntesis individual de delegación");
        using (var db = factory.Create())
        {
            (await db.Sintesis.FindAsync(origenId))!.DelegacionId = null; await db.SaveChangesAsync();
        }
        var (id, error) = await service.CrearFiltradaAsync(origenId, new[] { tercero, primero, primero }, "medios");
        Check(id > 0 && error == null && id != origenId, "Crear publicación independiente con IDs seleccionados");
        var nueva = (await service.GetByIdAsync(id))!;
        Check(nueva.EsFiltrada && nueva.SintesisOrigenId == origenId && nueva.SintesisOrigen!.Estado == "Informada", "Conservar vínculo al origen");
        Check(nueva.Tipo == "Vespertina" && nueva.Fecha == new DateOnly(2026,10,26), "Conservar tipo y fecha sin cambiar los límites ordinarios");
        Check(nueva.NotasIncluidas.Count == 2 && nueva.NotasIncluidas.All(sn => sn.NotaVersion.Version == 1), "Tomar solo snapshots de origen, no las versiones actuales");
        Check(nueva.NotasIncluidas.OrderBy(sn => sn.Orden).First().NotaVersionId == viejoId, "Conservar orden del origen y quitar IDs repetidos");
        Check(nueva.Estado == "Borrador" && nueva.PDFPath == null && nueva.FechaInformada == null && nueva.CanalMails == null
            && !nueva.CanalTodosRoles && !nueva.CanalTodasDelegaciones && nueva.DelegacionesDestino.Count == 0, "No heredar PDF, envíos ni destinatarios previos");
        var origenDespues = (await service.GetByIdAsync(origenId))!;
        Check(origenDespues.Estado == "Informada" && origenDespues.PDFPath == "/pdf/original.pdf" && origenDespues.FechaInformada == fechaInformada
            && origenDespues.NotasIncluidas.Count == 3 && origenDespues.CanalMails == "previo@example.test", "Original y canales previos permanecen intactos");
        using (var db = factory.Create())
            Check(await db.NotasPrensa.CountAsync() == 3 && await db.NotasVersion.CountAsync() == 6, "No duplicar notas ni versiones");
        var bytes = await service.GenerarWordAsync(id);
        using (var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false))
        {
            var text = doc.MainDocumentPart!.Document.InnerText;
            Check(text.Contains("SÍNTESIS FILTRADA") && text.Contains("Origen #" + origenId), "Word identifica su origen");
            Check(text.Contains("SNAPSHOT-0") && text.Contains("SNAPSHOT-2") && !text.Contains("SNAPSHOT-1") && !text.Contains("ACTUAL-"), "Word conserva notas seleccionadas aunque la versión actual esté rechazada");
        }
        await notas.ActualizarReferenciaSintesisAsync(notaId);
        Check((await service.GetByIdAsync(id))!.NotasIncluidas.Single(sn => sn.NotaPrensaId == notaId).NotaVersionId == viejoId, "Editar una nota no cambia el snapshot de la filtrada");
        await notas.DescartarAsync(notaId, "medios", "Otro rechazo");
        Check((await service.GetByIdAsync(id))!.NotasIncluidas.Count == 2 && (await service.GetByIdAsync(origenId))!.NotasIncluidas.Count == 3, "Descartar versión posterior no elimina snapshots históricos");
        Check(await service.ConvertirABorradorAsync(id, "medios", null) == -1, "No convertir una filtrada en sesión ni mover notas originales");
        using (var db = factory.Create())
        {
            var s = (await db.Sintesis.FindAsync(id))!; s.Estado = "Generada"; s.PDFPath = "/pdf/filtrada.pdf"; await db.SaveChangesAsync();
        }
        Check(!await service.RemitirAsync(id), "La filtrada usa Informar, no el flujo de remisión de delegación");
        Check(await service.IniciarInformacionAsync(id) && !await service.IniciarInformacionAsync(id), "Reserva el envío sin permitir doble solicitud");
        Check(await service.InformarConCanalesAsync(id, true, null) && !await service.IniciarInformacionAsync(id), "Informar una sola vez con historial independiente");
        Check((await service.GetByIdAsync(origenId))!.FechaInformada == fechaInformada, "Informar filtrada no cambia fecha original");
        Check(!await service.EliminarConsolidadaAsync(id) && !await service.EliminarConsolidadaAsync(origenId), "No eliminar publicaciones ya informadas");
        var otra = (await service.CrearFiltradaAsync(origenId, new[] { segundo }, "medios")).Id;
        Check(otra > 0 && otra != id, "Permitir otra selección independiente del mismo origen");
        Check(await service.EliminarConsolidadaAsync(otra) && (await service.GetByIdAsync(origenId))!.NotasIncluidas.Count == 3, "Eliminar una filtrada pendiente conserva el origen");

        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role,"DELEGACION") }, "test")) };
        var controller = new SintesisController(service, null!, null!, null!, null!, null!, null!, notas)
        {
            ControllerContext = new ControllerContext { HttpContext = http }, TempData = new TempDataDictionary(http, new TempProvider())
        };
        Check(await controller.CrearFiltrada(origenId) is ForbidResult && await controller.CrearFiltradaPost(origenId, new() { primero }) is ForbidResult, "Delegación no accede ni crea por URL directa");
        Check(await controller.EliminarFiltrada(id) is ForbidResult, "Delegación no elimina filtradas");
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role,"MEDIOS") }, "test"));
        var pendiente = (await service.CrearFiltradaAsync(origenId, new[] { primero }, "medios")).Id;
        await controller.RevertirConsolidar(pendiente);
        Check((await service.GetByIdAsync(pendiente)) != null, "Revertir filtrada pendiente queda bloqueado antes de tocar sesiones" );
        await controller.RevertirConsolidar(id);
        Check((await service.GetByIdAsync(origenId))!.Estado == "Informada", "Revertir no afecta a la publicación de origen");
        using (var db = factory.Create())
            Check(db.Model.FindEntityType(typeof(Sintesis))!.FindNavigation(nameof(Sintesis.SintesisOrigen))!.ForeignKey.DeleteBehavior == DeleteBehavior.Restrict, "FK de origen impide borrado en cascada");
        var raizRepo = new DirectoryInfo(AppContext.BaseDirectory);
        while (raizRepo != null && !File.Exists(Path.Combine(raizRepo.FullName, "src", "Medios", "Medios.csproj"))) raizRepo = raizRepo.Parent;
        Check(raizRepo != null, "Encontrar configuración real de permisos");
        var menus = new Medios.Security.MenuXmlService(new Entorno { ContentRootPath = Path.Combine(raizRepo!.FullName, "src", "Medios") });
        foreach (var rol in new[] { "MEDIOS", "DESARROLLADOR", "DELEGACION" })
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, rol) }, "test"));
            var urls = menus.GetAllowedUrls(principal);
            foreach (var ruta in new[] { "/sintesis/crearfiltrada", "/sintesis/eliminarfiltrada" })
                Check(urls.Any(u => ruta.StartsWith(u)) == (rol != "DELEGACION"), "Ruta de filtradas autorizada solo para roles de gestión: " + rol + ruta);
        }
        return count;
    }

    private sealed class Entorno : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Medios";
        public string EnvironmentName { get; set; } = "Testing";
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    private sealed class TempProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
