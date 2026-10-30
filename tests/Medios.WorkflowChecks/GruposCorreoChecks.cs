using System.ComponentModel.DataAnnotations;
using Medios.Entities;
using Medios.Models;
using Medios.Services;
using Microsoft.EntityFrameworkCore;

static class GruposCorreoChecks
{
    public static async Task<int> RunAsync()
    {
        var count = 0;
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); count++; }
        var factory = new MemoriaFactory();
        var service = new GruposCorreoService(factory);
        ContactoCorreoModel Contacto(string email) => new() { Nombre = "Ana", Apellido = "Prueba", Email = email };
        var modelo = new GrupoCorreoModel { Nombre = " Prensa ", Contactos = new() { Contacto("uno@ejemplo.invalid") } };
        var id = await service.GuardarAsync(modelo);
        Check(id > 0 && (await service.ObtenerAsync(id))!.Nombre == "Prensa", "Crear y normalizar nombre del grupo");
        Check((await service.ListarAsync(true)).Single().Contactos.Count == 1, "Grupo activo con contactos disponible al informar");
        var contactoId = (await service.ListarAsync()).Single().Contactos.Single().Id;
        modelo.Id = id;
        modelo.Contactos[0].Nombre = "Andrea";
        await service.GuardarAsync(modelo);
        Check((await service.ListarAsync()).Single().Contactos.Single().Id == contactoId, "Editar conserva el contacto y evita duplicados por reemplazo");
        modelo.Contactos.Add(Contacto("UNO@ejemplo.invalid"));
        try { await service.GuardarAsync(modelo); throw new InvalidOperationException("Aceptó duplicado"); }
        catch (ValidationException) { Check(true, "Rechazar correo repetido sin importar mayúsculas"); }
        Check((await service.ObtenerAsync(id))!.Contactos.Count == 1, "Validación no cambia el grupo guardado");
        modelo.Contactos = new() { Contacto("correo-invalido") };
        try { await service.GuardarAsync(modelo); throw new InvalidOperationException("Aceptó correo inválido"); }
        catch (ValidationException) { Check(true, "Validar formato de correo"); }
        var otro = new GrupoCorreoModel { Nombre = "Autoridades", Contactos = new() { Contacto("UNO@ejemplo.invalid"), Contacto("dos@ejemplo.invalid") } };
        var otroId = await service.GuardarAsync(otro);
        Check((await service.ResolverDestinatariosAsync(new[] { id, otroId, id })).Count == 2, "Deduplicar integrantes entre grupos e IDs repetidos");
        var unidos = GruposCorreoService.CombinarDestinatarios(new[] { "UNO@ejemplo.invalid", "tres@ejemplo.invalid" }, await service.ResolverDestinatariosAsync(new[] { id, otroId }));
        Check(unidos.Count == 3, "No duplicar correos entre grupos, delegaciones y entradas manuales");
        otro.Id = otroId; otro.Activo = false; await service.GuardarAsync(otro);
        Check((await service.ListarAsync(true)).Count == 1, "No mostrar grupos inactivos al informar");
        foreach (var inexistente in new[] { otroId, 9999 })
        {
            try { await service.ResolverDestinatariosAsync(new[] { id, inexistente }); throw new InvalidOperationException("Aceptó grupo inválido"); }
            catch (ValidationException) { Check(true, "Abortar selección completa si un grupo se eliminó o desactivó"); }
        }
        Check((await service.ResolverDestinatariosAsync(Array.Empty<int>())).Count == 0, "No agregar grupos por defecto");
        modelo.Contactos.Clear();
        await service.GuardarAsync(modelo);
        Check((await service.ListarAsync(true)).Count == 0, "No ofrecer grupos vacíos");
        using (var db = factory.Create())
            Check(!await db.ContactosCorreo.AnyAsync(c => c.GrupoCorreoId == id), "Quitar contactos del grupo");
        Check(await service.EliminarAsync(otroId), "Eliminar grupo");
        using (var db = factory.Create())
            Check(!await db.ContactosCorreo.AnyAsync(c => c.GrupoCorreoId == otroId), "Eliminar contactos solo de ese grupo");
        Check(await service.ObtenerAsync(otroId) == null && (await service.ListarAsync()).Count == 1, "Conservar otros grupos");
        return count;
    }
}
