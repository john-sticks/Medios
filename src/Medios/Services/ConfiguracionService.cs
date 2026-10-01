using Microsoft.EntityFrameworkCore;
using Medios.Entities;
using Medios.Infrastructure.Data;

namespace Medios.Services;

public class ConfiguracionService
{
    private readonly IMediosDbContextFactory _factory;

    public ConfiguracionService(IMediosDbContextFactory factory)
    {
        _factory = factory;
    }

    public async Task<string?> GetAsync(string clave)
    {
        using var db = _factory.Create();
        var cfg = await db.Configuraciones.FindAsync(clave);
        return cfg?.Valor;
    }

    public async Task SetAsync(string clave, string? valor)
    {
        using var db = _factory.Create();
        var cfg = await db.Configuraciones.FindAsync(clave);
        if (cfg == null)
        {
            db.Configuraciones.Add(new Configuracion { Clave = clave, Valor = valor });
        }
        else
        {
            cfg.Valor = valor;
        }
        await db.SaveChangesAsync();
    }

    public async Task<Dictionary<string, string?>> GetAllAsync()
    {
        using var db = _factory.Create();
        return await db.Configuraciones
            .ToDictionaryAsync(c => c.Clave, c => c.Valor);
    }
}
