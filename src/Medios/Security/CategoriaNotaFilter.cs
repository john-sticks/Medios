using Medios.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Medios.Security;

// Validación común a todos los formularios de alta, edición y ampliación.
public sealed class CategoriaNotaFilter(NotaService notas) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!HttpMethods.IsPost(context.HttpContext.Request.Method)
            || !context.ActionArguments.ContainsKey("categoriaId"))
        {
            await next();
            return;
        }

        object? Argumento(string nombre) => context.ActionArguments.TryGetValue(nombre, out var valor) ? valor : null;
        var categoriaId = Argumento("categoriaId") as int?;
        var partidoId = Argumento("partidoId") as int?;
        var seleccion = await notas.ValidarSeleccionCategoriaAsync(categoriaId, partidoId);
        if (seleccion.Error == null)
        {
            context.ActionArguments["ambitoNota"] = seleccion.Ambito;
            await next();
            return;
        }

        var controller = (Controller)context.Controller;
        controller.TempData["Error"] = seleccion.Error;
        var action = context.ActionDescriptor.RouteValues["action"];
        var sesionId = Argumento("sesionId");
        var notaId = Argumento("notaId");
        context.Result = action switch
        {
            "AgregarLibre" or "Nueva" => new RedirectToActionResult("Listado", "Nota", null),
            "AgregarNota" or "AgregarNotaAnalista" => new RedirectToActionResult("Detalle", "Sesion", new { id = sesionId }),
            _ => new RedirectToActionResult(action, "Nota", new
            {
                id = notaId, sesionId,
                returnUrl = Argumento("returnUrl")
            })
        };
    }
}
