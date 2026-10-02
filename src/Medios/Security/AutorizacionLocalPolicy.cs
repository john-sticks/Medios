using Medios.Entities;
using System.Security.Claims;

namespace Medios.Security;

public static class AutorizacionLocalPolicy
{
    public static bool RolValido(string? rol) =>
        rol?.Trim().ToUpperInvariant() is "DESARROLLADOR" or "MEDIOS" or "DELEGACION";

    public static bool PermiteAcceso(AutorizacionUsuario? autorizacion) =>
        autorizacion is { Estado: "aprobada", Activo: true }
        && RolValido(autorizacion.Rol);

    public static bool SesionVigente(ClaimsPrincipal usuario, AutorizacionUsuario? autorizacion)
    {
        if (!PermiteAcceso(autorizacion)) return false;

        var nombre = usuario.FindFirst("UsuarioReal")?.Value ?? usuario.FindFirst("Usuario")?.Value;
        var rol = usuario.FindFirst("RolReal")?.Value ?? usuario.FindFirst(ClaimTypes.Role)?.Value;
        var delegacion = usuario.FindFirst("DelegacionIdReal")?.Value ?? usuario.FindFirst("DelegacionId")?.Value;
        var ambito = usuario.FindFirst("AmbitoIdReal")?.Value ?? usuario.FindFirst("AmbitoId")?.Value;

        return string.Equals(nombre, autorizacion!.Usuario, StringComparison.OrdinalIgnoreCase)
            && string.Equals(rol, autorizacion.Rol?.Trim(), StringComparison.OrdinalIgnoreCase)
            && delegacion == autorizacion.DelegacionId?.ToString()
            && ambito == autorizacion.AmbitoId?.ToString();
    }
}
