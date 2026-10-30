using Medios.Entities;

namespace Medios.Security;

public static class PerfilUsuarioLocal
{
    static string Preferir(string? central, string? local) =>
        !string.IsNullOrWhiteSpace(central) ? central.Trim() : local ?? "";

    public static UserSession Combinar(string usuario, string token, AutorizacionUsuario? local, UserSession? perfil) => new()
    {
        Nombre = Preferir(perfil?.Nombre, Preferir(local?.Nombre, usuario)),
        Apellido = !string.IsNullOrWhiteSpace(perfil?.Nombre) ? perfil.Apellido.Trim() : "",
        Email = Preferir(perfil?.Email, local?.Email),
        Dependencia = Preferir(perfil?.Dependencia, local?.Destino),
        Jerarquia = Preferir(perfil?.Jerarquia, local?.Jerarquia),
        Legajo = Preferir(perfil?.Legajo, local?.Legajo),
        Telefono = Preferir(perfil?.Telefono, local?.Telefono),
        Rol = perfil?.Rol ?? "", Token = token
    };
}
