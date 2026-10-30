using Medios.Controllers;
using Medios.Entities;
using Medios.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

static class PublicadasChecks
{
    public static async Task<int> RunAsync()
    {
        var count = 0;
        void Check(bool ok, string caso) { if (!ok) throw new InvalidOperationException(caso); count++; }
        var factory = new MemoriaFactory();
        var service = new SintesisService(factory, null!, null!);
        var fecha = new DateOnly(2026, 10, 16);
        using (var db = factory.Create())
        {
            db.Delegaciones.Add(new Delegacion { Id = 1, Nombre = "Destino" });
            db.Sintesis.AddRange(
                new Sintesis { Fecha = fecha, Tipo = "Matutina", Estado = "Informada", CanalTodosRoles = true },
                new Sintesis { Fecha = fecha.AddDays(-14), Tipo = "Vespertina", Estado = "Informada", CanalTodasDelegaciones = true },
                new Sintesis { Fecha = fecha.AddDays(-15), Tipo = "Matutina", Estado = "Informada", CanalTodosRoles = true },
                new Sintesis { Fecha = fecha, Tipo = "Especial", Estado = "Generada" },
                new Sintesis { Fecha = fecha, Tipo = "Especial", Estado = "Informada", DelegacionId = 1 },
                new Sintesis { Fecha = fecha, Tipo = "Ampliacion Matutina", Estado = "Informada" },
                new Sintesis { Fecha = fecha, Tipo = "Especial", Estado = "Informada", DelegacionesDestino = new List<SintesisDelegacionDestino> { new() { DelegacionId = 1 } } });
            await db.SaveChangesAsync();
        }
        var todas = await service.GetPublicadasAsync(fecha.AddDays(-14), fecha);
        Check(todas.Count == 4, "Solo consolidadas informadas dentro del período inclusivo");
        Check(todas.First().Fecha >= todas.Last().Fecha, "Ordena por fecha descendente");
        Check((await service.GetPublicadasAsync(fecha, fecha, tipo: "Matutina")).Count == 1, "Filtro de tipo se combina con fecha");
        Check((await service.GetPublicadasAsync(fecha, fecha, tipo: "Especiales")).Count == 2, "Especiales incluye ampliaciones");
        Check((await service.GetPublicadasAsync(fecha.AddDays(-14), fecha, true, 1)).Count == 3, "Respeta canales y destino de la delegación");
        Check((await service.GetPublicadasAsync(fecha.AddDays(-14), fecha, true, 2)).Count == 2, "No filtra publicaciones de otro destino");
        Check((await service.GetPublicadasAsync(fecha.AddDays(-14), fecha, true, null)).Count == 0, "Sin delegación asignada no expone publicaciones");
        foreach (var inicio in new[] { fecha.AddDays(-15), fecha.AddDays(1) })
        {
            try { await service.GetPublicadasAsync(inicio, fecha); throw new InvalidOperationException("Aceptó período inválido"); }
            catch (ArgumentException) { Check(true, "Servicio rechaza período inválido"); }
        }
        SintesisController Controller() => new(service, null!, null!, null!, null!, null!, null!, null!, new GruposCorreoService(factory))
            { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var controller = Controller();
        var inicial = (ViewResult)await controller.Publicadas();
        Check(((List<Sintesis>)inicial.Model!).Count == 0 && !(bool)controller.ViewBag.BusquedaRealizada, "No consulta documentos al entrar");
        Check((string)controller.ViewBag.Desde == DateOnly.FromDateTime(DateTime.Today).AddDays(-14).ToString("yyyy-MM-dd"), "Predetermina últimos quince días");
        foreach (var parametros in new[] { (desde: (DateOnly?)null, hasta: (DateOnly?)fecha), (desde: (DateOnly?)fecha.AddDays(-15), hasta: (DateOnly?)fecha) })
        {
            controller = Controller();
            var resultado = (ViewResult)await controller.Publicadas(desde: parametros.desde, hasta: parametros.hasta, buscar: true);
            Check(!controller.ModelState.IsValid && ((List<Sintesis>)resultado.Model!).Count == 0, "El servidor bloquea búsquedas sin fechas o de más de quince días");
        }
        controller = Controller();
        var filtradas = (ViewResult)await controller.Publicadas("Matutina", fecha, fecha, true);
        Check(controller.ModelState.IsValid && ((List<Sintesis>)filtradas.Model!).Count == 1, "Buscar aplica los filtros y devuelve resultados");
        controller = Controller();
        await controller.Publicadas("tipo inválido", fecha, fecha, true);
        Check(!controller.ModelState.IsValid, "Rechaza tipos inválidos por URL");
        return count;
    }
}
