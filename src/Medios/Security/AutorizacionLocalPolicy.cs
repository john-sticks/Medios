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

    public static string? ResolverRol(AutorizacionUsuario? autorizacion, string? rolCerberus)
    {
        if (!PermiteAcceso(autorizacion)) return null;
        return string.Equals(rolCerberus?.Trim(), "SUPERVISOR", StringComparison.OrdinalIgnoreCase)
            ? "DESARROLLADOR" : autorizacion!.Rol!.Trim().ToUpperInvariant();
    }

    public static bool SesionVigente(ClaimsPrincipal usuario, AutorizacionUsuario? autorizacion)
    {
        if (!PermiteAcceso(autorizacion)) return false;

        var nombre = usuario.FindFirst("UsuarioReal")?.Value ?? usuario.FindFirst("Usuario")?.Value;
        var rol = usuario.FindFirst("RolReal")?.Value ?? usuario.FindFirst(ClaimTypes.Role)?.Value;
        var rolCerberus = usuario.FindFirst("RolCerberus")?.Value;
        var rolLocalAlIngresar = usuario.FindFirst("RolLocalAlIngresar")?.Value;
        var rolEsperado = ResolverRol(autorizacion, rolCerberus);
        // Conservar cookies anteriores; las nuevas registran el rol local para revocar cambios,
        // incluso cuando Supervisor determina el rol efectivo de desarrollador.
        if (rolLocalAlIngresar != null && !string.Equals(rolLocalAlIngresar, autorizacion!.Rol?.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(rolCerberus, "SUPERVISOR", StringComparison.OrdinalIgnoreCase) && rolLocalAlIngresar == null)
            return false;
        var delegacion = usuario.FindFirst("DelegacionIdReal")?.Value ?? usuario.FindFirst("DelegacionId")?.Value;
        var ambito = usuario.FindFirst("AmbitoIdReal")?.Value ?? usuario.FindFirst("AmbitoId")?.Value;

        return string.Equals(nombre, autorizacion!.Usuario, StringComparison.OrdinalIgnoreCase)
            && string.Equals(rol, rolEsperado, StringComparison.OrdinalIgnoreCase)
            && delegacion == autorizacion.DelegacionId?.ToString()
            && ambito == autorizacion.AmbitoId?.ToString();
    }
}
