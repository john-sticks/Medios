using Medios.Entities;
using Medios.Services;
using Medios.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

static class CategoriaChecks
{
    public static async Task<int> RunAsync()
    {
        var checks = 0;
        void Check(bool value, string name)
        {
            if (!value) throw new InvalidOperationException(name);
            checks++;
        }
        var factory = new MemoriaFactory();
        var service = new NotaService(factory);
        var nacional = new CategoriaNoticia { Id = 101, Nombre = "Ámbito Nacional", Orden = 1 };
        var provincial = new CategoriaNoticia { Id = 102, Nombre = "Ámbito Provincial", Orden = 2 };
        var partido = new CategoriaNoticia { Id = 103, Nombre = "Ámbito Partido", Orden = 3 };
        var hechos = new CategoriaNoticia { Id = 106, Nombre = "Hechos Delictuales", Orden = 6 };
        var instituciones = new CategoriaNoticia { Id = 104, Nombre = "Institucionales", Orden = 4 };
        var laPlata = new Partido { IdPartido = 1, Nombre = "La Plata" };
        using (var db = factory.Create())
        {
            db.CategoriasNoticia.AddRange(nacional, provincial, partido, hechos, instituciones,
                new CategoriaNoticia { Id = 200, Nombre = "Inactiva", Activa = false });
            db.Partidos.Add(laPlata);
            await db.SaveChangesAsync();
        }
        Check((await service.GetCategoriasAsync()).Any(c => c.Id == 101), "Nacional está disponible sin depender del ID 1");
        Check((await service.GetCategoriasAsync()).Any(c => c.Id == 102), "Provincial está disponible sin depender del ID 2");
        Check((await service.ValidarSeleccionCategoriaAsync(101, null)).Ambito == "Nacional", "Nacional no requiere partido");
        Check((await service.ValidarSeleccionCategoriaAsync(102, null)).Ambito == "Provincial", "Provincial no requiere partido");
        Check((await service.ValidarSeleccionCategoriaAsync(103, null)).Error != null, "Ámbito Partido requiere partido");
        Check((await service.ValidarSeleccionCategoriaAsync(106, null)).Error != null, "Las categorías comunes requieren partido");
        Check((await service.ValidarSeleccionCategoriaAsync(106, 1)).Ambito == "Partido", "Hechos Delictuales deriva ámbito Partido");
        Check((await service.ValidarSeleccionCategoriaAsync(103, 999)).Error != null, "No aceptar un partido inexistente");
        Check((await service.ValidarSeleccionCategoriaAsync(null, 1)).Error != null, "Categoría obligatoria en el servidor");
        Check((await service.ValidarSeleccionCategoriaAsync(200, 1)).Error != null, "No aceptar categoría inactiva");

        var descriptor = new ActionDescriptor();
        descriptor.RouteValues["action"] = "AgregarLibre";
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        var controller = new ControllerForChecks { TempData = new TempDataDictionary(http, new TempDataProvider()) };
        var action = new ActionContext(http, new RouteData(), descriptor, new ModelStateDictionary());
        var arguments = new Dictionary<string, object?> { ["categoriaId"] = 101, ["partidoId"] = null, ["ambitoNota"] = "Partido" };
        var context = new ActionExecutingContext(action, new List<IFilterMetadata>(), arguments, controller);
        var executed = false;
        await new CategoriaNotaFilter(service).OnActionExecutionAsync(context, () =>
        {
            executed = true;
            return Task.FromResult(new ActionExecutedContext(action, new List<IFilterMetadata>(), controller));
        });
        Check(executed && (string?)arguments["ambitoNota"] == "Nacional", "El servidor corrige un ámbito manipulado");
        arguments["categoriaId"] = 103;
        context = new ActionExecutingContext(action, new List<IFilterMetadata>(), arguments, controller);
        executed = false;
        await new CategoriaNotaFilter(service).OnActionExecutionAsync(context, () =>
        {
            executed = true;
            return Task.FromResult(new ActionExecutedContext(action, new List<IFilterMetadata>(), controller));
        });
        Check(!executed && context.Result is RedirectToActionResult, "No guardar una nota de Partido sin selección");

        var notas = new[]
        {
            new SintesisNota { Orden = 0, NotaVersion = new NotaVersion { Categoria = hechos, Partido = laPlata } },
            new SintesisNota { Orden = 1, NotaVersion = new NotaVersion { Categoria = partido, Partido = laPlata } },
            new SintesisNota { Orden = 2, NotaVersion = new NotaVersion { Categoria = provincial } },
            new SintesisNota { Orden = 3, NotaVersion = new NotaVersion { Categoria = nacional } },
            new SintesisNota { Orden = 4, NotaVersion = new NotaVersion { Categoria = instituciones, Partido = laPlata } }
        };
        var grupos = SintesisPdfModel.Agrupar(notas);
        Check(grupos.Select(g => g.Titulo).SequenceEqual(new[] { "ÁMBITO NACIONAL", "ÁMBITO PROVINCIAL", "ÁMBITO LA PLATA", "INSTITUCIONALES", "HECHOS DELICTUALES" }), "Orden Nacional, Provincial y partido elegido");
        Check(grupos[2].SoloLocalidad && grupos[2].Categorias.Single().Notas.Count == 1, "Ámbito Partido genérico antes de categorías en orden del modelo");
        Check(grupos.Take(2).All(g => g.Categorias.Single().Titulo == null), "Nacional/Provincial sin encabezado de categoría duplicado");
        Check(grupos.Sum(g => g.Categorias.Sum(c => c.Notas.Count)) == 5, "Conservar todas las notas en el modelo");
        Check(CategoriaNotaPolicy.AmbitoDeNota(new NotaVersion { Categoria = instituciones, AmbitoNota = "Nacional" }) == "Nacional", "Conservar ámbito explícito histórico");
        var procedimientos = new CategoriaNoticia { Nombre = "Procedimientos Policiales" };
        var mixtas = SintesisPdfModel.Agrupar(new[]
        {
            new SintesisNota { NotaVersion = new NotaVersion { Categoria = procedimientos, Partido = laPlata } },
            new SintesisNota { NotaVersion = new NotaVersion { Categoria = procedimientos, Partido = new Partido { Nombre = "Mar del Plata" } } }
        });
        Check(mixtas.Count == 1 && mixtas[0].Titulo == "PROCEDIMIENTOS POLICIALES"
            && mixtas[0].Categorias.Single().Notas.Count == 2 && !mixtas[0].SoloLocalidad,
            "Agrupar procedimientos de distintos partidos en un único bloque");
        Check(SintesisPdfModel.LocalidadDentroPartido(new NotaVersion { Partido = laPlata,
            Localidad = new Localidad { Nombre = "TOLOSA (LA PLATA)" } }) == "TOLOSA",
            "Mostrar solo localidad dentro del encabezado Ámbito La Plata");
        return checks;
    }
    private sealed class ControllerForChecks : Controller { }
    private sealed class TempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
