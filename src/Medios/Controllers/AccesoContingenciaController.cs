using System.Security.Claims;
using Medios.Entities;
using Medios.Security;
using Medios.Services.Respaldos;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Medios.Controllers;

[AllowAnonymous]
[Route("Login")]
public class AccesoContingenciaController(RespaldoStore store, AccesoEmergenciaService acceso) : Controller
{
    [HttpGet("Contingencia")]
    public async Task<IActionResult> Index()
    {
        if (!store.Habilitado) return NotFound();
        ViewBag.Activa = (await store.LeerAsync()).ContingenciaActiva;
        return View("~/Views/Respaldos/Acceso.cshtml");
    }
    [HttpPost("Contingencia"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Entrar(string usuario, string password)
    {
        if (!store.Habilitado) return NotFound(); if (!Request.IsHttps) return BadRequest("El acceso de contingencia requiere HTTPS.");
        var user = await acceso.ValidarAsync(usuario ?? "", password ?? "");
        if (user == null) { ViewBag.Error = "No se pudo iniciar sesión. Verificá la credencial de emergencia o consultá al desarrollador."; ViewBag.Activa = (await store.LeerAsync()).ContingenciaActiva; return View("~/Views/Respaldos/Acceso.cshtml"); }
        await SignIn(user);
        var state = await store.LeerAsync();
        return Redirect(state.Accesos.Single(a => a.Usuario.Equals(user.Usuario, StringComparison.OrdinalIgnoreCase)).CambiarClave ? "/Login/CambiarClaveEmergencia" : "/Home");
    }
    [HttpGet("CambiarClaveEmergencia")]
    public IActionResult Cambiar()
    {
        if (!store.Habilitado || !User.HasClaim(c => c.Type == "BackupEmergencyEpoch")) return NotFound();
        return View("~/Views/Respaldos/CambiarClave.cshtml");
    }
    [HttpPost("CambiarClaveEmergencia"), ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarClave(string password, string repetir)
    {
        if (!store.Habilitado || !Request.IsHttps || !User.HasClaim(c => c.Type == "BackupEmergencyEpoch")) return NotFound();
        if (password != repetir) { ViewBag.Error = "Las claves no coinciden."; return View("~/Views/Respaldos/CambiarClave.cshtml"); }
        try
        {
            var username = User.FindFirst("UsuarioReal")!.Value;
            await acceso.CambiarClaveAsync(username, password ?? ""); await SignIn((await acceso.UsuarioAsync(username))!);
            return Redirect("/Home");
        }
        catch (InvalidOperationException e) { ViewBag.Error = e.Message; return View("~/Views/Respaldos/CambiarClave.cshtml"); }
    }
    async Task SignIn(AutorizacionUsuario user)
    {
        var state = await store.LeerAsync(); var access = state.Accesos.Single(a => a.Usuario.Equals(user.Usuario, StringComparison.OrdinalIgnoreCase));
        var claims = new List<Claim>{new(ClaimTypes.Name,user.Nombre),new(ClaimTypes.Role,user.Rol!),
            new("Usuario",user.Usuario),new("UsuarioReal",user.Usuario),new("RolReal",user.Rol!),new("RolCerberus",user.Rol!),
            new("NombreSolo",user.Nombre),new("Apellido",""),new("NombreCompletoReal",user.Nombre),new("NombreSoloReal",user.Nombre),new("ApellidoReal",""),
            new("Dependencia",user.Destino??""),new("DependenciaReal",user.Destino??""),new("Email",user.Email??""),new("EmailReal",user.Email??""),
            new("Jerarquia",user.Jerarquia??""),new("Legajo",user.Legajo??""),new("Telefono",user.Telefono??""),new("Token","contingencia-local"),
            new("BackupEmergencyEpoch",state.EpocaContingencia),new("BackupAccessRevision",access.Revision)};
        if (access.CambiarClave) claims.Add(new("CambioClaveEmergencia", "true"));
        if (user.DelegacionId.HasValue) { claims.Add(new("DelegacionId", user.DelegacionId.Value.ToString())); claims.Add(new("DelegacionIdReal", user.DelegacionId.Value.ToString())); }
        if (user.AmbitoId.HasValue) { claims.Add(new("AmbitoId", user.AmbitoId.Value.ToString())); claims.Add(new("AmbitoIdReal", user.AmbitoId.Value.ToString())); }
        if (user.Rol == "DESARROLLADOR") claims.Add(new(ClaimTypes.Role, "MEDIOS"));
        await HttpContext.SignInAsync("Cookies", new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies")), new AuthenticationProperties { IsPersistent = false, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30) });
        await store.Cambiar(s => { RespaldoStore.Evento(s, user.Usuario, "Inicio de sesión de emergencia"); return true; });
    }
}
