using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
using Medios.Entities;
using Medios.Infrastructure.Data;
using Medios.Models;
using Microsoft.EntityFrameworkCore;

namespace Medios.Services;

public class GruposCorreoService
{
    private readonly IMediosDbContextFactory _factory;
    public GruposCorreoService(IMediosDbContextFactory factory) => _factory = factory;

    public async Task<List<GrupoCorreo>> ListarAsync(bool disponibles = false)
    {
        using var db = _factory.Create();
        var query = db.GruposCorreo.AsNoTracking().Include(g => g.Contactos).AsQueryable();
        if (disponibles) query = query.Where(g => g.Activo && g.Contactos.Any());
        return await query.OrderBy(g => g.Nombre).ToListAsync();
    }

    public async Task<GrupoCorreoModel?> ObtenerAsync(int id)
    {
        using var db = _factory.Create();
        var grupo = await db.GruposCorreo.AsNoTracking().Include(g => g.Contactos).SingleOrDefaultAsync(g => g.Id == id);
        return grupo == null ? null : new GrupoCorreoModel
        {
            Id = grupo.Id, Nombre = grupo.Nombre, Activo = grupo.Activo,
            Contactos = grupo.Contactos.OrderBy(c => c.Apellido).ThenBy(c => c.Nombre)
                .Select(c => new ContactoCorreoModel { Nombre = c.Nombre, Apellido = c.Apellido, Email = c.Email }).ToList()
        };
    }

    public async Task<int> GuardarAsync(GrupoCorreoModel modelo)
    {
        modelo.Nombre = (modelo.Nombre ?? "").Trim();
        modelo.Contactos ??= new();
        Validator.ValidateObject(modelo, new ValidationContext(modelo), true);
        var correos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var contacto in modelo.Contactos)
        {
            contacto.Nombre = (contacto.Nombre ?? "").Trim();
            contacto.Apellido = (contacto.Apellido ?? "").Trim();
            contacto.Email = (contacto.Email ?? "").Trim();
            Validator.ValidateObject(contacto, new ValidationContext(contacto), true);
            if (!MailAddress.TryCreate(contacto.Email, out var direccion) || direccion.Address != contacto.Email)
                throw new ValidationException("Ingresá solamente una dirección de correo por contacto.");
            if (!correos.Add(contacto.Email))
                throw new ValidationException("El mismo correo no puede repetirse dentro de un grupo.");
        }
        using var db = _factory.Create();
        var nombres = await db.GruposCorreo.Where(g => g.Id != modelo.Id).Select(g => g.Nombre).ToListAsync();
        if (nombres.Any(n => string.Equals(n, modelo.Nombre, StringComparison.OrdinalIgnoreCase)))
            throw new ValidationException("Ya existe un grupo con ese nombre.");
        var grupo = modelo.Id == 0 ? new GrupoCorreo() :
            await db.GruposCorreo.Include(g => g.Contactos).SingleOrDefaultAsync(g => g.Id == modelo.Id)
                ?? throw new KeyNotFoundException("El grupo ya no existe.");
        if (modelo.Id == 0) db.GruposCorreo.Add(grupo);
        foreach (var anterior in grupo.Contactos.Where(c => !correos.Contains(c.Email)).ToList())
        {
            db.ContactosCorreo.Remove(anterior);
            grupo.Contactos.Remove(anterior);
        }
        grupo.Nombre = modelo.Nombre;
        grupo.Activo = modelo.Activo;
        foreach (var c in modelo.Contactos)
        {
            var contacto = grupo.Contactos.FirstOrDefault(v => string.Equals(v.Email, c.Email, StringComparison.OrdinalIgnoreCase));
            if (contacto == null)
            {
                contacto = new ContactoCorreo();
                grupo.Contactos.Add(contacto);
            }
            contacto.Nombre = c.Nombre;
            contacto.Apellido = c.Apellido;
            contacto.Email = c.Email;
        }
        await db.SaveChangesAsync();
        return grupo.Id;
    }

    public async Task<bool> EliminarAsync(int id)
    {
        using var db = _factory.Create();
        var grupo = await db.GruposCorreo.Include(g => g.Contactos).SingleOrDefaultAsync(g => g.Id == id);
        if (grupo == null) return false;
        db.GruposCorreo.Remove(grupo);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<List<string>> ResolverDestinatariosAsync(IEnumerable<int> seleccionados)
    {
        var ids = seleccionados.Distinct().ToArray();
        if (ids.Length == 0) return new();
        using var db = _factory.Create();
        var grupos = await db.GruposCorreo.AsNoTracking().Include(g => g.Contactos)
            .Where(g => ids.Contains(g.Id)).ToListAsync();
        if (grupos.Count != ids.Length || grupos.Any(g => !g.Activo || g.Contactos.Count == 0))
            throw new ValidationException("Un grupo seleccionado ya no está disponible. Revisá los destinatarios antes de informar.");
        return grupos.SelectMany(g => g.Contactos).Select(c => c.Email.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<string> CombinarDestinatarios(IEnumerable<string> existentes, IEnumerable<string> grupos) =>
        existentes.Concat(grupos).Select(m => m.Trim()).Where(m => m.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
