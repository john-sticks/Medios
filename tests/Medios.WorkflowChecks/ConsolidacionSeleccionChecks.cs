using System.Security.Claims;
using Medios.Controllers;
using Medios.Entities;
using Medios.Infrastructure.Data;
using Medios.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;

static class ConsolidacionSeleccionChecks
{
    public static async Task<int> RunAsync()
    {
        var count = 0;
        void Check(bool ok, string texto) { if (!ok) throw new InvalidOperationException("Selección: " + texto); count++; }
        var factory = new SeleccionFactory();
        using (var db = factory.Create())
        {
            db.Delegaciones.Add(new Delegacion { Id = 1, Nombre = "Delegación de prueba" });
            db.SesionesPrensas.AddRange(
                new SesionPrensa { Id = 1, Fecha = new DateOnly(2026, 10, 23), Turno = "Vespertina", Estado = "Finalizada", DelegacionId = 1 },
                new SesionPrensa { Id = 2, Fecha = new DateOnly(2026, 10, 27), Turno = "Ampliacion Vespertina", Estado = "Finalizada", DelegacionId = 1 },
                new SesionPrensa { Id = 3, Fecha = new DateOnly(2026, 10, 27), Turno = "Vespertina", Estado = "Remitida", DelegacionId = 1 },
                new SesionPrensa { Id = 4, Fecha = new DateOnly(2026, 10, 27), Turno = "Matutina", Estado = "Consolidada", DelegacionId = 1 });
            await db.SaveChangesAsync();
        }
        var service = new SintesisService(factory, null!, null!);
        SintesisController Controller(string rol = "MEDIOS")
        {
            var http = new DefaultHttpContext();
            http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, rol), new Claim(ClaimTypes.Name, "Operador")], "test"));
            return new(service, null!, null!, null!, null!, null!, null!, null!)
            {
                ControllerContext = new ControllerContext { HttpContext = http },
                TempData = new TempDataDictionary(http, new MemoriaTempData())
            };
        }
        var controller = Controller();
        var general = (ViewResult)await controller.Consolidar((string?)null);
        Check(((List<SesionPrensa>)general.Model!).Count == 2, "Acceso general conserva el listado de finalizadas");
        Check(controller.ViewBag.DesdeBandeja is false, "Acceso general no simula una selección de bandeja");
        controller = Controller();
        var una = (ViewResult)await controller.Consolidar(null, [2], true);
        Check(((List<SesionPrensa>)una.Model!).Select(s => s.Id).SequenceEqual([2]), "Una marcada no incluye la otra finalizada");
        Check(controller.ViewBag.DesdeBandeja is true, "La vista identifica el alcance de la selección");
        controller = Controller();
        var varias = (ViewResult)await controller.Consolidar(null, [1, 2], true);
        Check(((List<SesionPrensa>)varias.Model!).Count == 2, "Permite elegir diferentes fechas y tipos");
        controller = Controller();
        var repetida = (ViewResult)await controller.Consolidar(null, [2, 2], true);
        Check(((List<SesionPrensa>)repetida.Model!).Count == 1, "No duplica una sesión repetida en la URL");
        foreach (var ids in new List<int>[] { [], [0], [-1], [1, 3], [4], [999] })
        {
            controller = Controller();
            Check(await controller.Consolidar(null, ids, true) is RedirectResult { Url: "/Sesion/Bandeja#finalizadas" },
                "Selección vacía, inválida o desactualizada no amplía el alcance");
            Check(controller.TempData["Error"] is string, "Explica el motivo al volver a Finalizadas");
        }
        controller = Controller();
        controller.ModelState.AddModelError("sesionIds", "No es un entero");
        Check(await controller.Consolidar(null, null, true) is RedirectResult, "Rechaza IDs malformados en lugar de cargar todas");
        Check(await Controller("DELEGACION").Consolidar(null, [1], true) is ForbidResult, "Delegación no accede a consolidar por URL");
        controller = Controller();
        var enviada = await controller.Consolidar("2026-10-28", "Vespertina", "[1,3]");
        Check(enviada is RedirectResult { Url: "/Sesion/Bandeja#finalizadas" }, "El POST vuelve a comprobar el estado de todas las sesiones");
        controller = Controller();
        var errorOperador = (RedirectResult)await controller.Consolidar("2026-10-28", "Vespertina", "[2]", operador: new string('x', 101));
        Check(errorOperador.Url.Contains("desdeBandeja=true") && errorOperador.Url.Contains("sesionIds=2") && !errorOperador.Url.Contains("sesionIds=1"), "Un error de formulario conserva las sesiones marcadas");
        controller = Controller();
        var sinNotas = (RedirectResult)await controller.Consolidar("2026-10-28", "Vespertina", "[1]");
        Check(sinNotas.Url.Contains("sesionIds=1") && sinNotas.Url.Contains("desdeBandeja=true"), "No amplía el listado cuando faltan notas consolidables");
        using (var db = factory.Create())
        {
            Check(await db.Sintesis.CountAsync() == 0, "Una selección obsoleta no crea una consolidada parcial");
            Check((await db.SesionesPrensas.FindAsync(1))!.Estado == "Finalizada" && (await db.SesionesPrensas.FindAsync(3))!.Estado == "Remitida",
                "No marca como consolidadas sesiones ajenas o inválidas");
        }
        return count;
    }

    private sealed class SeleccionFactory : IMediosDbContextFactory
    {
        private readonly string nombre = Guid.NewGuid().ToString();
        public MediosDbContext Create() => new(new DbContextOptionsBuilder<MediosDbContext>().UseInMemoryDatabase(nombre).Options);
    }
    private sealed class MemoriaTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
